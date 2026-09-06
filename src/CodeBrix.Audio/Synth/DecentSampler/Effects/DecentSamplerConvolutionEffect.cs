using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Dsp;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Samples;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The convolution effect: uniformly partitioned overlap-save convolution with an impulse response read
// from a WAV or AIFF file beside the preset.
//
// FROM THE GUIDE: irFile is required and is "the path of the WAV or AIFF to use as an Impulse Response
// (IR) file"; mix is the wet/dry crossfade, default 0.5, where 1.0 is just convolution. The guide also
// warns that a long impulse response costs substantial CPU, which is exactly what partition count
// means here.
//
// THE TRANSFORM. The partition length is the engine's own block size, so the algorithm has ZERO
// latency when the effect is driven a whole block at a time, which is how the engine drives it: each
// block of B new samples is transformed with the previous B (an N = 2B overlap-save frame), multiplied
// against every partition of the impulse response held in a frequency-domain delay line, and the last
// B samples of the inverse transform are the output. A caller that renders in some other size gets the
// same audio delayed by up to one block.
//
// A MONO impulse response is applied to both channels. A STEREO one applies its left channel to the
// left and its right to the right - the true-stereo form. An impulse response recorded at a different
// sample rate is resampled linearly when it is loaded.
//
// FX_IR_FILE REBINDING. The parameter is bindable, so a preset can switch impulse responses from a
// menu. Reading a file is not something a render callback may do, so the change is NOTICED on the
// audio thread and the load runs on the thread pool; the new response is swapped in atomically when it
// is ready and the old one keeps playing until then.
internal sealed class DecentSamplerConvolutionEffect : DecentSamplerEffectBase, IThreadPoolWorkItem
{
    // Longer than this and the partition count makes the effect useless in real time; the tail beyond
    // it is dropped and the drop is reported.
    private const double MaximumImpulseSeconds = 10.0;

    private readonly List<string> _problems = [];
    private readonly object _loadGate = new object();

    private FftProcessor _fft;
    private Complex[] _blockSpectrum;
    private Complex[] _accumulator;
    private float[] _frame;
    private float[] _transformed;
    private float[] _historyLeft;
    private float[] _historyRight;
    private float[] _dryLeft;
    private float[] _dryRight;

    private Complex[][] _delayLineLeft;
    private Complex[][] _delayLineRight;
    private int _delayLineHead;
    private int _partitionCapacity;

    private int _blockFrames;
    private int _fftSize;
    private int _bins;

    private ImpulseResponse _active;
    private string _requestedPath;
    private int _loading;

    private float _mix;

    public DecentSamplerConvolutionEffect(EffectContext context)
        : base(context, "FX_MIX", "FX_IR_FILE")
    {
        _requestedPath = Effect.IrFile;
        _active = Load(Effect.IrFile, _problems);
    }

    // Everything that could not be honoured while loading an impulse response. The chain drains this
    // into the synthesizer's own Problems once, after the effect is built.
    public IReadOnlyList<string> Problems => _problems;

    // How many partitions the loaded impulse response occupies, which is what its CPU cost is
    // proportional to. Zero when nothing loaded.
    public int PartitionCount => _active == null ? 0 : _active.Partitions;

    // The file the loaded impulse response came from, or null.
    public string ImpulsePath => _active == null ? null : _active.Path;

    // Whether a background reload is in flight. Only the tests wait on it.
    public bool IsLoading => Volatile.Read(ref _loading) != 0;

    protected override void OnPrepare()
    {
        _blockFrames = Math.Max(1, BlockSize);
        _fftSize = NextPowerOfTwo(_blockFrames * 2);
        _bins = _fftSize / 2 + 1;

        _fft = new FftProcessor(_fftSize);
        _blockSpectrum = new Complex[_bins];
        _accumulator = new Complex[_bins];
        _frame = new float[_fftSize];
        _transformed = new float[_fftSize];
        _historyLeft = new float[_fftSize];
        _historyRight = new float[_fftSize];
        _dryLeft = new float[_blockFrames];
        _dryRight = new float[_blockFrames];

        // Room for the impulse response that is loaded plus a generous margin, so a rebind to a longer
        // file does not have to allocate on the audio thread.
        var wanted = Math.Max(8, PartitionCount * 2);
        _partitionCapacity = wanted;
        _delayLineLeft = NewDelayLine(wanted, _bins);
        _delayLineRight = NewDelayLine(wanted, _bins);
        _delayLineHead = 0;
    }

    protected override void OnReset()
    {
        if (_historyLeft == null)
        {
            return;
        }

        Array.Clear(_historyLeft, 0, _historyLeft.Length);
        Array.Clear(_historyRight, 0, _historyRight.Length);

        for (var i = 0; i < _delayLineLeft.Length; i++)
        {
            Array.Clear(_delayLineLeft[i], 0, _delayLineLeft[i].Length);
            Array.Clear(_delayLineRight[i], 0, _delayLineRight[i].Length);
        }

        _delayLineHead = 0;
    }

    protected override void Refresh(bool force)
    {
        _mix = (float)Math.Clamp(Effect.Mix ?? 0.5, 0.0, 1.0);

        var path = Effect.IrFile;

        if (string.Equals(path, _requestedPath, StringComparison.Ordinal))
        {
            return;
        }

        // A binding moved FX_IR_FILE. Notice it here, load it there.
        _requestedPath = path;

        if (Interlocked.CompareExchange(ref _loading, 1, 0) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var impulse = Volatile.Read(ref _active);

        if (impulse == null)
        {
            return;
        }

        var offset = 0;
        while (offset < frames)
        {
            var take = Math.Min(_blockFrames, frames - offset);

            Array.Copy(left, offset, _dryLeft, 0, take);
            Array.Copy(right, offset, _dryRight, 0, take);

            if (take < _blockFrames)
            {
                Array.Clear(_dryLeft, take, _blockFrames - take);
                Array.Clear(_dryRight, take, _blockFrames - take);
            }

            ProcessOneBlock(impulse, left, right, offset, take);

            offset += take;
        }
    }

    void IThreadPoolWorkItem.Execute()
    {
        try
        {
            // Loop, because the path may have moved again while this load was running.
            while (true)
            {
                var wanted = Volatile.Read(ref _requestedPath);

                if (string.Equals(wanted, ImpulsePath, StringComparison.Ordinal))
                {
                    return;
                }

                var problems = new List<string>();
                var loaded = Load(wanted, problems);

                lock (_loadGate)
                {
                    foreach (var problem in problems)
                    {
                        _problems.Add(problem);
                    }
                }

                Volatile.Write(ref _active, loaded);

                if (string.Equals(Volatile.Read(ref _requestedPath), wanted, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape a thread-pool work item, and the instrument this reads through can be
            // disposed while a load is in flight. The old impulse response keeps playing.
            lock (_loadGate)
            {
                _problems.Add("convolution impulse response failed to load: " + exception.Message);
            }
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    private void ProcessOneBlock(ImpulseResponse impulse, float[] left, float[] right, int offset, int frames)
    {
        var b = _blockFrames;

        SlideHistory(_historyLeft, _dryLeft, b);
        SlideHistory(_historyRight, _dryRight, b);

        var partitions = Math.Min(impulse.Partitions, _partitionCapacity);

        // The head advances first, so slot 0 always holds the newest transform.
        _delayLineHead = _delayLineHead == 0 ? _partitionCapacity - 1 : _delayLineHead - 1;

        _fft.RealForward(_historyLeft, _blockSpectrum);
        Array.Copy(_blockSpectrum, _delayLineLeft[_delayLineHead], _bins);

        _fft.RealForward(_historyRight, _blockSpectrum);
        Array.Copy(_blockSpectrum, _delayLineRight[_delayLineHead], _bins);

        Convolve(_delayLineLeft, impulse.Left, partitions);
        _fft.RealInverse(_accumulator, _transformed);
        Blend(left, offset, frames, _dryLeft, b);

        Convolve(_delayLineRight, impulse.Right, partitions);
        _fft.RealInverse(_accumulator, _transformed);
        Blend(right, offset, frames, _dryRight, b);
    }

    private void Convolve(Complex[][] delayLine, Complex[][] impulse, int partitions)
    {
        Array.Clear(_accumulator, 0, _bins);

        for (var p = 0; p < partitions; p++)
        {
            var slot = _delayLineHead + p;
            if (slot >= _partitionCapacity)
            {
                slot -= _partitionCapacity;
            }

            var x = delayLine[slot];
            var h = impulse[p];

            for (var k = 0; k < _bins; k++)
            {
                var xr = x[k].X;
                var xi = x[k].Y;
                var hr = h[k].X;
                var hi = h[k].Y;

                _accumulator[k].X += xr * hr - xi * hi;
                _accumulator[k].Y += xr * hi + xi * hr;
            }
        }
    }

    // Overlap-save: the useful output is the last block of the inverse transform.
    private void Blend(float[] destination, int offset, int frames, float[] dry, int blockFrames)
    {
        var mix = _mix;
        var dryGain = 1f - mix;
        var start = _fftSize - blockFrames;

        for (var i = 0; i < frames; i++)
        {
            destination[offset + i] = dry[i] * dryGain + _transformed[start + i] * mix;
        }
    }

    private static void SlideHistory(float[] history, float[] block, int blockFrames)
    {
        Array.Copy(history, blockFrames, history, 0, history.Length - blockFrames);
        Array.Copy(block, 0, history, history.Length - blockFrames, blockFrames);
    }

    private static Complex[][] NewDelayLine(int partitions, int bins)
    {
        var line = new Complex[partitions][];
        for (var i = 0; i < partitions; i++)
        {
            line[i] = new Complex[bins];
        }

        return line;
    }

    private static int NextPowerOfTwo(int value)
    {
        var size = 2;
        while (size < value)
        {
            size *= 2;
        }

        return size;
    }

    // Reads the file, resamples it if it was recorded at another rate, and transforms it into the
    // partitioned spectra the render path multiplies against. Runs off the audio thread.
    private ImpulseResponse Load(string path, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            problems.Add("the convolution effect has no irFile");
            return null;
        }

        var instrument = Context.Instrument;

        if (instrument == null)
        {
            problems.Add($"the convolution effect cannot resolve '{path}' without an instrument");
            return null;
        }

        var source = instrument.LoadAuxiliaryFile(path, out var problem);

        if (source == null)
        {
            problems.Add($"convolution impulse response: {problem}");
            return null;
        }

        var blockFrames = Math.Max(1, BlockSize);
        var fftSize = NextPowerOfTwo(blockFrames * 2);
        var bins = fftSize / 2 + 1;

        var left = ReadChannel(source, 0, SampleRate, problems, path);
        var right = source.ChannelCount > 1 ? ReadChannel(source, 1, SampleRate, problems, path) : left;

        if (left == null || left.Length == 0)
        {
            problems.Add($"convolution impulse response is empty: {path}");
            return null;
        }

        var partitions = (left.Length + blockFrames - 1) / blockFrames;

        return new ImpulseResponse
        {
            Path = path,
            Partitions = partitions,
            Left = Partition(left, partitions, blockFrames, fftSize, bins),
            Right = ReferenceEquals(left, right)
                ? null
                : Partition(right, partitions, blockFrames, fftSize, bins),
        }.Mirrored();
    }

    private static float[] ReadChannel(
        ISampleSource source, int channel, int sampleRate, List<string> problems, string path)
    {
        var frames = source.Frames;
        var limit = (long)(MaximumImpulseSeconds * source.SampleRate);

        if (frames > limit)
        {
            problems.Add(
                $"convolution impulse response is longer than {MaximumImpulseSeconds:0} seconds and was " +
                $"truncated: {path}");
            frames = limit;
        }

        var raw = new float[frames];
        source.Read(channel, 0, raw);

        if (source.SampleRate == sampleRate)
        {
            return raw;
        }

        // Linear resampling is enough for an impulse response: the error it introduces is far below
        // the difference between one room recording and another.
        var ratio = (double)sampleRate / source.SampleRate;
        var length = Math.Max(1, (int)(raw.Length * ratio));
        var resampled = new float[length];

        for (var i = 0; i < length; i++)
        {
            var position = i / ratio;
            var index = (int)position;
            var fraction = (float)(position - index);
            var a = index < raw.Length ? raw[index] : 0f;
            var b = index + 1 < raw.Length ? raw[index + 1] : 0f;
            resampled[i] = a + (b - a) * fraction;
        }

        return resampled;
    }

    // The forward transform this package applies carries a 1/N scale, so the round trip through a
    // spectrum product needs an N back. Folding it into the impulse response spectra once means the
    // render path never pays for it.
    private static Complex[][] Partition(
        float[] impulse, int partitions, int blockFrames, int fftSize, int bins)
    {
        var fft = new FftProcessor(fftSize);
        var frame = new float[fftSize];
        var spectra = new Complex[partitions][];

        for (var p = 0; p < partitions; p++)
        {
            Array.Clear(frame, 0, frame.Length);

            var start = p * blockFrames;
            var take = Math.Min(blockFrames, impulse.Length - start);
            if (take > 0)
            {
                Array.Copy(impulse, start, frame, 0, take);
            }

            var spectrum = new Complex[bins];
            fft.RealForward(frame, spectrum);

            for (var k = 0; k < bins; k++)
            {
                spectrum[k].X *= fftSize;
                spectrum[k].Y *= fftSize;
            }

            spectra[p] = spectrum;
        }

        return spectra;
    }

    private sealed class ImpulseResponse
    {
        public string Path;
        public int Partitions;
        public Complex[][] Left;
        public Complex[][] Right;

        // A mono impulse response drives both channels from the same spectra.
        public ImpulseResponse Mirrored()
        {
            Right ??= Left;
            return this;
        }
    }
}

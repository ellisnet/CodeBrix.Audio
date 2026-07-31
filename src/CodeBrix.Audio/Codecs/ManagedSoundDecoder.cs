using System;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Base class for a fully managed decoder that plugs into the audio engine: it adapts a decoder
/// producing audio at the file's own channel count and sample rate to the format the engine asked
/// for.
/// </summary>
/// <remarks>
/// <para>
/// The engine hands every decoder a target format - normally the output device's - and expects
/// samples back in it. The native decoder does that conversion in C; managed decoders derive from
/// this class instead of each growing their own copy. An add-on package adding a codec only has to
/// supply <see cref="ReadSourceSamples"/>, <see cref="SeekSource"/> and <see cref="DisposeCore"/>,
/// then call <see cref="Initialize"/> once it knows the file's format.
/// </para>
/// <para>
/// Rate conversion is linear interpolation, with the fractional read position carried across
/// calls so chunk boundaries introduce no discontinuity. That is good enough for a fallback path
/// and honest about what it is, but it is not the equal of the native converter, so these
/// decoders are registered below the native factory rather than in front of it.
/// </para>
/// </remarks>
public abstract class ManagedSoundDecoder : ISoundDecoder
{
    private readonly object syncLock = new object();

    private int sourceChannels;
    private int sourceSampleRate;

    private float[] sourceBuffer = [];
    private float[] previousFrame = [];
    private float[] nextFrame = [];
    private double phase;
    private bool primed;
    private bool sourceExhausted;
    private bool finished;
    private bool endOfStreamRaised;

    /// <summary>Creates the decoder for a requested output format.</summary>
    /// <param name="channels">Channel count to produce, or 0 to adopt the source's.</param>
    /// <param name="sampleRate">Sample rate to produce, or 0 to adopt the source's.</param>
    protected ManagedSoundDecoder(int channels, int sampleRate)
    {
        if (channels < 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate < 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        RequestedChannels = channels;
        RequestedSampleRate = sampleRate;
    }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public int Length { get; private set; }

    /// <inheritdoc />
    /// <remarks>The managed decoders all produce 32-bit float, the engine's mixing format.</remarks>
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc />
    public int Channels { get; private set; }

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs> EndOfStreamReached;

    /// <summary>The output format requested at construction; 0 means "adopt the source's".</summary>
    protected int RequestedChannels { get; }

    /// <summary>The output rate requested at construction; 0 means "adopt the source's".</summary>
    protected int RequestedSampleRate { get; }

    /// <summary>
    /// Completes construction once the derived class knows what the file actually contains.
    /// </summary>
    /// <param name="channels">The source's channel count.</param>
    /// <param name="sampleRate">The source's sample rate.</param>
    /// <param name="totalFrames">The source's total frame count, or 0 when unknown.</param>
    protected void Initialize(int channels, int sampleRate, long totalFrames)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        sourceChannels = channels;
        sourceSampleRate = sampleRate;

        Channels = RequestedChannels > 0 ? RequestedChannels : channels;
        SampleRate = RequestedSampleRate > 0 ? RequestedSampleRate : sampleRate;

        previousFrame = new float[channels];
        nextFrame = new float[channels];
        sourceBuffer = new float[channels];

        var targetFrames = sourceSampleRate == SampleRate
            ? totalFrames
            : (long)(totalFrames * (double)SampleRate / sourceSampleRate);

        Length = (int)Math.Min(int.MaxValue, targetFrames * Channels);
    }

    /// <summary>Reads interleaved samples at the SOURCE format.</summary>
    /// <param name="destination">Buffer to fill.</param>
    /// <returns>The number of samples written; 0 at the end of the stream.</returns>
    protected abstract int ReadSourceSamples(Span<float> destination);

    /// <summary>Seeks the underlying decoder to a source frame index.</summary>
    /// <param name="frameIndex">The frame to move to.</param>
    /// <returns>True when the seek succeeded.</returns>
    protected abstract bool SeekSource(long frameIndex);

    /// <summary>Releases the underlying decoder.</summary>
    protected abstract void DisposeCore();

    /// <inheritdoc />
    public int Decode(Span<float> samples)
    {
        lock (syncLock)
        {
            if (IsDisposed) return 0;

            var framesWanted = samples.Length / Channels;
            if (framesWanted == 0) return 0;

            var framesWritten = sourceSampleRate == SampleRate
                ? DecodeAtSourceRate(samples, framesWanted)
                : DecodeResampled(samples, framesWanted);

            if (framesWritten == 0 && !endOfStreamRaised)
            {
                endOfStreamRaised = true;
                EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            }

            return framesWritten * Channels;
        }
    }

    /// <inheritdoc />
    public bool Seek(int offset)
    {
        lock (syncLock)
        {
            if (IsDisposed || offset < 0) return false;

            var targetFrame = offset / Channels;
            var sourceFrame = sourceSampleRate == SampleRate
                ? targetFrame
                : (long)(targetFrame * (double)sourceSampleRate / SampleRate);

            if (!SeekSource(sourceFrame)) return false;

            // Interpolation state describes the old position, so drop it.
            primed = false;
            sourceExhausted = false;
            finished = false;
            endOfStreamRaised = false;
            phase = 0;
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (syncLock)
        {
            if (IsDisposed) return;
            DisposeCore();
            IsDisposed = true;
        }
    }

    /// <summary>
    /// Fast path: no rate change, so frames pass through with only channel mapping applied.
    /// </summary>
    private int DecodeAtSourceRate(Span<float> samples, int framesWanted)
    {
        if (sourceChannels == Channels)
        {
            var read = ReadSourceSamples(samples.Slice(0, framesWanted * Channels));
            return read / Channels;
        }

        EnsureSourceBuffer(framesWanted * sourceChannels);
        var sourceSamples = ReadSourceSamples(new Span<float>(sourceBuffer, 0, framesWanted * sourceChannels));
        var frames = sourceSamples / sourceChannels;

        for (var frame = 0; frame < frames; frame++)
        {
            MapChannels(new ReadOnlySpan<float>(sourceBuffer, frame * sourceChannels, sourceChannels),
                        samples.Slice(frame * Channels, Channels));
        }

        return frames;
    }

    /// <summary>
    /// Rate-converting path: linear interpolation between consecutive source frames.
    /// </summary>
    private int DecodeResampled(Span<float> samples, int framesWanted)
    {
        // Once the held tail frame has been emitted there is nothing left to interpolate from;
        // without this the decoder would keep returning that one frame forever and any caller
        // looping until Decode returns 0 would never terminate.
        if (finished) return 0;

        var step = (double)sourceSampleRate / SampleRate;

        if (!primed)
        {
            if (!ReadSourceFrame(previousFrame)) return 0;
            if (!ReadSourceFrame(nextFrame)) Array.Copy(previousFrame, nextFrame, sourceChannels);
            primed = true;
            phase = 0;
        }

        Span<float> interpolated = sourceChannels <= 8 ? stackalloc float[sourceChannels] : new float[sourceChannels];

        var framesWritten = 0;
        while (framesWritten < framesWanted)
        {
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                interpolated[channel] =
                    (float)(previousFrame[channel] + (nextFrame[channel] - previousFrame[channel]) * phase);
            }

            MapChannels(interpolated, samples.Slice(framesWritten * Channels, Channels));
            framesWritten++;

            phase += step;
            while (phase >= 1.0)
            {
                phase -= 1.0;

                (previousFrame, nextFrame) = (nextFrame, previousFrame);

                if (!ReadSourceFrame(nextFrame))
                {
                    if (sourceExhausted)
                    {
                        finished = true;
                        return framesWritten;
                    }

                    // Hold the last frame so the tail is emitted rather than clipped.
                    sourceExhausted = true;
                    Array.Copy(previousFrame, nextFrame, sourceChannels);
                }
            }
        }

        return framesWritten;
    }

    private bool ReadSourceFrame(float[] destination)
    {
        var read = ReadSourceSamples(new Span<float>(destination, 0, sourceChannels));
        if (read >= sourceChannels) return true;

        // A partial frame at the very end is not usable; treat it as the end of the stream.
        Array.Clear(destination, 0, sourceChannels);
        return false;
    }

    /// <summary>
    /// Maps one source frame onto one target frame: duplicate when widening, average when
    /// narrowing to mono, and otherwise copy what lines up and leave the rest silent.
    /// </summary>
    private void MapChannels(ReadOnlySpan<float> source, Span<float> destination)
    {
        if (sourceChannels == destination.Length)
        {
            source.CopyTo(destination);
            return;
        }

        if (sourceChannels == 1)
        {
            destination.Fill(source[0]);
            return;
        }

        if (destination.Length == 1)
        {
            var sum = 0f;
            for (var i = 0; i < source.Length; i++) sum += source[i];
            destination[0] = sum / source.Length;
            return;
        }

        var shared = Math.Min(source.Length, destination.Length);
        source.Slice(0, shared).CopyTo(destination);
        if (destination.Length > shared) destination.Slice(shared).Clear();
    }

    private void EnsureSourceBuffer(int samples)
    {
        if (sourceBuffer.Length < samples) sourceBuffer = new float[samples];
    }
}

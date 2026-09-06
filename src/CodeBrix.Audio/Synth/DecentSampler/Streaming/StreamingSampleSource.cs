using System;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler.Samples;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// A sample played from disk instead of from RAM.
//
// What is held: the file's header, and a PRELOAD HEAD - the first N frames, decoded once and kept, so
// that a note starting at the beginning of the file (which is nearly every note) has audio the instant
// the key goes down, with no reader thread involved. Everything past the head is fetched by the
// streaming reader into the voice's own ring buffer.
//
// What is NOT held: the audio. That is the whole point. A 96 kHz stereo sample costs 768 kB per second
// decoded; the nine-library corpus this engine was measured against decodes to 4.7 GB.
//
// Threading: the preload head is immutable and lock-free to read. Everything that touches the decoder
// takes _gate, because the streaming reader, a lazily-decoding worker and a test can all ask for frames
// at once. The audio thread NEVER calls in here - it reads the voice's ring buffer, which the reader
// filled.
internal sealed class StreamingSampleSource : ISampleSource
{
    // How many frames the decoder is asked for at a time.
    private const int ReadChunkFrames = 4096;

    private readonly string _fileName;
    private readonly object _gate = new object();

    private readonly int _sourceChannels;
    private readonly int _blockAlign;
    private readonly float[][] _preload;
    private readonly long _preloadFrames;

    private WaveStream _reader;
    private Stream _stream;
    private ISampleProvider _provider;
    private long _readerFrame;
    private float[] _scratch;
    private bool _disposed;

    private StreamingSampleSource(
        string fileName,
        WaveStream reader,
        Stream stream,
        int sourceChannels,
        int channelCount,
        int sampleRate,
        long frames,
        long? loopStart,
        long? loopEnd,
        float[][] preload,
        long preloadFrames)
    {
        _fileName = fileName;
        _reader = reader;
        _stream = stream;
        _provider = reader.ToSampleProvider();
        _sourceChannels = sourceChannels;
        _blockAlign = reader.WaveFormat.BlockAlign;
        _preload = preload;
        _preloadFrames = preloadFrames;
        _readerFrame = -1;

        ChannelCount = channelCount;
        SampleRate = sampleRate;
        Frames = frames;
        EmbeddedLoopStart = loopStart;
        EmbeddedLoopEnd = loopEnd;
    }

    /// <inheritdoc/>
    public int ChannelCount { get; }

    /// <inheritdoc/>
    public int SampleRate { get; }

    /// <inheritdoc/>
    public long Frames { get; }

    /// <inheritdoc/>
    public long? EmbeddedLoopStart { get; }

    /// <inheritdoc/>
    public long? EmbeddedLoopEnd { get; }

    /// <inheritdoc/>
    public bool HasEmbeddedLoop => EmbeddedLoopStart.HasValue && EmbeddedLoopEnd.HasValue;

    /// <inheritdoc/>
    public bool IsInMemory => false;

    /// <summary>The RAM this source costs: the preload head only, never the whole file.</summary>
    public long DecodedByteCount => _preloadFrames * ChannelCount * sizeof(float);

    /// <summary>How many frames of the head are held in RAM.</summary>
    public long PreloadFrameCount => _preloadFrames;

    /// <summary>What the whole file would have cost decoded, which is what the policy measured.</summary>
    public long FullyDecodedByteCount => Frames * ChannelCount * sizeof(float);

    /// <summary>The file this source reads, for problem messages.</summary>
    public string FileName => _fileName;

    /// <summary>
    /// Opens a file for streaming and decodes its preload head.
    /// </summary>
    /// <param name="stream">The audio bytes. Owned by the source from here on.</param>
    /// <param name="fileName">The file name, for the reader choice and for problem messages.</param>
    /// <param name="preloadFrames">How many frames to hold in RAM, at least one.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file does not say how long it is.</exception>
    public static StreamingSampleSource Open(Stream stream, string fileName, int preloadFrames)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var reader = SampleReaders.Open(stream, fileName);

        try
        {
            var format = reader.WaveFormat;
            var sourceChannels = Math.Max(1, format.Channels);
            var channelCount = SampleReaders.TargetChannelCount(sourceChannels);
            var blockAlign = format.BlockAlign;

            if (blockAlign <= 0 || reader.Length <= 0)
            {
                // A stream that cannot say how long it is cannot be positioned, so it cannot be
                // streamed. The loader falls back to an in-memory decode and says so.
                throw new InvalidDataException(
                    "the file does not declare its length, so it cannot be streamed");
            }

            var declaredFrames = reader.Length / blockAlign;

            SfzSampleData.ReadEmbeddedLoop(reader, out var loopStart, out var loopEnd);

            var headFrames = (int)Math.Min(Math.Max(1, preloadFrames), declaredFrames);
            var preload = new float[channelCount][];
            for (var c = 0; c < channelCount; c++)
            {
                preload[c] = new float[headFrames];
            }

            reader.Position = 0;
            var provider = reader.ToSampleProvider();
            var scratch = new float[ReadChunkFrames * sourceChannels];
            var filled = Fill(provider, scratch, sourceChannels, channelCount, preload, 0, headFrames);

            // A file shorter than its header claims: shrink the head rather than play zeros, and take
            // the shorter count as the truth.
            var frames = filled < headFrames ? filled : declaredFrames;

            if (filled < headFrames)
            {
                for (var c = 0; c < channelCount; c++)
                {
                    Array.Resize(ref preload[c], Math.Max(1, filled));
                }
            }

            if (loopEnd.HasValue && loopEnd.Value >= frames)
            {
                loopEnd = frames - 1;
            }

            if (loopStart.HasValue &&
                (loopStart.Value < 0 || (loopEnd.HasValue && loopStart.Value > loopEnd.Value)))
            {
                loopStart = null;
                loopEnd = null;
            }

            return new StreamingSampleSource(
                fileName, reader, stream, sourceChannels, channelCount, format.SampleRate,
                frames, loopStart, loopEnd, preload, Math.Max(0, filled));
        }
        catch (Exception)
        {
            reader.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public int Read(int channel, long startFrame, Span<float> destination)
    {
        if (channel < 0 || channel >= ChannelCount || startFrame < 0 || startFrame >= Frames)
        {
            return 0;
        }

        var wanted = (int)Math.Min(destination.Length, Frames - startFrame);
        if (wanted <= 0)
        {
            return 0;
        }

        // Allocating here is deliberate and safe: this overload exists for the ISampleSource contract
        // and for tests, never for the audio thread, which reads a ring buffer instead.
        var planar = new float[ChannelCount][];
        for (var c = 0; c < ChannelCount; c++)
        {
            planar[c] = new float[wanted];
        }

        var read = ReadFrames(startFrame, planar, 0, wanted);
        planar[channel].AsSpan(0, read).CopyTo(destination);
        return read;
    }

    /// <summary>
    /// Fills planar channel buffers from the PRELOAD HEAD only, never touching the decoder and never
    /// taking a lock, so a note-on may call it.
    /// </summary>
    /// <param name="startFrame">The first frame to read.</param>
    /// <param name="destination">One array per channel.</param>
    /// <param name="destinationOffset">Where in each channel array the frames go.</param>
    /// <param name="count">How many frames are wanted.</param>
    /// <returns>How many frames the head could serve, which is zero past its end.</returns>
    public int ReadFromHead(long startFrame, float[][] destination, int destinationOffset, int count)
    {
        if (destination == null || count <= 0 || startFrame < 0 || startFrame >= _preloadFrames)
        {
            return 0;
        }

        var available = (int)Math.Min(count, _preloadFrames - startFrame);

        for (var c = 0; c < ChannelCount && c < destination.Length; c++)
        {
            Array.Copy(_preload[c], (int)startFrame, destination[c], destinationOffset, available);
        }

        return available;
    }

    /// <summary>
    /// Fills planar channel buffers from the file, taking whatever the preload head can serve without
    /// touching the decoder.
    /// </summary>
    /// <param name="startFrame">The first frame to read.</param>
    /// <param name="destination">One array per channel.</param>
    /// <param name="destinationOffset">Where in each channel array the frames go.</param>
    /// <param name="count">How many frames are wanted.</param>
    /// <returns>How many frames were written, which is fewer at the end of the file.</returns>
    public int ReadFrames(long startFrame, float[][] destination, int destinationOffset, int count)
    {
        if (destination == null || count <= 0 || startFrame < 0 || startFrame >= Frames)
        {
            return 0;
        }

        var total = 0;
        var frame = startFrame;
        var offset = destinationOffset;
        var remaining = (int)Math.Min(count, Frames - startFrame);

        // The head first: RAM, no lock, no decoder.
        if (frame < _preloadFrames)
        {
            var fromHead = (int)Math.Min(remaining, _preloadFrames - frame);
            for (var c = 0; c < ChannelCount && c < destination.Length; c++)
            {
                Array.Copy(_preload[c], (int)frame, destination[c], offset, fromHead);
            }

            frame += fromHead;
            offset += fromHead;
            remaining -= fromHead;
            total += fromHead;
        }

        if (remaining <= 0)
        {
            return total;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return total;
            }

            if (_readerFrame != frame)
            {
                SeekTo(frame);
            }

            _scratch ??= new float[ReadChunkFrames * _sourceChannels];

            var read = Fill(_provider, _scratch, _sourceChannels, ChannelCount, destination, offset, remaining);
            _readerFrame = frame + read;
            total += read;
        }

        return total;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reader?.Dispose();

            // THE READER DOES NOT OWN THE STREAM. Every WaveStream this engine builds over a Stream -
            // WaveFileReader, AiffFileReader, FlacFileReader and any registered decoder - is
            // constructed with ownInput = false, so disposing it leaves the underlying FileStream, and
            // therefore the operating-system file handle, open until a finalizer happens to run. A
            // streamed instrument holds one of those per streamed FILE for its whole life, which is
            // the design; letting them survive its disposal is not.
            _stream?.Dispose();

            _reader = null;
            _stream = null;
            _provider = null;
            _scratch = null;
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"streaming {_fileName}: {Frames} frames, {ChannelCount} channels, head {_preloadFrames}";

    private void SeekTo(long frame)
    {
        _reader.Position = frame * _blockAlign;

        // A decoder may land short of the request when it can only position on a frame boundary of its
        // own; skip the difference so the caller always gets the frames it asked for.
        var landed = _reader.Position / _blockAlign;

        if (landed < frame)
        {
            _scratch ??= new float[ReadChunkFrames * _sourceChannels];

            var skip = frame - landed;
            while (skip > 0)
            {
                var chunk = (int)Math.Min(skip, ReadChunkFrames);
                var read = _provider.Read(_scratch.AsSpan(0, chunk * _sourceChannels));
                if (read <= 0)
                {
                    break;
                }

                skip -= read / _sourceChannels;
            }
        }

        _readerFrame = frame;
    }

    // Reads interleaved frames out of a sample provider and de-interleaves them into planar channels,
    // folding anything past stereo away exactly as SfzSampleData does, so a streamed sample and a
    // decoded one hold the same numbers.
    private static int Fill(
        ISampleProvider provider, float[] scratch, int sourceChannels, int channelCount,
        float[][] destination, int destinationOffset, int count)
    {
        var capacity = scratch.Length / sourceChannels;
        var written = 0;

        while (written < count)
        {
            var wanted = Math.Min(count - written, capacity);
            var read = provider.Read(scratch.AsSpan(0, wanted * sourceChannels));

            if (read <= 0)
            {
                break;
            }

            var frames = read / sourceChannels;
            if (frames == 0)
            {
                break;
            }

            for (var c = 0; c < channelCount; c++)
            {
                var channel = destination[c];
                var target = destinationOffset + written;

                for (var f = 0; f < frames; f++)
                {
                    channel[target + f] = scratch[f * sourceChannels + c];
                }
            }

            written += frames;
        }

        return written;
    }
}

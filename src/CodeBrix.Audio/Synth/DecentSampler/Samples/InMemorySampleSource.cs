using System;
using System.IO;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Synth.DecentSampler.Samples;

/// <summary>
/// A sample decoded whole into memory, as planar 32-bit float channels.
/// </summary>
/// <remarks>
/// <para>
/// The decode itself is the SFZ engine's <see cref="SfzSampleData"/>, unchanged: one decoder serves
/// both sampled formats, including the WAV <c>smpl</c> chunk that carries a file's own loop points.
/// This type adds the container-aware opening the Decent Sampler format needs - WAV, AIFF and FLAC,
/// from a folder or from inside an archive.
/// </para>
/// <para>
/// Memory follows the audio: a stereo 96 kHz sample costs eight bytes per frame per second per pair.
/// <see cref="DecodedByteCount"/> is what the load options' thresholds are measured against.
/// </para>
/// </remarks>
internal sealed class InMemorySampleSource : ISampleSource
{
    private readonly SfzSampleData _data;

    private InMemorySampleSource(SfzSampleData data)
    {
        _data = data;
    }

    /// <inheritdoc/>
    public int ChannelCount => _data.ChannelCount;

    /// <inheritdoc/>
    public int SampleRate => _data.SampleRate;

    /// <inheritdoc/>
    public long Frames => _data.Frames;

    /// <inheritdoc/>
    public long? EmbeddedLoopStart => _data.EmbeddedLoopStart;

    /// <inheritdoc/>
    public long? EmbeddedLoopEnd => _data.EmbeddedLoopEnd;

    /// <inheritdoc/>
    public bool HasEmbeddedLoop => _data.HasEmbeddedLoop;

    /// <inheritdoc/>
    public bool IsInMemory => true;

    /// <inheritdoc/>
    public long DecodedByteCount => _data.Frames * _data.ChannelCount * sizeof(float);

    /// <summary>The decoded audio, for the voice engine's oscillators.</summary>
    public SfzSampleData Data => _data;

    /// <summary>
    /// Decodes an audio stream, choosing the reader from the file name's extension.
    /// </summary>
    /// <param name="stream">The audio bytes. Read fully, then disposed by this method.</param>
    /// <param name="fileName">
    /// The file name the extension is taken from. Only the extension is used, so an archive entry name
    /// works as well as a path.
    /// </param>
    /// <returns>The decoded sample.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="NotSupportedException">No reader handles the extension.</exception>
    public static InMemorySampleSource Decode(Stream stream, string fileName)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using (stream)
        using (var reader = SampleReaders.Open(stream, fileName))
        {
            return new InMemorySampleSource(SfzSampleData.Load(reader));
        }
    }

    /// <inheritdoc/>
    public int Read(int channel, long startFrame, Span<float> destination)
    {
        if (channel < 0 || channel >= _data.ChannelCount || startFrame < 0 || startFrame >= _data.Frames)
        {
            return 0;
        }

        var available = (int)Math.Min(destination.Length, _data.Frames - startFrame);
        if (available <= 0)
        {
            return 0;
        }

        _data.Channels[channel].AsSpan((int)startFrame, available).CopyTo(destination);
        return available;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The decoded audio is plain managed arrays; there is nothing to release.
    }
}

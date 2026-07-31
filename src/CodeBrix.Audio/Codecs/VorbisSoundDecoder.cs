using System;
using System.IO;
using CodeBrix.Audio.Vorbis;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Decodes Ogg Vorbis for the audio engine using the fully managed decoder in
/// <c>CodeBrix.Audio.Vorbis</c>.
/// </summary>
/// <remarks>
/// This is the fallback path. Where the bundled native library was built with Ogg Vorbis support
/// - which is the case for every runtime identifier built from this repository's own sources -
/// the native decoder is used instead and does the format conversion in C. This class exists so
/// that .ogg still plays when the native library predates Vorbis support or is missing for the
/// current architecture. Channel and rate conversion come from <see cref="ManagedSoundDecoder"/>.
/// </remarks>
internal sealed class VorbisSoundDecoder : ManagedSoundDecoder
{
    private readonly VorbisReader reader;

    /// <summary>
    /// Creates a decoder over an Ogg Vorbis stream.
    /// </summary>
    /// <param name="stream">The stream to read from; it is not disposed by this class.</param>
    /// <param name="channels">
    /// The channel count the engine wants back, or 0 to adopt the file's own channel count.
    /// </param>
    /// <param name="sampleRate">
    /// The sample rate the engine wants back, or 0 to adopt the file's own sample rate.
    /// </param>
    public VorbisSoundDecoder(Stream stream, int channels, int sampleRate)
        : base(channels, sampleRate)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        reader = new VorbisReader(stream, false);
        Initialize(reader.Channels, reader.SampleRate, reader.TotalSamples);
    }

    /// <inheritdoc />
    protected override int ReadSourceSamples(Span<float> destination) => reader.ReadSamples(destination);

    /// <inheritdoc />
    protected override bool SeekSource(long frameIndex)
    {
        try
        {
            reader.SeekTo(Math.Min(frameIndex, reader.TotalSamples));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore() => reader.Dispose();
}

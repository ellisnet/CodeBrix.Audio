using System;
using System.IO;
using CodeBrix.Audio.Flac;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Decodes FLAC for the audio engine using the fully managed decoder in
/// <c>CodeBrix.Audio.Flac</c>.
/// </summary>
/// <remarks>
/// The bundled native library has always been able to decode FLAC, so this is a fallback for the
/// cases the native path cannot serve - an architecture with no native binary, or a host where it
/// fails to load. Channel and rate conversion come from <see cref="ManagedSoundDecoder"/>.
/// </remarks>
internal sealed class FlacSoundDecoder : ManagedSoundDecoder
{
    private readonly FlacDecoder decoder;
    private readonly float scale;

    private int[] sampleBuffer = [];

    /// <summary>
    /// Creates a decoder over a FLAC stream.
    /// </summary>
    /// <param name="stream">The stream to read from; it is not disposed by this class.</param>
    /// <param name="channels">
    /// The channel count the engine wants back, or 0 to adopt the file's own channel count.
    /// </param>
    /// <param name="sampleRate">
    /// The sample rate the engine wants back, or 0 to adopt the file's own sample rate.
    /// </param>
    public FlacSoundDecoder(Stream stream, int channels, int sampleRate)
        : base(channels, sampleRate)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        decoder = new FlacDecoder(stream);
        var info = decoder.StreamInfo;

        // FLAC samples are signed integers at the stream's own bit depth; the engine mixes in
        // float, so they are scaled by the depth's full-scale value.
        scale = 1f / (1L << (info.BitsPerSample - 1));

        Initialize(info.Channels, info.SampleRate, info.TotalSamples);
    }

    /// <inheritdoc />
    protected override int ReadSourceSamples(Span<float> destination)
    {
        if (sampleBuffer.Length < destination.Length) sampleBuffer = new int[destination.Length];

        var read = decoder.ReadSamples(new Span<int>(sampleBuffer, 0, destination.Length));
        for (var i = 0; i < read; i++) destination[i] = sampleBuffer[i] * scale;

        return read;
    }

    /// <inheritdoc />
    protected override bool SeekSource(long frameIndex)
    {
        try
        {
            var total = decoder.StreamInfo.TotalSamples;
            decoder.SeekTo(total > 0 ? Math.Min(frameIndex, total) : frameIndex);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore() => decoder.Dispose();
}

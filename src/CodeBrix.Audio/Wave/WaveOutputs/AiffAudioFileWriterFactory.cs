using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Wave.Internal;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Writes <c>.aif</c> and <c>.aiff</c> files through <see cref="AudioFileWriterRegistry"/>.
/// Registered out of the box, under both names.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFAULT IS 16-BIT PCM, big-endian, which is what an AIFF file ordinarily holds and what
/// <see cref="AiffFileReader"/> reads back. 24-bit PCM is available by passing an explicit format.
/// Float samples are deliberately NOT offered: storing them would need the AIFF-C container, and
/// this writer writes plain AIFF, where a reader would take the bytes for PCM and produce noise.
/// </para>
/// <para>
/// An AIFF file records its length in the FORM, COMM and SSND headers, none of which is correct
/// until the audio has been written, so this format needs a stream it can seek back through.
/// </para>
/// </remarks>
public sealed class AiffAudioFileWriterFactory : IAudioFileWriterFactory
{
    private static readonly string[] AiffExtensions = [".aif", ".aiff"];

    private readonly int defaultBitsPerSample;

    /// <summary>Creates the factory with its 16-bit PCM default.</summary>
    public AiffAudioFileWriterFactory() : this(16)
    {
    }

    /// <summary>Creates the factory with a chosen default bit depth.</summary>
    /// <param name="defaultBitsPerSample">16 or 24, both PCM.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bit depth is not 16 or 24.</exception>
    public AiffAudioFileWriterFactory(int defaultBitsPerSample)
    {
        if (defaultBitsPerSample != 16 && defaultBitsPerSample != 24)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultBitsPerSample),
                defaultBitsPerSample,
                "An AIFF file is written as 16 or 24 bit PCM.");
        }

        this.defaultBitsPerSample = defaultBitsPerSample;
    }

    /// <summary>The bit depth <see cref="DefaultFormat"/> asks for. 16 by default.</summary>
    public int DefaultBitsPerSample => defaultBitsPerSample;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions => AiffExtensions;

    /// <inheritdoc />
    public bool RequiresSeekableStream => true;

    /// <inheritdoc />
    public WaveFormat DefaultFormat(int sampleRate, int channels)
    {
        AudioFileWriterGuards.CheckFormatArguments(sampleRate, channels);

        return new WaveFormat(sampleRate, defaultBitsPerSample, channels);
    }

    /// <inheritdoc />
    public IAudioFileWriter Create(Stream stream, WaveFormat format)
    {
        AudioFileWriterGuards.CheckStream(stream, format, ".aiff", requiresSeek: true);

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            throw new ArgumentException(
                "An AIFF file cannot hold IEEE float samples: that needs the AIFF-C container, and " +
                "this writer writes plain AIFF. Ask for 16 or 24 bit PCM instead.",
                nameof(format));
        }

        return new AiffStreamAudioFileWriter(stream, format);
    }
}

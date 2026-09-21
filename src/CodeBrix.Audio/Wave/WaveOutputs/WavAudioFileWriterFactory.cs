using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Wave.Internal;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Writes <c>.wav</c> files through <see cref="AudioFileWriterRegistry"/>. Registered out of the
/// box, so nothing has to be done to render to a WAV file.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFAULT IS 32-BIT FLOAT, which is what an offline render produces and what
/// <see cref="CodeBrix.Audio.Synth.SoundFontRenderer"/> has always written: nothing is quantised
/// and nothing clips on the way to disk. 16-bit PCM - half the size, and what every other program
/// expects of a <c>.wav</c> - is a format away:
/// </para>
/// <code>
/// var format = new WaveFormat(44100, 16, 2);                       // 16-bit PCM
/// SoundFontRenderer.RenderToFile(synthesizer, sequence, "tune.wav", format);
/// </code>
/// <para>
/// An application that wants 16-bit to be ITS default registers a factory that says so, which
/// replaces the built-in one:
/// </para>
/// <code>
/// AudioFileWriterRegistry.Register(new WavAudioFileWriterFactory(defaultBitsPerSample: 16));
/// </code>
/// <para>
/// A WAV file records its own length in two chunk headers that are only correct once the audio has
/// been written, so this format needs a stream it can seek back through.
/// </para>
/// </remarks>
public sealed class WavAudioFileWriterFactory : IAudioFileWriterFactory
{
    private static readonly string[] WavExtensions = [".wav"];

    private readonly int defaultBitsPerSample;

    /// <summary>Creates the factory with its 32-bit float default.</summary>
    public WavAudioFileWriterFactory() : this(32)
    {
    }

    /// <summary>Creates the factory with a chosen default bit depth.</summary>
    /// <param name="defaultBitsPerSample">
    /// 16 or 24 for PCM, 32 for IEEE float. Only affects <see cref="DefaultFormat"/>; a caller who
    /// passes an explicit format to <see cref="Create"/> gets that format.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The bit depth is not 16, 24 or 32.</exception>
    public WavAudioFileWriterFactory(int defaultBitsPerSample)
    {
        if (defaultBitsPerSample != 16 && defaultBitsPerSample != 24 && defaultBitsPerSample != 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultBitsPerSample),
                defaultBitsPerSample,
                "A WAV file is written as 16 or 24 bit PCM, or as 32 bit IEEE float.");
        }

        this.defaultBitsPerSample = defaultBitsPerSample;
    }

    /// <summary>The bit depth <see cref="DefaultFormat"/> asks for. 32, meaning float, by default.</summary>
    public int DefaultBitsPerSample => defaultBitsPerSample;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions => WavExtensions;

    /// <inheritdoc />
    public bool RequiresSeekableStream => true;

    /// <inheritdoc />
    public WaveFormat DefaultFormat(int sampleRate, int channels)
    {
        AudioFileWriterGuards.CheckFormatArguments(sampleRate, channels);

        return defaultBitsPerSample == 32
            ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
            : new WaveFormat(sampleRate, defaultBitsPerSample, channels);
    }

    /// <inheritdoc />
    public IAudioFileWriter Create(Stream stream, WaveFormat format)
    {
        AudioFileWriterGuards.CheckStream(stream, format, ".wav", requiresSeek: true);

        return new WavStreamAudioFileWriter(stream, format);
    }
}

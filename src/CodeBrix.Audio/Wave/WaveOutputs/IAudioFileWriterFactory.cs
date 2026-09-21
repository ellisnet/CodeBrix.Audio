using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Makes an <see cref="IAudioFileWriter"/> for one audio format, and says what that format needs -
/// the write-side mirror of the reader factories <see cref="AudioFileReaderRegistry"/> holds.
/// </summary>
/// <remarks>
/// <para>
/// CodeBrix.Audio is MIT and gains no dependencies, so an encoder under another licence - or one
/// that carries native code - lives in its own package and arrives here:
/// </para>
/// <code>
/// AudioFileWriterRegistry.Register(new OpusAudioFileWriterFactory());
/// </code>
/// <para>
/// Everything that writes audio by file name then covers that format too, including
/// <see cref="CodeBrix.Audio.Synth.SoundFontRenderer"/>'s <c>RenderToFile</c>. Format support is a
/// registration question, not a dependency question.
/// </para>
/// </remarks>
public interface IAudioFileWriterFactory
{
    /// <summary>
    /// The file extensions this factory writes, each with its leading dot and in lower case -
    /// <c>.wav</c>, or <c>.aif</c> and <c>.aiff</c> for a format known by two names.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Whether the format can only be written to a stream that can seek.
    /// </summary>
    /// <remarks>
    /// True for a format whose header records a length that is not known until the audio has been
    /// written, because finishing the file means going back and patching it - WAV and AIFF both do.
    /// False for a format written strictly forwards, such as Ogg, which can therefore be written to
    /// a network stream or a pipe.
    /// </remarks>
    bool RequiresSeekableStream { get; }

    /// <summary>
    /// The format this factory writes when the caller says only how many channels at what rate -
    /// for WAV, 32-bit float; for AIFF, 16-bit PCM.
    /// </summary>
    /// <param name="sampleRate">The sample rate, in Hz.</param>
    /// <param name="channels">The number of channels.</param>
    /// <returns>A format this factory can write.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> or <paramref name="channels"/> is not positive.
    /// </exception>
    WaveFormat DefaultFormat(int sampleRate, int channels);

    /// <summary>Creates a writer over a stream.</summary>
    /// <param name="stream">
    /// The stream the file is written to. The writer does not close it, and it must be seekable
    /// when <see cref="RequiresSeekableStream"/> is true.
    /// </param>
    /// <param name="format">
    /// The sample rate, channel count and stored sample format to write. Pass
    /// <see cref="DefaultFormat"/> to accept this factory's own choice.
    /// </param>
    /// <returns>A writer that is ready for its first samples.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The stream cannot be written to, the stream cannot seek and this format needs it to, or the
    /// format is one this factory does not write.
    /// </exception>
    IAudioFileWriter Create(Stream stream, WaveFormat format);
}

using System;
using System.IO;

namespace CodeBrix.Audio.Wave.Internal;

/// <summary>
/// The argument checking the built-in <see cref="IAudioFileWriter"/> adapters and their factories
/// share, kept in one place so every format refuses the same things with the same words.
/// </summary>
internal static class AudioFileWriterGuards
{
    /// <summary>Checks the buffer, offset and count of a write.</summary>
    internal static void CheckBuffer(float[] samples, int offset, int count)
    {
        if (samples == null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        if (offset < 0 || offset > samples.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), offset, "The offset must fall inside the buffer.");
        }

        if (count < 0 || offset + count > samples.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "The count must not run past the end of the buffer.");
        }
    }

    /// <summary>Checks the stream a writer is about to be built over.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="extension">The format's extension, for the message.</param>
    /// <param name="requiresSeek">Whether the format has to patch its header at the end.</param>
    internal static void CheckStream(Stream stream, WaveFormat format, string extension, bool requiresSeek)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (format == null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        if (!stream.CanWrite)
        {
            throw new ArgumentException(
                $"A {extension} file cannot be written to a stream that is not writable.", nameof(stream));
        }

        if (requiresSeek && !stream.CanSeek)
        {
            throw new ArgumentException(
                $"A {extension} file needs a stream that can seek: the length is written into the " +
                "header once the audio is known, which means going back to the start of the file. " +
                "Write to a FileStream or a MemoryStream, or choose a format that is written " +
                "strictly forwards.",
                nameof(stream));
        }
    }

    /// <summary>Checks a sample rate and channel count handed to a factory.</summary>
    internal static void CheckFormatArguments(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels), channels, "The channel count must be positive.");
        }
    }
}

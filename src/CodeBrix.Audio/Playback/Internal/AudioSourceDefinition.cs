using System;
using System.IO;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Where a track's recording lives, and how to open a fresh, independently positioned decoder over
/// it.
/// </summary>
/// <remarks>
/// Several readers of one track run at once - the live mix, an offline render, a loudness
/// measurement - and each needs its own position. A file path supports that directly (open the file
/// again); a stream does not, so a stream's bytes are copied once into memory and every decoder
/// reads its own view of that copy.
/// </remarks>
internal sealed class AudioSourceDefinition
{
    private readonly string filePath;
    private readonly byte[] bytes;

    private AudioSourceDefinition(string filePath, byte[] bytes, TimeSpan duration, int sampleRate, int channels)
    {
        this.filePath = filePath;
        this.bytes = bytes;
        Duration = duration;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>The length of the recording.</summary>
    internal TimeSpan Duration { get; }

    /// <summary>The recording's own sample rate, before any conversion.</summary>
    internal int SampleRate { get; }

    /// <summary>The recording's own channel count, before any conversion.</summary>
    internal int Channels { get; }

    /// <summary>Describes a recording on disk, probing it once for its length and format.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The description.</returns>
    internal static AudioSourceDefinition FromFile(string path)
    {
        using (var reader = AudioSourceReaders.Open(path))
        {
            return new AudioSourceDefinition(path, null, reader.TotalTime,
                reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        }
    }

    /// <summary>Describes a recording held in a stream, copying its bytes into memory.</summary>
    /// <param name="stream">The stream to read; it is read in full from its start.</param>
    /// <param name="leaveOpen">When false, the stream is disposed once its bytes have been copied.</param>
    /// <returns>The description.</returns>
    internal static AudioSourceDefinition FromStream(Stream stream, bool leaveOpen)
    {
        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "A multi-track audio source must be seekable, so that the transport can seek and loop.",
                nameof(stream));
        }

        byte[] copy;
        try
        {
            stream.Position = 0;
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                copy = buffer.ToArray();
            }
        }
        finally
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }
        }

        using (var reader = AudioSourceReaders.Open(new MemoryStream(copy, writable: false), leaveOpen: false))
        {
            return new AudioSourceDefinition(null, copy, reader.TotalTime,
                reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        }
    }

    /// <summary>
    /// Opens a decoder over the recording, converting to stereo at the requested rate.
    /// </summary>
    /// <param name="sampleRate">The rate to produce.</param>
    /// <returns>A decoder the caller owns and must dispose.</returns>
    internal WaveStreamSoundDecoder OpenDecoder(int sampleRate)
    {
        WaveStream reader = filePath != null
            ? AudioSourceReaders.Open(filePath)
            : AudioSourceReaders.Open(new MemoryStream(bytes, writable: false), leaveOpen: false);

        try
        {
            return new WaveStreamSoundDecoder(reader, ownsReader: true, channels: 2, sampleRate: sampleRate);
        }
        catch (Exception)
        {
            reader.Dispose();
            throw;
        }
    }
}

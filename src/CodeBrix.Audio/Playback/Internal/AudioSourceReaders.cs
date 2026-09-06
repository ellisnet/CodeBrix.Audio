using System;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Opens an audio file or stream as a <see cref="WaveStream"/>, choosing the reader from the
/// CONTENT rather than from a file extension.
/// </summary>
/// <remarks>
/// <see cref="AudioFileReader"/> already does this for paths, but only for paths and only by
/// extension, and a multi-track source is just as likely to be a stream lifted out of a zip. The
/// four built-in formats all announce themselves in their first few bytes, so sniffing is both
/// easy and more honest than trusting a name.
/// </remarks>
internal static class AudioSourceReaders
{
    /// <summary>Opens an audio file.</summary>
    /// <param name="filePath">Path to a WAV, MP3, Ogg Vorbis or FLAC file, or to a format added
    /// with <see cref="AudioFileReaderRegistry.Register"/>.</param>
    /// <returns>A reader positioned at the start; the caller owns and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is null.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    internal static WaveStream Open(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        // A format some add-on package registered is opened through the registry, which owns the
        // file handle it makes and hands back a stream whose Dispose closes both.
        if (!IsBuiltInExtension(filePath) && AudioFileReaderRegistry.Supports(filePath))
        {
            return AudioFileReaderRegistry.OpenFile(filePath);
        }

        var stream = File.OpenRead(filePath);
        try
        {
            return Open(stream, leaveOpen: false);
        }
        catch (Exception)
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Opens audio held in a seekable stream.</summary>
    /// <param name="stream">The stream to read. Must be readable and seekable.</param>
    /// <param name="leaveOpen">When true, disposing the reader leaves the stream open.</param>
    /// <returns>A reader positioned at the start.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot seek.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    internal static WaveStream Open(Stream stream, bool leaveOpen)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "A multi-track audio source must be seekable, so that the transport can seek and loop.",
                nameof(stream));
        }

        var source = leaveOpen ? new NonClosingStream(stream) : stream;
        source.Position = 0;

        Span<byte> header = stackalloc byte[12];
        var read = source.Read(header);
        source.Position = 0;

        if (read >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'A' && header[10] == (byte)'V' && header[11] == (byte)'E')
        {
            return new WaveFileReader(source);
        }

        if (read >= 4 &&
            header[0] == (byte)'O' && header[1] == (byte)'g' && header[2] == (byte)'g' && header[3] == (byte)'S')
        {
            return new OggVorbisFileReader(source);
        }

        if (read >= 4 &&
            header[0] == (byte)'f' && header[1] == (byte)'L' && header[2] == (byte)'a' && header[3] == (byte)'C')
        {
            return new FlacFileReader(source);
        }

        // MP3: either an ID3v2 tag in front of the audio, or a bare frame sync.
        if (read >= 3 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
        {
            return new Mp3FileReader(source);
        }

        if (read >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return new Mp3FileReader(source);
        }

        throw new InvalidDataException(
            "The audio source is not a WAV, MP3, Ogg Vorbis or FLAC stream. Formats are identified " +
            "from their content, so a mislabelled file is not the cause.");
    }

    private static bool IsBuiltInExtension(string filePath) =>
        filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);

    // The readers all dispose the stream they were handed. This keeps a caller's "leave it open"
    // promise without changing that for everyone else.
    private sealed class NonClosingStream : Stream
    {
        private readonly Stream inner;

        internal NonClosingStream(Stream inner) => this.inner = inner;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Deliberately does not touch the inner stream.
            base.Dispose(disposing);
        }
    }
}

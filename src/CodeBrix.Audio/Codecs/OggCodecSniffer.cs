using System;
using System.IO;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// The codec carried inside an Ogg container.
/// </summary>
/// <remarks>
/// Ogg is a container, not a codec, so "this is an Ogg file" does not say what is inside it. The
/// audio engine's metadata layer reports the format identifier "ogg" for all of them, which means
/// every Ogg-capable codec is offered every Ogg stream and has to recognise its own.
/// </remarks>
public enum OggCodec
{
    /// <summary>The stream is not an Ogg container at all.</summary>
    NotOgg,

    /// <summary>An Ogg stream whose codec could not be identified.</summary>
    Unknown,

    /// <summary>Ogg Vorbis.</summary>
    Vorbis,

    /// <summary>Ogg Opus.</summary>
    Opus,

    /// <summary>Ogg FLAC (FLAC audio inside an Ogg container, as opposed to native FLAC).</summary>
    Flac
}

/// <summary>
/// Identifies which codec an Ogg stream carries, by reading its first packet.
/// </summary>
/// <remarks>
/// Useful to any codec that plugs into CodeBrix.Audio: a factory offered an Ogg stream should
/// check this and decline anything that is not its own format, so the remaining factories still
/// get their turn.
/// </remarks>
public static class OggCodecSniffer
{
    /// <summary>
    /// Reads enough of the stream to identify the codec, and leaves the position where it found it.
    /// </summary>
    /// <param name="stream">A readable, seekable stream positioned at the start of the file.</param>
    /// <returns>The codec inside the container, or <see cref="OggCodec.NotOgg"/>.</returns>
    public static OggCodec Identify(Stream stream)
    {
        if (stream == null || !stream.CanRead || !stream.CanSeek) return OggCodec.NotOgg;

        var position = stream.Position;
        try
        {
            // Ogg page header: "OggS", version, flags, granule (8), serial (4), sequence (4),
            // CRC (4), then the segment count and the segment table. The first packet follows.
            Span<byte> header = stackalloc byte[27];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return OggCodec.NotOgg;
            }

            if (header[0] != (byte)'O' || header[1] != (byte)'g' ||
                header[2] != (byte)'g' || header[3] != (byte)'S')
            {
                return OggCodec.NotOgg;
            }

            int segmentCount = header[26];
            if (segmentCount > 0 && stream.Seek(segmentCount, SeekOrigin.Current) < 0)
            {
                return OggCodec.Unknown;
            }

            Span<byte> packet = stackalloc byte[8];
            var read = stream.ReadAtLeast(packet, packet.Length, throwOnEndOfStream: false);
            if (read < packet.Length) return OggCodec.Unknown;

            // Vorbis: packet type 0x01 followed by "vorbis".
            if (packet[0] == 0x01 && Matches(packet.Slice(1, 6), "vorbis")) return OggCodec.Vorbis;

            // Opus: "OpusHead".
            if (Matches(packet, "OpusHead")) return OggCodec.Opus;

            // Ogg FLAC: packet type 0x7F followed by "FLAC".
            if (packet[0] == 0x7F && Matches(packet.Slice(1, 4), "FLAC")) return OggCodec.Flac;

            return OggCodec.Unknown;
        }
        catch (IOException)
        {
            return OggCodec.Unknown;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>
    /// Builds the error for an Ogg stream whose codec nothing installed can decode.
    /// </summary>
    /// <param name="codec">The codec that was identified.</param>
    /// <param name="inner">The underlying failure, if there was one.</param>
    /// <returns>An exception naming the actual codec, or null when the codec is decodable.</returns>
    internal static NotSupportedException DescribeUndecodable(OggCodec codec, Exception inner = null)
    {
        return codec switch
        {
            OggCodec.Opus => new NotSupportedException(
                "This is an Ogg Opus stream. CodeBrix.Audio decodes Ogg Vorbis, not Opus - they " +
                "share the .ogg container and often the .opus extension, but they are different " +
                "codecs. Opus support is available as a separate add-on package; register it with " +
                "SharedAudioOutput.RegisterCodecFactory and AudioFileReaderRegistry.Register.",
                inner),
            OggCodec.Flac => new NotSupportedException(
                "This is an Ogg FLAC stream (FLAC audio inside an Ogg container). CodeBrix.Audio " +
                "decodes native FLAC (.flac) and Ogg Vorbis, but not FLAC carried in Ogg.",
                inner),
            OggCodec.Unknown => new NotSupportedException(
                "This is an Ogg stream, but not one CodeBrix.Audio can decode: its first packet " +
                "identifies neither Vorbis, Opus, nor FLAC.",
                inner),
            _ => null
        };
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, string ascii)
    {
        if (bytes.Length < ascii.Length) return false;

        for (var i = 0; i < ascii.Length; i++)
        {
            if (bytes[i] != (byte)ascii[i]) return false;
        }

        return true;
    }
}

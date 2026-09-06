using System;
using System.Buffers.Binary;
using System.IO;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// Reads a WAV file's format and length from its header, reading forward only and stopping at the
/// data chunk. A stems zip holds hundreds of megabytes of WAV; this is what lets a song report its
/// duration without extracting any of it.
/// </summary>
internal static class SunoWavProbe
{
    /// <summary>
    /// Reads the header of a WAV stream.
    /// </summary>
    /// <param name="stream">A stream positioned at the start of the file. Only read forward.</param>
    /// <param name="totalBytes">
    /// The file's total size, used when the data chunk declares a length it cannot have (a WAV
    /// written as a stream declares 0 or 0xFFFFFFFF). Pass 0 when it is not known.
    /// </param>
    /// <returns>What the header said, or an invalid instance when it could not be read.</returns>
    internal static SunoWavInfo Read(Stream stream, long totalBytes)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            if (!ReadExactly(stream, header))
            {
                return default;
            }

            if (header[0] != (byte)'R' || header[1] != (byte)'I' || header[2] != (byte)'F' || header[3] != (byte)'F' ||
                header[8] != (byte)'W' || header[9] != (byte)'A' || header[10] != (byte)'V' || header[11] != (byte)'E')
            {
                return default;
            }

            var consumed = 12L;
            var sampleRate = 0;
            var channels = 0;
            var bits = 0;

            Span<byte> chunkHeader = stackalloc byte[8];
            Span<byte> format = stackalloc byte[16];
            while (ReadExactly(stream, chunkHeader))
            {
                consumed += 8;
                var chunkId = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
                var declared = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.Slice(4));

                if (chunkId == DataChunkId)
                {
                    long dataBytes = declared;
                    if (declared == 0 || declared == uint.MaxValue || (totalBytes > 0 && consumed + declared > totalBytes))
                    {
                        dataBytes = totalBytes > consumed ? totalBytes - consumed : 0;
                    }

                    return new SunoWavInfo(sampleRate, channels, bits, dataBytes);
                }

                if (chunkId == FormatChunkId && declared >= 16)
                {
                    if (!ReadExactly(stream, format))
                    {
                        return default;
                    }

                    channels = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(2));
                    sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format.Slice(4));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(14));
                    consumed += 16;
                    if (!Skip(stream, declared - 16 + (declared % 2)))
                    {
                        return default;
                    }

                    consumed += declared - 16 + (declared % 2);
                    continue;
                }

                var toSkip = declared + (declared % 2);
                if (!Skip(stream, toSkip))
                {
                    return default;
                }

                consumed += toSkip;
            }

            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    private const uint FormatChunkId = 0x666D7420; // "fmt "
    private const uint DataChunkId = 0x64617461;   // "data"

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = stream.Read(buffer.Slice(read));
            if (got <= 0)
            {
                return false;
            }

            read += got;
        }

        return true;
    }

    private static bool Skip(Stream stream, long count)
    {
        if (count <= 0)
        {
            return true;
        }

        Span<byte> scratch = stackalloc byte[1024];
        while (count > 0)
        {
            var want = (int)Math.Min(count, scratch.Length);
            var got = stream.Read(scratch.Slice(0, want));
            if (got <= 0)
            {
                return false;
            }

            count -= got;
        }

        return true;
    }
}

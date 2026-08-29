using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>
/// Takes an Ogg file apart into the codec packets it carries, and re-frames the three Vorbis setup
/// headers the way a media container carries them.
/// </summary>
/// <remarks>
/// <para>
/// The packet-level seam is fed by a demultiplexer, and the fixtures here are Ogg files, so the
/// tests need something that turns one into the other. This is deliberately a TEST-SIDE reader
/// written from the Ogg framing rules (RFC 3533): capture pattern, segment table, and a packet that
/// ends at the first segment shorter than 255. Using the library's own Ogg reader would prove less -
/// the packet path would then be fed by the same code it is being compared against.
/// </para>
/// <para>Single logical stream only, which is what every fixture in tests/Assets/audio is.</para>
/// </remarks>
internal static class OggPacketReader
{
    /// <summary>Reads every packet of an Ogg file, in order, headers first.</summary>
    /// <param name="filePath">The .ogg file to take apart.</param>
    /// <returns>The packets, each one complete and without container framing.</returns>
    public static List<byte[]> ReadPackets(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var packets = new List<byte[]>();
        var current = new List<byte>();
        var offset = 0;

        while (offset + 27 <= bytes.Length)
        {
            if (bytes[offset] != (byte)'O' || bytes[offset + 1] != (byte)'g' ||
                bytes[offset + 2] != (byte)'g' || bytes[offset + 3] != (byte)'S')
            {
                throw new InvalidDataException($"No Ogg page capture pattern at offset {offset}.");
            }

            var segmentCount = bytes[offset + 26];
            var segmentTable = offset + 27;
            var data = segmentTable + segmentCount;
            if (data > bytes.Length)
            {
                throw new InvalidDataException("Truncated Ogg page header.");
            }

            var dataOffset = data;
            for (var i = 0; i < segmentCount; i++)
            {
                int length = bytes[segmentTable + i];
                if (dataOffset + length > bytes.Length)
                {
                    throw new InvalidDataException("Truncated Ogg page body.");
                }

                for (var b = 0; b < length; b++)
                {
                    current.Add(bytes[dataOffset + b]);
                }
                dataOffset += length;

                // A segment shorter than 255 bytes terminates the packet; a 255-byte segment means
                // the packet continues into the next segment (or onto the next page).
                if (length < 255)
                {
                    packets.Add(current.ToArray());
                    current = new List<byte>();
                }
            }

            offset = dataOffset;
        }

        return packets;
    }

    /// <summary>
    /// Builds the Xiph-laced codec-private data a media container carries for Vorbis: a count byte,
    /// the lengths of the first two headers, then all three headers back to back.
    /// </summary>
    /// <param name="identification">The identification header (packet 0).</param>
    /// <param name="comment">The comment header (packet 1).</param>
    /// <param name="setup">The setup header (packet 2).</param>
    /// <returns>The codec-private bytes.</returns>
    public static byte[] BuildXiphCodecPrivate(byte[] identification, byte[] comment, byte[] setup)
    {
        var bytes = new List<byte> { 2 };
        AppendLacedLength(bytes, identification.Length);
        AppendLacedLength(bytes, comment.Length);
        bytes.AddRange(identification);
        bytes.AddRange(comment);
        bytes.AddRange(setup);
        return bytes.ToArray();
    }

    private static void AppendLacedLength(List<byte> bytes, int length)
    {
        while (length >= 255)
        {
            bytes.Add(255);
            length -= 255;
        }
        bytes.Add((byte)length);
    }
}

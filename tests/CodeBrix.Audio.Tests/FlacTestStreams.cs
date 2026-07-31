using System;
using System.IO;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Builds FLAC streams that the fixture generator cannot produce.
/// </summary>
internal static class FlacTestStreams
{
    /// <summary>
    /// Returns the file's bytes with a SEEKTABLE metadata block spliced in.
    /// </summary>
    /// <remarks>
    /// ffmpeg does not write SEEKTABLE blocks, so without this the decoder's table-driven seek
    /// path would never be exercised. The table holds a real point at the first frame (sample 0,
    /// offset 0 - true for every FLAC stream) plus a placeholder point, which a decoder is
    /// required to ignore.
    /// </remarks>
    public static byte[] WithSyntheticSeekTable(string flacPath)
    {
        var original = File.ReadAllBytes(flacPath);

        // Walk the metadata blocks to find where the audio frames begin, and which block
        // currently carries the "last block" flag.
        var offset = 4; // past "fLaC"
        var lastBlockHeaderOffset = -1;

        while (true)
        {
            lastBlockHeaderOffset = offset;
            var isLast = (original[offset] & 0x80) != 0;
            var length = (original[offset + 1] << 16) | (original[offset + 2] << 8) | original[offset + 3];
            offset += 4 + length;
            if (isLast) break;
        }

        var seekTable = BuildSeekTableBlock();

        using var output = new MemoryStream();
        output.Write(original, 0, offset);
        output.Write(seekTable, 0, seekTable.Length);
        output.Write(original, offset, original.Length - offset);

        var result = output.ToArray();

        // The block that was last is no longer last; our SEEKTABLE is.
        result[lastBlockHeaderOffset] &= 0x7F;
        return result;
    }

    private static byte[] BuildSeekTableBlock()
    {
        const int pointSize = 18;
        var points = new byte[pointSize * 2];

        // Point 1: sample 0 lives at byte 0 of the audio data - true for any FLAC stream.
        WriteInt64BigEndian(points, 0, 0);
        WriteInt64BigEndian(points, 8, 0);
        points[16] = 0x10; // frame sample count, informational
        points[17] = 0x00;

        // Point 2: a placeholder, which is all-ones and must be ignored by a decoder.
        for (var i = pointSize; i < pointSize + 8; i++) points[i] = 0xFF;

        var block = new byte[4 + points.Length];
        block[0] = 0x83; // last-block flag | block type 3 (SEEKTABLE)
        block[1] = (byte)(points.Length >> 16);
        block[2] = (byte)(points.Length >> 8);
        block[3] = (byte)points.Length;
        Array.Copy(points, 0, block, 4, points.Length);
        return block;
    }

    private static void WriteInt64BigEndian(byte[] buffer, int offset, long value)
    {
        for (var i = 0; i < 8; i++) buffer[offset + i] = (byte)(value >> (56 - i * 8));
    }
}

using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>
/// A deliberately minimal Standard MIDI File reader that pulls out note-on times and nothing
/// else. It exists so that a corpus measurement is never blocked by a file the library's own
/// parsers decline: it skips every meta and system event without interpreting any of them, so
/// there is nothing in it to be strict about.
/// </summary>
/// <remarks>
/// Test-only. It supports format 0 and 1 with metrical (ticks-per-quarter-note) division, which
/// is what stem exports use, and it follows the tempo map so its times are in real seconds.
/// SMPTE division and format 2 are not supported and produce an empty result.
/// </remarks>
internal static class MinimalSmfReader
{
    /// <summary>Reads the time of every note-on with a non-zero velocity, in seconds.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The times, in ascending order.</returns>
    public static IReadOnlyList<double> ReadNoteOnTimesSeconds(byte[] bytes)
    {
        var noteOnTicks = new List<long>();
        var tempoChanges = new List<(long tick, int microsecondsPerQuarterNote)>();

        int position = 0;
        if (!ReadChunkHeader(bytes, ref position, "MThd", out int headerLength)) { return Array.Empty<double>(); }

        int format = ReadUInt16(bytes, position);
        int trackCount = ReadUInt16(bytes, position + 2);
        int division = ReadUInt16(bytes, position + 4);
        position += headerLength;
        if (format == 2 || (division & 0x8000) != 0 || division == 0) { return Array.Empty<double>(); }

        for (int track = 0; track < trackCount && position < bytes.Length; track++)
        {
            if (!ReadChunkHeader(bytes, ref position, "MTrk", out int trackLength)) { break; }
            int end = Math.Min(bytes.Length, position + trackLength);
            ReadTrack(bytes, position, end, noteOnTicks, tempoChanges);
            position = end;
        }

        noteOnTicks.Sort();
        tempoChanges.Sort((a, b) => a.tick.CompareTo(b.tick));
        return ToSeconds(noteOnTicks, tempoChanges, division);
    }

    private static void ReadTrack(byte[] bytes, int position, int end, List<long> noteOnTicks,
        List<(long, int)> tempoChanges)
    {
        long tick = 0;
        byte runningStatus = 0;
        while (position < end)
        {
            tick += ReadVariableLength(bytes, ref position);
            if (position >= end) { break; }

            byte status = bytes[position];
            if ((status & 0x80) != 0) { position++; runningStatus = status; }
            else { status = runningStatus; }
            if (status == 0) { break; }

            if (status == 0xFF)
            {
                if (position >= end) { break; }
                byte metaType = bytes[position++];
                int length = ReadVariableLength(bytes, ref position);
                if (metaType == 0x51 && length == 3 && position + 3 <= end)
                {
                    tempoChanges.Add((tick,
                        (bytes[position] << 16) | (bytes[position + 1] << 8) | bytes[position + 2]));
                }
                position += length;
                if (metaType == 0x2F) { return; }
                continue;
            }

            if (status == 0xF0 || status == 0xF7)
            {
                position += ReadVariableLength(bytes, ref position);
                continue;
            }

            int command = status & 0xF0;
            if (command == 0xC0 || command == 0xD0)
            {
                position += 1;
                continue;
            }

            if (position + 2 > end) { break; }
            byte data1 = bytes[position];
            byte data2 = bytes[position + 1];
            position += 2;
            if (command == 0x90 && data2 > 0) { noteOnTicks.Add(tick); }
            _ = data1;
        }
    }

    private static IReadOnlyList<double> ToSeconds(List<long> noteOnTicks,
        List<(long tick, int microsecondsPerQuarterNote)> tempoChanges, int ticksPerQuarterNote)
    {
        var times = new List<double>(noteOnTicks.Count);
        int tempoIndex = 0;
        long lastTick = 0;
        double elapsed = 0.0;
        double secondsPerTick = 0.5 / ticksPerQuarterNote; // 120 bpm until told otherwise

        foreach (long tick in noteOnTicks)
        {
            while (tempoIndex < tempoChanges.Count && tempoChanges[tempoIndex].tick <= tick)
            {
                var change = tempoChanges[tempoIndex++];
                elapsed += (change.tick - lastTick) * secondsPerTick;
                lastTick = change.tick;
                secondsPerTick = change.microsecondsPerQuarterNote / 1000000.0 / ticksPerQuarterNote;
            }
            times.Add(elapsed + ((tick - lastTick) * secondsPerTick));
        }
        return times;
    }

    private static bool ReadChunkHeader(byte[] bytes, ref int position, string expected, out int length)
    {
        length = 0;
        if (position + 8 > bytes.Length) { return false; }
        for (int n = 0; n < 4; n++)
        {
            if (bytes[position + n] != expected[n]) { return false; }
        }
        length = (bytes[position + 4] << 24) | (bytes[position + 5] << 16)
                 | (bytes[position + 6] << 8) | bytes[position + 7];
        position += 8;
        return true;
    }

    private static int ReadUInt16(byte[] bytes, int position) =>
        (bytes[position] << 8) | bytes[position + 1];

    private static int ReadVariableLength(byte[] bytes, ref int position)
    {
        int value = 0;
        for (int n = 0; n < 4 && position < bytes.Length; n++)
        {
            byte b = bytes[position++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) { break; }
        }
        return value;
    }
}

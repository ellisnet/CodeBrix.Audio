using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// Builds Standard MIDI File bytes in code, so that the leniency tests can produce exactly the
/// malformed shapes they are about without carrying a corpus file into the repository.
/// </summary>
internal static class SyntheticMidi
{
    internal const int Division = 480;

    /// <summary>Wraps track chunks in an MThd header.</summary>
    internal static byte[] File(int format, int division, params byte[][] trackChunks)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(BigEndian32(6));
        bytes.AddRange(BigEndian16(format));
        bytes.AddRange(BigEndian16(trackChunks.Length));
        bytes.AddRange(BigEndian16(division));
        foreach (var chunk in trackChunks)
        {
            bytes.AddRange(chunk);
        }
        return bytes.ToArray();
    }

    /// <summary>Wraps track chunks in an MThd header that declares a track count of its own.</summary>
    internal static byte[] FileDeclaring(int format, int division, int declaredTracks, params byte[][] chunks)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(BigEndian32(6));
        bytes.AddRange(BigEndian16(format));
        bytes.AddRange(BigEndian16(declaredTracks));
        bytes.AddRange(BigEndian16(division));
        foreach (var chunk in chunks)
        {
            bytes.AddRange(chunk);
        }
        return bytes.ToArray();
    }

    /// <summary>An arbitrary non-track chunk, of the kind the specification says to skip.</summary>
    internal static byte[] AlienChunk(string fourCc, params byte[] payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes(fourCc));
        bytes.AddRange(BigEndian32(payload.Length));
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    internal static byte[] BigEndian16(int value) => [(byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)];

    internal static byte[] BigEndian32(int value) =>
    [
        (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF),
    ];

    internal static void WriteVarInt(List<byte> target, int value)
    {
        var buffer = new byte[4];
        var n = 0;
        do
        {
            buffer[n++] = (byte)(value & 0x7F);
            value >>= 7;
        }
        while (value > 0);

        while (n > 0)
        {
            n--;
            target.Add(n > 0 ? (byte)(buffer[n] | 0x80) : buffer[n]);
        }
    }
}

/// <summary>
/// Accumulates the events of one MIDI track and wraps them in an MTrk chunk.
/// </summary>
internal sealed class MidiTrackBuilder
{
    private readonly List<byte> _bytes = [];

    internal MidiTrackBuilder Raw(int delta, params byte[] data)
    {
        SyntheticMidi.WriteVarInt(_bytes, delta);
        _bytes.AddRange(data);
        return this;
    }

    /// <summary>Appends bytes with no delta time in front of them, for running-status runs.</summary>
    internal MidiTrackBuilder RawBytes(params byte[] data)
    {
        _bytes.AddRange(data);
        return this;
    }

    internal MidiTrackBuilder Meta(int delta, byte metaType, params byte[] data)
    {
        SyntheticMidi.WriteVarInt(_bytes, delta);
        _bytes.Add(0xFF);
        _bytes.Add(metaType);
        SyntheticMidi.WriteVarInt(_bytes, data.Length);
        _bytes.AddRange(data);
        return this;
    }

    /// <summary>A meta event whose declared length deliberately disagrees with its payload.</summary>
    internal MidiTrackBuilder MetaWithDeclaredLength(int delta, byte metaType, int declaredLength, params byte[] data)
    {
        SyntheticMidi.WriteVarInt(_bytes, delta);
        _bytes.Add(0xFF);
        _bytes.Add(metaType);
        SyntheticMidi.WriteVarInt(_bytes, declaredLength);
        _bytes.AddRange(data);
        return this;
    }

    internal MidiTrackBuilder TrackName(int delta, byte[] rawName) => Meta(delta, 0x03, rawName);

    internal MidiTrackBuilder KeySignature(int delta, int sharpsFlats, int majorMinor) =>
        Meta(delta, 0x59, unchecked((byte)sharpsFlats), (byte)majorMinor);

    internal MidiTrackBuilder Tempo(int delta, int microsecondsPerQuarterNote) =>
        Meta(delta, 0x51,
            (byte)((microsecondsPerQuarterNote >> 16) & 0xFF),
            (byte)((microsecondsPerQuarterNote >> 8) & 0xFF),
            (byte)(microsecondsPerQuarterNote & 0xFF));

    internal MidiTrackBuilder ProgramChange(int delta, int channel, int program) =>
        Raw(delta, (byte)(0xC0 | (channel - 1)), (byte)program);

    internal MidiTrackBuilder ControlChange(int delta, int channel, int controller, int value) =>
        Raw(delta, (byte)(0xB0 | (channel - 1)), (byte)controller, (byte)value);

    internal MidiTrackBuilder NoteOn(int delta, int channel, int note, int velocity) =>
        Raw(delta, (byte)(0x90 | (channel - 1)), (byte)note, (byte)velocity);

    internal MidiTrackBuilder NoteOff(int delta, int channel, int note, int velocity) =>
        Raw(delta, (byte)(0x80 | (channel - 1)), (byte)note, (byte)velocity);

    internal MidiTrackBuilder EndOfTrack(int delta = 0) => Meta(delta, 0x2F);

    internal byte[] ToChunk()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MTrk"));
        bytes.AddRange(SyntheticMidi.BigEndian32(_bytes.Count));
        bytes.AddRange(_bytes);
        return bytes.ToArray();
    }

    /// <summary>The track's event bytes without the MTrk wrapper.</summary>
    internal byte[] ToEventBytes() => _bytes.ToArray();
}

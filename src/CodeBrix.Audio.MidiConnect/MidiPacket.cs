using System;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>A complete MIDI 1.0 wire message with owned storage. Channels are numbered 1 through 16.</summary>
public sealed class MidiPacket
{
    private readonly byte[] bytes;

    /// <summary>Validates and copies exactly one message, including F0/F7 for SysEx.</summary>
    public MidiPacket(ReadOnlySpan<byte> data)
    {
        Validate(data);
        bytes = data.ToArray();
    }

    /// <summary>Read-only message bytes. Storage remains valid after the receive callback returns.</summary>
    public ReadOnlyMemory<byte> Data => bytes;
    /// <summary>Status byte, including the zero-based channel bits for channel messages.</summary>
    public byte Status => bytes[0];
    /// <summary>Channel 1–16, or zero for a system message.</summary>
    public int Channel => Status < 0xF0 ? (Status & 15) + 1 : 0;
    /// <summary>Command without the channel bits, or the full system status.</summary>
    public int Command => Status < 0xF0 ? Status & 0xF0 : Status;
    /// <summary>First data byte, or zero when absent.</summary>
    public int Data1 => bytes.Length > 1 ? bytes[1] : 0;
    /// <summary>Second data byte, or zero when absent.</summary>
    public int Data2 => bytes.Length > 2 ? bytes[2] : 0;
    /// <summary>Whether this is a note-on with nonzero velocity.</summary>
    public bool IsNoteOn => Command == 0x90 && Data2 != 0;
    /// <summary>Whether this is note-off, including note-on with zero velocity.</summary>
    public bool IsNoteOff => Command == 0x80 || (Command == 0x90 && Data2 == 0);
    /// <summary>Whether this is a complete system-exclusive message.</summary>
    public bool IsSystemExclusive => Status == 0xF0;

    /// <summary>Creates a channel message. Data values must be 0–127; channels are 1–16.</summary>
    public static MidiPacket ChannelMessage(int command, int channel, int data1, int data2 = 0)
    {
        if (command < 0x80 || command > 0xE0 || (command & 15) != 0)
            throw new ArgumentOutOfRangeException(nameof(command));
        if (channel < 1 || channel > 16) throw new ArgumentOutOfRangeException(nameof(channel));
        if ((uint)data1 > 127) throw new ArgumentOutOfRangeException(nameof(data1));
        if ((uint)data2 > 127) throw new ArgumentOutOfRangeException(nameof(data2));
        byte status = (byte)(command | (channel - 1));
        return command is 0xC0 or 0xD0
            ? new MidiPacket(new byte[] { status, (byte)data1 })
            : new MidiPacket(new byte[] { status, (byte)data1, (byte)data2 });
    }

    /// <summary>Converts to the Core editable MIDI model. System common/realtime use an SMF F7 escape event.</summary>
    public MidiEvent ToMidiEvent(long absoluteTicks)
    {
        if (absoluteTicks < 0) throw new ArgumentOutOfRangeException(nameof(absoluteTicks));
        if (IsSystemExclusive) return new SysexEvent(absoluteTicks, bytes.AsSpan(1, bytes.Length - 2).ToArray());
        if (Channel == 0) return SysexEvent.FromFileData(absoluteTicks, MidiCommandCode.Eox, bytes);
        var result = MidiEvent.FromRawMessage(Status | (Data1 << 8) | (Data2 << 16));
        result.AbsoluteTime = absoluteTicks;
        return result;
    }

    internal static int MessageLength(byte status) => status switch
    {
        < 0x80 => 0,
        < 0xC0 => 3,
        < 0xE0 => 2,
        < 0xF0 => 3,
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        0xF6 or 0xF8 or 0xFA or 0xFB or 0xFC or 0xFE or 0xFF => 1,
        _ => 0
    };

    internal static void Validate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new ArgumentException("A MIDI message cannot be empty.", nameof(data));
        if (data[0] == 0xF0)
        {
            if (data.Length < 2 || data[^1] != 0xF7)
                throw new ArgumentException("SysEx must include F0 and F7 framing.", nameof(data));
            for (int i = 1; i < data.Length - 1; i++)
                if (data[i] >= 0x80) throw new ArgumentException("SysEx payload bytes must be seven-bit data.", nameof(data));
        }
        else
        {
            int length = MessageLength(data[0]);
            if (length == 0 || data.Length != length)
                throw new ArgumentException("Expected exactly one complete, defined MIDI 1.0 message.", nameof(data));
            for (int i = 1; i < data.Length; i++)
                if (data[i] >= 0x80) throw new ArgumentException("MIDI data bytes must be seven-bit data.", nameof(data));
        }
    }

    /// <summary>Formats the message as hexadecimal bytes.</summary>
    public override string ToString() => Convert.ToHexString(bytes);
}

/// <summary>A message received from an input.</summary>
public sealed class MidiMessageReceivedEventArgs : EventArgs
{
    internal MidiMessageReceivedEventArgs(MidiPacket message, TimeSpan timestamp)
    {
        Message = message;
        Timestamp = timestamp;
    }
    /// <summary>The complete message, with storage independent of the native buffer.</summary>
    public MidiPacket Message { get; }
    /// <summary>Monotonic time on <see cref="MidiClock"/>'s process-local clock, captured before dispatch.</summary>
    public TimeSpan Timestamp { get; }
}

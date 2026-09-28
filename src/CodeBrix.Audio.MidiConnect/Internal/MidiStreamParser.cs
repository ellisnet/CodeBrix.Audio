using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MidiConnect.Internal;

// MIDI 1.0 framing: callbacks may contain fractions of messages, multiple messages,
// running status, or realtime messages embedded within another message (including SysEx).
internal sealed class MidiStreamParser
{
    private readonly int maxSysexBytes;
    private readonly Action<MidiPacket, TimeSpan> emit;
    private readonly Action<Exception> error;
    private readonly byte[] shortMessage = new byte[3];
    private readonly List<byte> sysex = new();
    private byte runningStatus;
    private int count;
    private int length;
    private bool inSysex;
    private bool discardSysex;
    private TimeSpan started;

    internal bool HasIncompleteMessage => inSysex || count != 0;

    internal MidiStreamParser(int maxSysexBytes, Action<MidiPacket, TimeSpan> emit, Action<Exception> error)
    {
        this.maxSysexBytes = maxSysexBytes;
        this.emit = emit;
        this.error = error;
    }

    internal void Feed(ReadOnlySpan<byte> data, TimeSpan timestamp)
    {
        foreach (byte value in data)
        {
            if (value >= 0xF8)
            {
                if (MidiPacket.MessageLength(value) != 0) emit(new MidiPacket(new[] { value }), timestamp);
                else error(new MidiDeviceException($"Undefined MIDI status 0x{value:X2}."));
                continue;
            }
            if (inSysex)
            {
                if (value < 0x80 || value == 0xF7)
                {
                    if (!discardSysex)
                    {
                        if (sysex.Count == maxSysexBytes)
                        {
                            discardSysex = true;
                            sysex.Clear();
                            error(new MidiDeviceException("Incoming SysEx exceeded the configured size limit; the message was discarded."));
                        }
                        else sysex.Add(value);
                    }
                    if (value == 0xF7)
                    {
                        if (!discardSysex) emit(new MidiPacket(sysex.ToArray()), started);
                        inSysex = false;
                        sysex.Clear();
                    }
                    continue;
                }
                inSysex = false;
                sysex.Clear();
                error(new MidiDeviceException("A new status interrupted an incomplete SysEx message."));
            }
            if (value >= 0x80)
            {
                if (count != 0) error(new MidiDeviceException("A new status interrupted an incomplete MIDI message."));
                count = 0;
                runningStatus = value < 0xF0 ? value : (byte)0;
                started = timestamp;
                if (value == 0xF0)
                {
                    inSysex = true;
                    discardSysex = false;
                    sysex.Add(value);
                    continue;
                }
                length = MidiPacket.MessageLength(value);
                if (length == 0)
                {
                    error(new MidiDeviceException($"Unexpected MIDI status 0x{value:X2}."));
                    continue;
                }
                shortMessage[count++] = value;
            }
            else
            {
                if (count == 0)
                {
                    if (runningStatus == 0)
                    {
                        error(new MidiDeviceException("Received a MIDI data byte without a status."));
                        continue;
                    }
                    shortMessage[count++] = runningStatus;
                    length = MidiPacket.MessageLength(runningStatus);
                    started = timestamp;
                }
                shortMessage[count++] = value;
            }
            if (count == length)
            {
                emit(new MidiPacket(shortMessage.AsSpan(0, count)), started);
                count = 0;
            }
        }
    }
}

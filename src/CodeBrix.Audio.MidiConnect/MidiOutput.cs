using System;
using CodeBrix.Audio.MidiConnect.Internal;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>An open MIDI output. Concurrent sends and disposal are serialized.</summary>
public sealed class MidiOutput : IDisposable
{
    private readonly object gate = new();
    private readonly IMidiOutputConnection connection;
    private bool closed;
    internal MidiOutput(MidiPortInfo port, IMidiOutputConnection connection) { Port = port; this.connection = connection; }
    /// <summary>The destination port.</summary>
    public MidiPortInfo Port { get; }
    /// <summary>Whether this connection has not been closed or invalidated.</summary>
    public bool IsOpen { get { lock (gate) return !closed; } }
    /// <summary>Sends one complete message immediately. SysEx includes F0/F7.</summary>
    public void Send(MidiPacket message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            connection.Send(message.Data.Span);
        }
    }
    /// <summary>Validates and sends one complete message immediately.</summary>
    public void Send(ReadOnlySpan<byte> message) => Send(new MidiPacket(message));
    /// <summary>Sends a note-on. Channel is 1–16; note and velocity are 0–127.</summary>
    public void NoteOn(int channel, int note, int velocity = 100) => Send(MidiPacket.ChannelMessage(0x90, channel, note, velocity));
    /// <summary>Sends a note-off. Channel is 1–16.</summary>
    public void NoteOff(int channel, int note, int velocity = 0) => Send(MidiPacket.ChannelMessage(0x80, channel, note, velocity));
    /// <summary>Sends a controller change. Channel is 1–16.</summary>
    public void ControlChange(int channel, int controller, int value) => Send(MidiPacket.ChannelMessage(0xB0, channel, controller, value));
    /// <summary>Selects a program 0–127 on channel 1–16.</summary>
    public void ProgramChange(int channel, int program) => Send(MidiPacket.ChannelMessage(0xC0, channel, program));
    /// <summary>Sends a pitch-bend value 0–16383 (8192 is centered) on channel 1–16.</summary>
    public void PitchBend(int channel, int value)
    {
        if ((uint)value > 16383) throw new ArgumentOutOfRangeException(nameof(value));
        Send(MidiPacket.ChannelMessage(0xE0, channel, value & 127, value >> 7));
    }
    /// <summary>Releases sustain and sends all-notes-off and all-sound-off on one channel, or all channels when zero.</summary>
    public void Panic(int channel = 0)
    {
        if ((uint)channel > 16) throw new ArgumentOutOfRangeException(nameof(channel));
        for (int ch = channel == 0 ? 1 : channel; ch <= (channel == 0 ? 16 : channel); ch++)
        {
            ControlChange(ch, 64, 0);
            ControlChange(ch, 123, 0);
            ControlChange(ch, 120, 0);
        }
    }
    /// <summary>Closes the port. Does not send panic messages that could affect another application's performance.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;
            connection.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>A MIDI message positioned relative to the start of a recording or playback.</summary>
public sealed record MidiTimedMessage
{
    /// <summary>Creates a positioned message. Time must be nonnegative.</summary>
    public MidiTimedMessage(TimeSpan time, MidiPacket message)
    {
        if (time < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(time));
        ArgumentNullException.ThrowIfNull(message);
        Time = time;
        Message = message;
    }
    /// <summary>Time relative to the beginning.</summary>
    public TimeSpan Time { get; }
    /// <summary>Complete message with owned storage.</summary>
    public MidiPacket Message { get; }
}

/// <summary>A performed note. Duration measures key/pad hold time, independently of sustain-pedal sound.</summary>
public sealed record MidiRecordedNote
{
    internal MidiRecordedNote(MidiTimedMessage start, TimeSpan end, int releaseVelocity, bool truncated)
    {
        Channel = start.Message.Channel;
        Note = start.Message.Data1;
        Velocity = start.Message.Data2;
        Start = start.Time;
        Duration = end - start.Time;
        ReleaseVelocity = releaseVelocity;
        IsTruncated = truncated;
    }
    /// <summary>Channel 1–16.</summary>
    public int Channel { get; }
    /// <summary>Note number 0–127.</summary>
    public int Note { get; }
    /// <summary>Attack velocity 1–127.</summary>
    public int Velocity { get; }
    /// <summary>Release velocity 0–127, or zero for a synthesized ending.</summary>
    public int ReleaseVelocity { get; }
    /// <summary>Time of the note-on.</summary>
    public TimeSpan Start { get; }
    /// <summary>Time until the matching note-off, channel-wide release, or end of recording.</summary>
    public TimeSpan Duration { get; }
    /// <summary>True when recording ended before this note was released.</summary>
    public bool IsTruncated { get; }
}

/// <summary>An immutable snapshot of a captured performance, including all defined MIDI 1.0 messages.</summary>
public sealed class MidiRecording
{
    internal MidiRecording(IEnumerable<MidiTimedMessage> messages, TimeSpan duration, Exception failure)
    {
        Messages = Array.AsReadOnly(messages.OrderBy(m => m.Time).ToArray());
        Duration = Messages.Count == 0 || duration >= Messages[^1].Time ? duration : Messages[^1].Time;
        Failure = failure;
        Notes = BuildNotes();
    }
    /// <summary>Messages in time order; equal-time messages retain receive order.</summary>
    public IReadOnlyList<MidiTimedMessage> Messages { get; }
    /// <summary>Notes in start order. Overlapping instances of the same key are matched first-in, first-out.</summary>
    public IReadOnlyList<MidiRecordedNote> Notes { get; }
    /// <summary>Recording length, including trailing silence.</summary>
    public TimeSpan Duration { get; }
    /// <summary>The input or capacity failure that ended capture, or null for a normal stop.</summary>
    public Exception Failure { get; }

    private IReadOnlyList<MidiRecordedNote> BuildNotes()
    {
        var active = new Dictionary<(int Channel, int Note), Queue<MidiTimedMessage>>();
        var notes = new List<MidiRecordedNote>();
        foreach (var item in Messages)
        {
            var message = item.Message;
            var key = (message.Channel, message.Data1);
            if (message.IsNoteOn)
            {
                if (!active.TryGetValue(key, out var queue)) active[key] = queue = new();
                queue.Enqueue(item);
            }
            else if (message.IsNoteOff && active.TryGetValue(key, out var queue) && queue.Count > 0)
                notes.Add(new MidiRecordedNote(queue.Dequeue(), item.Time, message.Data2, false));
            else if (message.Status == 0xFF || (message.Command == 0xB0 && (message.Data1 == 120 || message.Data1 >= 123)))
                foreach (var pair in active.Where(p => message.Status == 0xFF || p.Key.Channel == message.Channel))
                    while (pair.Value.Count > 0) notes.Add(new MidiRecordedNote(pair.Value.Dequeue(), item.Time, 0, false));
        }
        foreach (var queue in active.Values)
            while (queue.Count > 0) notes.Add(new MidiRecordedNote(queue.Dequeue(), Duration, 0, true));
        return Array.AsReadOnly(notes.OrderBy(n => n.Start).ToArray());
    }

    /// <summary>Creates a type-0 MIDI file model at a constant tempo, preserving performance timing.
    /// System common/realtime messages use SMF F7 escapes. By default, held notes and sustain are released at the end.</summary>
    public MidiEventCollection ToMidiEventCollection(double beatsPerMinute = 120, int ticksPerQuarterNote = 960, bool releaseAtEnd = true)
    {
        if (!double.IsFinite(beatsPerMinute) || beatsPerMinute <= 0) throw new ArgumentOutOfRangeException(nameof(beatsPerMinute));
        double tempoValue = Math.Round(60000000 / beatsPerMinute);
        if (tempoValue < 1 || tempoValue > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(beatsPerMinute));
        if (ticksPerQuarterNote < 1 || ticksPerQuarterNote > 32767) throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote));
        int tempo = (int)tempoValue;
        long Tick(TimeSpan time) => checked((long)Math.Round((decimal)time.Ticks * ticksPerQuarterNote / (tempo * 10m)));
        var events = new MidiEventCollection(0, ticksPerQuarterNote);
        var track = events.AddTrack();
        track.Add(new TempoEvent(tempo, 0));
        foreach (var item in Messages) track.Add(item.Message.ToMidiEvent(Tick(item.Time)));
        long end = Tick(Duration);
        if (releaseAtEnd)
        {
            foreach (var note in Notes.Where(n => n.IsTruncated))
                track.Add(new NoteEvent(end, note.Channel, MidiCommandCode.NoteOff, note.Note, 0));
            foreach (int channel in Messages.Select(m => m.Message.Channel).Where(c => c != 0).Distinct())
                track.Add(new ControlChangeEvent(end, channel, MidiController.Sustain, 0));
        }
        track.Add(new MetaEvent(MetaEventType.EndTrack, 0, end));
        events.PrepareForExport();
        return events;
    }

    /// <summary>Saves a Standard MIDI File. See ToMidiEventCollection for timing and ending behavior.</summary>
    public void Save(string path, double beatsPerMinute = 120, int ticksPerQuarterNote = 960) =>
        MidiFile.Export(path, ToMidiEventCollection(beatsPerMinute, ticksPerQuarterNote));
}

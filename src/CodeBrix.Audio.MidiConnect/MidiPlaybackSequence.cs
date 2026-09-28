using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MidiConnect.Internal;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>An immutable MIDI output timeline. No device is opened while preparing it.</summary>
public sealed class MidiPlaybackSequence
{
    /// <summary>Copies and stably sorts messages. An optional duration preserves trailing silence.</summary>
    public MidiPlaybackSequence(IEnumerable<MidiTimedMessage> messages, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var items = messages.ToArray();
        if (items.Any(m => m == null)) throw new ArgumentException("Messages cannot contain null.", nameof(messages));
        items = items.OrderBy(m => m.Time).ToArray();
        TimeSpan last = items.Length == 0 ? TimeSpan.Zero : items[^1].Time;
        if (duration.HasValue && duration.Value < last) throw new ArgumentOutOfRangeException(nameof(duration));
        Messages = Array.AsReadOnly(items);
        Duration = duration ?? last;
    }
    /// <summary>Messages in delivery order.</summary>
    public IReadOnlyList<MidiTimedMessage> Messages { get; }
    /// <summary>Time until playback completes, including trailing silence.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Prepares an already-loaded MIDI file. Check MidiFile.Problems when using tolerant reading.</summary>
    public static MidiPlaybackSequence FromMidiFile(MidiFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return FromEvents(file.Events);
    }

    /// <summary>Prepares type 0/1 events, honoring tempo changes and PPQN or SMPTE timing.
    /// Metadata is not transmitted. Complete SysEx and escaped complete MIDI messages are supported.
    /// Timed SysEx continuations are rejected, because the native backends require complete messages.</summary>
    /// <remarks>Do not modify the collection during this call. Type-1 equal-time events retain track/event order;
    /// tempo changes in any track are honored in that order. Type 2 contains separate songs and must be selected/converted first.</remarks>
    public static MidiPlaybackSequence FromEvents(MidiEventCollection events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.MidiFileType is not (0 or 1)) throw new NotSupportedException("Select one song from a type-2 MIDI file and convert it to type 0 or 1 before playback.");
        int division = events.DeltaTicksPerQuarterNote;
        if (division < short.MinValue || division > ushort.MaxValue || division == 0) throw new FormatException("Invalid MIDI time division.");
        bool smpte = (division & 0x8000) != 0;
        decimal ticksPerSecond = 0;
        if (smpte)
        {
            int frameCode = -(sbyte)(division >> 8);
            int subframes = division & 255;
            decimal frames = frameCode switch { 24 => 24, 25 => 25, 29 => 30000m / 1001m, 30 => 30, _ => 0 };
            if (frames == 0 || subframes == 0) throw new FormatException("Invalid MIDI SMPTE time division.");
            ticksPerSecond = frames * subframes;
        }
        var flattened = new List<MidiEvent>();
        for (int track = 0; track < events.Tracks; track++) flattened.AddRange(events[track]);
        if (flattened.Any(e => e == null || e.AbsoluteTime < 0)) throw new FormatException("MIDI events must have nonnegative absolute times.");
        var result = new List<MidiTimedMessage>();
        long previousTick = 0;
        int tempo = 500000;
        decimal timeTicks = 0;
        TimeSpan time = TimeSpan.Zero;
        foreach (var ev in flattened.OrderBy(e => e.AbsoluteTime))
        {
            long delta = ev.AbsoluteTime - previousTick;
            timeTicks += smpte ? delta * (decimal)TimeSpan.TicksPerSecond / ticksPerSecond : delta * (decimal)tempo * 10 / division;
            time = TimeSpan.FromTicks(checked((long)Math.Round(timeTicks)));
            previousTick = ev.AbsoluteTime;
            if (ev is TempoEvent change)
            {
                if (change.MicrosecondsPerQuarterNote <= 0) throw new FormatException("A MIDI tempo must be positive.");
                tempo = change.MicrosecondsPerQuarterNote;
            }
            else if (ev is SysexEvent sysex)
            {
                // Parse the SMF payload, not its length/status framing. F7 escapes may contain
                // several messages, including system-common/realtime that ordinary SMF events cannot model.
                var parser = new MidiStreamParser(1024 * 1024,
                    (message, timestamp) => result.Add(new MidiTimedMessage(timestamp, message)),
                    exception => throw new FormatException("Invalid or unsupported MIDI file SysEx/escape payload.", exception));
                if (sysex.CommandCode == MidiCommandCode.Sysex) parser.Feed(new byte[] { 0xF0 }, time);
                parser.Feed(sysex.GetFileData(), time);
                if (parser.HasIncompleteMessage)
                    throw new NotSupportedException("The MIDI file contains a split SysEx or incomplete escaped message. Device playback requires complete messages in each file event.");
            }
            else if (ev is not MetaEvent)
            {
                int raw = ev.GetAsShortMessage();
                byte status = (byte)raw;
                int length = MidiPacket.MessageLength(status);
                if (length == 0) throw new NotSupportedException($"Cannot send MIDI file event {ev.GetType().Name}.");
                byte[] bytes = { status, (byte)(raw >> 8), (byte)(raw >> 16) };
                result.Add(new MidiTimedMessage(time, new MidiPacket(bytes.AsSpan(0, length))));
            }
        }
        return new MidiPlaybackSequence(result, time);
    }
}

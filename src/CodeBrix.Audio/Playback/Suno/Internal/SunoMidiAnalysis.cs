using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// What a stem's MIDI file says about the stem: which instrument and channel it plays on, how many
/// notes it holds, when they start, and how much of the song they cover.
/// </summary>
internal static class SunoMidiAnalysis
{
    /// <summary>The result of reading a stem's MIDI file through once.</summary>
    internal sealed class Result
    {
        internal int NoteCount { get; set; }

        internal double[] NoteOnTimes { get; set; } = [];

        internal int Program { get; set; } = -1;

        internal int Channel { get; set; } = -1;

        internal TimeSpan SoundingTime { get; set; }
    }

    /// <summary>
    /// Walks a sequence once, gathering everything a stem needs from it.
    /// </summary>
    /// <param name="sequence">The stem's MIDI, read tolerantly.</param>
    /// <param name="minimumNoteHold">The shortest a note is taken to sound; see the load options.</param>
    /// <returns>What the file said.</returns>
    internal static Result Analyse(MidiSequence sequence, TimeSpan minimumNoteHold)
    {
        var result = new Result();
        var messages = sequence.Messages;
        var times = sequence.Times;
        var minimumSeconds = minimumNoteHold.TotalSeconds;

        var noteOns = new List<double>();
        var intervals = new List<(double Start, double End)>();
        var sounding = new Dictionary<int, List<double>>();

        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            if (message.Type != MidiSequence.MessageType.Normal)
            {
                continue;
            }

            var seconds = times[i].TotalSeconds;
            var channel = message.Channel + 1;

            switch (message.Command)
            {
                case 0xC0:
                    if (result.Program < 0)
                    {
                        result.Program = message.Data1;
                        result.Channel = channel;
                    }

                    break;

                case 0x90 when message.Data2 > 0:
                    if (result.Channel < 0)
                    {
                        result.Channel = channel;
                    }

                    result.NoteCount++;
                    noteOns.Add(seconds);
                    Key(sounding, message.Channel, message.Data1).Add(seconds);
                    break;

                case 0x90:
                case 0x80:
                    var open = Key(sounding, message.Channel, message.Data1);
                    if (open.Count > 0)
                    {
                        var start = open[0];
                        open.RemoveAt(0);
                        intervals.Add((start, Math.Max(seconds, start + minimumSeconds)));
                    }

                    break;
            }
        }

        // Anything the file never released - tolerant reading closes these, but a caller may have
        // built a sequence some other way - still sounds for at least the minimum hold.
        foreach (var open in sounding.Values)
        {
            foreach (var start in open)
            {
                intervals.Add((start, start + minimumSeconds));
            }
        }

        noteOns.Sort();
        result.NoteOnTimes = noteOns.ToArray();
        result.SoundingTime = TimeSpan.FromSeconds(TotalCovered(intervals));
        return result;
    }

    /// <summary>
    /// The tempo map a stem's MIDI carries: one entry per tempo event, in time order.
    /// </summary>
    /// <param name="midiBytes">The bytes of the stem's MIDI file.</param>
    /// <returns>The tempo map, empty when the file carries no tempo event or will not read.</returns>
    /// <remarks>
    /// This reads the file through <see cref="MidiFile"/> rather than through the sequence, because
    /// a sequence applies its tempo events while merging its tracks onto one timeline and does not
    /// keep them. The file is a few kilobytes, and only the tempo events are taken from it.
    /// </remarks>
    internal static SunoTempoChange[] ReadTempoMap(byte[] midiBytes)
    {
        MidiFile file;
        try
        {
            using var stream = new MemoryStream(midiBytes, false);
            file = new MidiFile(stream, MidiReadMode.Tolerant);
        }
        catch (Exception)
        {
            return [];
        }

        var ticksPerQuarterNote = file.DeltaTicksPerQuarterNote;
        if (ticksPerQuarterNote <= 0)
        {
            return [];
        }

        var events = new List<TempoEvent>();
        foreach (var track in file.Events)
        {
            foreach (var midiEvent in track)
            {
                if (midiEvent is TempoEvent tempo)
                {
                    events.Add(tempo);
                }
            }
        }

        events.Sort((left, right) => left.AbsoluteTime.CompareTo(right.AbsoluteTime));

        var map = new List<SunoTempoChange>(events.Count);
        var beatsPerMinute = 120.0;
        long tick = 0;
        var seconds = 0.0;

        foreach (var tempoEvent in events)
        {
            seconds += 60.0 / (ticksPerQuarterNote * beatsPerMinute) * (tempoEvent.AbsoluteTime - tick);
            tick = tempoEvent.AbsoluteTime;
            beatsPerMinute = tempoEvent.Tempo;
            map.Add(new SunoTempoChange(MidiSequence.GetTimeSpanFromSeconds(seconds), beatsPerMinute));
        }

        return map.ToArray();
    }

    private static List<double> Key(Dictionary<int, List<double>> sounding, int channel, int note)
    {
        var key = (channel << 8) | note;
        if (!sounding.TryGetValue(key, out var list))
        {
            list = new List<double>();
            sounding[key] = list;
        }

        return list;
    }

    private static double TotalCovered(List<(double Start, double End)> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0;
        }

        intervals.Sort((left, right) => left.Start.CompareTo(right.Start));

        var total = 0.0;
        var start = intervals[0].Start;
        var end = intervals[0].End;

        for (var i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Start <= end)
            {
                end = Math.Max(end, intervals[i].End);
            }
            else
            {
                total += end - start;
                start = intervals[i].Start;
                end = intervals[i].End;
            }
        }

        return total + (end - start);
    }
}

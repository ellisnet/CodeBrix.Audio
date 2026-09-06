using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Turns the MIDI tracks of a <see cref="MultiTrackPlayer"/> back into one General MIDI file: each
/// track on its own channel and its own SMF track, percussion on channel 10, one shared tempo map,
/// and every track's timing offset already applied.
/// </summary>
/// <remarks>
/// <para>
/// A stems export arrives as one MIDI file per stem, each on its own clock and each claiming
/// whatever channel it likes. Nothing outside this library can open twelve files as one song, so
/// this puts the song back together: what comes out loads in any sequencer and plays the
/// arrangement the player plays.
/// </para>
/// <para>
/// The conversion is time-to-tick, because a <see cref="MidiSequence"/> has already been flattened
/// to absolute time. The tempo map that flattening applied is what converts it back, so the ticks
/// land where they started - subject to the resolution written into the file
/// (<see cref="TicksPerQuarterNote"/>, one tick being about a third of a millisecond at 120 BPM).
/// </para>
/// </remarks>
internal static class MergedMidiBuilder
{
    /// <summary>The resolution the merged file is written at, in ticks per quarter note.</summary>
    internal const int TicksPerQuarterNote = 480;

    // The General MIDI drum channel, zero-based (channel 10 as musicians count them).
    private const int PercussionChannel = 9;

    /// <summary>Builds the merged event collection.</summary>
    /// <param name="tracks">Every track of the player; those without a MIDI source are skipped.</param>
    /// <param name="problems">Receives one line per thing that could not be honoured.</param>
    /// <returns>A type 1 collection, sorted and end-of-track terminated, ready for <c>MidiFile.Export</c>.</returns>
    internal static MidiEventCollection Build(IReadOnlyList<PlayerTrack> tracks, List<string> problems)
    {
        var events = new MidiEventCollection(1, TicksPerQuarterNote);

        MidiTempoMap tempoMap = null;
        foreach (var track in tracks)
        {
            if (track.HasMidiSource)
            {
                tempoMap = track.MidiSequence.TempoMap;
                break;
            }
        }

        if (tempoMap == null)
        {
            problems.Add("No track has a MIDI source, so the merged export is empty.");
            events.AddEvent(new MetaEvent(MetaEventType.EndTrack, 0, 0), 1);
            events.PrepareForExport();
            return events;
        }

        WriteTempoMap(events, tempoMap);

        var nextChannel = 0;
        var trackNumber = 1;

        foreach (var track in tracks)
        {
            if (!track.HasMidiSource)
            {
                continue;
            }

            int channel;
            if (track.IsPercussion)
            {
                channel = PercussionChannel;
            }
            else
            {
                while (nextChannel == PercussionChannel)
                {
                    nextChannel++;
                }

                if (nextChannel > 15)
                {
                    channel = 15;
                    problems.Add(
                        $"Track '{track.Name}' shares MIDI channel 16 with another track: a General " +
                        "MIDI file has only fifteen melodic channels.");
                }
                else
                {
                    channel = nextChannel;
                    nextChannel++;
                }
            }

            trackNumber++;
            WriteTrack(events, trackNumber, track, tempoMap, channel);
        }

        events.PrepareForExport();
        return events;
    }

    private static void WriteTempoMap(MidiEventCollection events, MidiTempoMap tempoMap)
    {
        foreach (var change in tempoMap.Changes)
        {
            var tick = (long)Math.Round(change.BeatPosition * TicksPerQuarterNote);
            var microseconds = (int)Math.Round(60000000.0 / change.BeatsPerMinute);
            events.AddEvent(new TempoEvent(microseconds, tick < 0 ? 0 : tick), 1);
        }
    }

    private static void WriteTrack(
        MidiEventCollection events,
        int trackNumber,
        PlayerTrack track,
        MidiTempoMap tempoMap,
        int channel)
    {
        // MidiEvent numbers channels 1-16; everything inside the synth path numbers them 0-15.
        var midiChannel = channel + 1;

        if (!string.IsNullOrEmpty(track.Name))
        {
            events.AddEvent(new TextEvent(track.Name, MetaEventType.SequenceTrackName, 0), trackNumber);
        }

        if (track.GmProgram >= 0)
        {
            events.AddEvent(new PatchChangeEvent(0, midiChannel, track.GmProgram), trackNumber);
        }

        // Notes are given a minimum length so that the file carries the same percussion rule the
        // player applies. Without it a drum part written with zero-length notes exports as a file
        // that is silent in every sequencer that opens it.
        var hold = track.IgnoreNoteOff && track.MinimumNoteHold <= TimeSpan.Zero
            ? PlayerTrack.DefaultMinimumNoteHold
            : track.MinimumNoteHold;

        var sequence = track.MidiSequence;
        var messages = sequence.Messages;
        var times = sequence.Times;

        // The end of a note is written when the note-off is reached, so a note whose hold has not
        // elapsed can have its release pushed out. Key: (note << 4) is not enough - the source
        // channel is irrelevant here, every message lands on one channel - so the note number is
        // the whole key.
        var noteOnTime = new TimeSpan?[128];

        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            if (message.Type != MidiSequence.MessageType.Normal)
            {
                continue;
            }

            var time = times[i] + track.Offset + track.MidiSourceOffset;
            if (time < TimeSpan.Zero)
            {
                continue;
            }

            var command = message.Command;
            var data1 = message.Data1;
            var data2 = message.Data2;
            var isNoteOff = command == 0x80 || (command == 0x90 && data2 == 0);

            if (command == 0x90 && data2 > 0)
            {
                if (data1 < 128)
                {
                    noteOnTime[data1] = time;
                }

                events.AddEvent(new NoteEvent(ToTicks(tempoMap, time), midiChannel, MidiCommandCode.NoteOn, data1, data2), trackNumber);
                continue;
            }

            if (isNoteOff)
            {
                if (track.IgnoreNoteOff)
                {
                    // The player never sends these; the file would be a set of stuck notes without
                    // something in their place, so the synthesized release below covers it.
                    continue;
                }

                var releaseTime = time;
                if (data1 < 128 && noteOnTime[data1].HasValue)
                {
                    var earliest = noteOnTime[data1].Value + hold;
                    if (releaseTime < earliest)
                    {
                        releaseTime = earliest;
                    }

                    noteOnTime[data1] = null;
                }

                events.AddEvent(new NoteEvent(ToTicks(tempoMap, releaseTime), midiChannel, MidiCommandCode.NoteOff, data1, 0), trackNumber);
                continue;
            }

            switch (command)
            {
                case 0xA0:
                    events.AddEvent(new NoteEvent(ToTicks(tempoMap, time), midiChannel, MidiCommandCode.KeyAfterTouch, data1, data2), trackNumber);
                    break;
                case 0xB0:
                    events.AddEvent(new ControlChangeEvent(ToTicks(tempoMap, time), midiChannel, (MidiController)data1, data2), trackNumber);
                    break;
                case 0xC0:
                    events.AddEvent(new PatchChangeEvent(ToTicks(tempoMap, time), midiChannel, data1), trackNumber);
                    break;
                case 0xD0:
                    events.AddEvent(new ChannelAfterTouchEvent(ToTicks(tempoMap, time), midiChannel, data1), trackNumber);
                    break;
                case 0xE0:
                    events.AddEvent(new PitchWheelChangeEvent(ToTicks(tempoMap, time), midiChannel, data1 | (data2 << 7)), trackNumber);
                    break;
            }
        }

        // Any note still sounding at the end of the sequence - which is every note on a track with
        // IgnoreNoteOff set - gets a release, so the file does not hang.
        for (var note = 0; note < noteOnTime.Length; note++)
        {
            if (!noteOnTime[note].HasValue)
            {
                continue;
            }

            var releaseTime = noteOnTime[note].Value + hold;
            events.AddEvent(new NoteEvent(ToTicks(tempoMap, releaseTime), midiChannel, MidiCommandCode.NoteOff, note, 0), trackNumber);
        }
    }

    private static long ToTicks(MidiTempoMap tempoMap, TimeSpan time)
    {
        var tick = (long)Math.Round(tempoMap.BeatPositionAt(time) * TicksPerQuarterNote);
        return tick < 0 ? 0 : tick;
    }
}

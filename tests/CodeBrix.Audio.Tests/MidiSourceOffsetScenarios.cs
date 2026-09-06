using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// <see cref="PlayerTrack.MidiSourceOffset"/>: the lever that moves a track's synthesized rendition
/// against its own recording, which is what an alignment measurement produces. Everything here is an
/// offline render, so no audio device is involved.
/// </summary>
public class MidiSourceOffsetScenarios
{
    private const int SampleRate = MultiTrackTestSong.SampleRate;

    [Fact]
    public void the_midi_source_offset_delays_the_rendition_and_leaves_the_recording_alone()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var path = song.WriteConstantWav("stem.wav", 0.5f, 0.5f, SampleRate);
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(path, "Stem"));
        track.SetMidiSource(
            MultiTrackTestSong.BuildSingleNoteSequence(60, noteTicks: 400, totalTicks: 1000),
            MultiTrackTestSong.SoundFontFactory());
        track.MidiSourceOffset = TimeSpan.FromMilliseconds(250);

        //Act
        var onAudio = player.Render(SampleRate);
        track.ActiveSource = TrackSource.Midi;
        var onMidi = player.Render(SampleRate);

        //Assert - the recording is at full level from the first frame either way; the rendition
        //         is silent for the first quarter second and audible after it
        Math.Abs(onAudio[0]).Should().BeApproximately(0.5f, 1e-6f);
        Energy(onMidi, 0, SampleRate / 8).Should().Be(0.0);
        Energy(onMidi, SampleRate / 2, SampleRate / 8).Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void a_negative_midi_source_offset_pulls_the_rendition_earlier()
    {
        //Arrange - one note, written a quarter of a second in
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new MidiTrack(NoteAt(500), MultiTrackTestSong.SoundFontFactory(), "Part"));
        var asWritten = player.Render(SampleRate);

        //Act
        track.MidiSourceOffset = TimeSpan.FromMilliseconds(-250);
        var pulledEarlier = player.Render(SampleRate);

        //Assert - silent until the note as written, sounding from the first frame once pulled back
        Energy(asWritten, 0, SampleRate / 8).Should().Be(0.0);
        Energy(pulledEarlier, 0, SampleRate / 8).Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_midi_source_offset_is_counted_in_the_tracks_duration()
    {
        //Arrange
        using var player = new MultiTrackPlayer();
        var track = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(60, noteTicks: 400, totalTicks: 1000),
            MultiTrackTestSong.SoundFontFactory(),
            "Part"));
        var before = track.Duration;

        //Act
        track.MidiSourceOffset = TimeSpan.FromMilliseconds(500);

        //Assert
        track.Duration.Should().Be(before + TimeSpan.FromMilliseconds(500));
        track.MidiDuration.Should().Be(before + TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void the_merged_export_writes_the_midi_source_offset_into_the_note_times()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var track = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 400, stepTicks: 500),
            MultiTrackTestSong.SoundFontFactory(),
            "Part"));
        track.MidiSourceOffset = TimeSpan.FromMilliseconds(500);
        var path = Path.Combine(song.Directory, "merged.mid");

        //Act
        player.ExportMergedMidi(path);
        var reloaded = new MidiFile(path);

        //Assert - 500 ms at 120 BPM is one beat, which is 480 ticks at the export's resolution
        var noteOn = Enumerable.Range(0, reloaded.Tracks)
            .SelectMany(index => reloaded.Events[index])
            .Where(MidiEvent.IsNoteOn)
            .Cast<NoteEvent>()
            .Single();
        noteOn.AbsoluteTime.Should().BeInRange(475, 485);
    }

    // One note, struck at the given tick and released 400 ticks later, in a sequence that runs on
    // for two beats afterwards. At 120 BPM and 1000 ticks per quarter, a tick is half a millisecond.
    private static CodeBrix.Audio.Synth.MidiSequence NoteAt(int tick)
    {
        var events = new MidiEventCollection(1, 1000);
        events.AddEvent(new NoteEvent(tick, 1, MidiCommandCode.NoteOn, 60, 100), 1);
        events.AddEvent(new NoteEvent(tick + 400, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        events.AddEvent(new ControlChangeEvent(tick + 2000, 1, MidiController.Expression, 127), 1);
        events.PrepareForExport();
        return CodeBrix.Audio.Synth.MidiSequence.FromEvents(events);
    }

    private static double Energy(float[] samples, int fromFrame, int frames)
    {
        var total = 0.0;
        var to = Math.Min(samples.Length, (fromFrame + frames) * 2);
        for (var i = fromFrame * 2; i < to; i++) { total += Math.Abs(samples[i]); }
        return total;
    }
}

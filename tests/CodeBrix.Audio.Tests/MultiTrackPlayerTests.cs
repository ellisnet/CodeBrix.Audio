using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Covers <see cref="MultiTrackPlayer"/>. Almost everything here goes through the OFFLINE render,
/// which is the same mixing code the speakers hear and needs no audio device; the one audible test
/// is opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1. Shares the non-parallel "SharedAudioOutput"
/// collection with the other player tests.
/// </summary>
[Collection("SharedAudioOutput")]
public sealed class MultiTrackPlayerTests : IDisposable
{
    private const int Rate = MultiTrackTestSong.SampleRate;

    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public MultiTrackPlayerTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- the empty player -----

    [Fact]
    public void a_new_player_has_no_tracks_and_no_length()
    {
        //Arrange & Act
        using var player = new MultiTrackPlayer();

        //Assert
        player.Tracks.Should().BeEmpty();
        player.Duration.Should().Be(TimeSpan.Zero);
        player.Position.Should().Be(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.IsPrepared.Should().BeFalse();
        player.Volume.Should().Be(1.0f);
        player.IsLooping.Should().BeFalse();
        player.AutoSetRelativeTrackLevels.Should().BeFalse();
        player.LevelMeasurement.Should().NotBeNull();
        player.LevelMeasurement.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task awaiting_the_level_measurement_of_a_fresh_player_returns_at_once()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        await player.LevelMeasurement;

        //Assert
        player.LevelMeasurement.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void preparing_a_player_with_no_tracks_throws()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var act = () => player.Prepare();

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void seeking_before_the_song_is_prepared_throws()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var act = () => player.Seek(TimeSpan.FromSeconds(1));

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void rendering_a_player_with_no_tracks_gives_nothing()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Should().BeEmpty();
    }

    [Fact]
    public void dispose_is_idempotent()
    {
        //Arrange
        var player = new MultiTrackPlayer();

        //Act
        var act = () => { player.Dispose(); player.Dispose(); };

        //Assert
        act.Should().NotThrow();
    }

    // ----- the track collection -----

    [Fact]
    public void tracks_are_reported_in_the_order_they_were_added()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var first = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));
        var second = player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410)));

        //Act
        var tracks = player.Tracks;

        //Assert
        tracks.Should().HaveCount(2);
        tracks[0].Should().BeSameAs(first);
        tracks[1].Should().BeSameAs(second);
    }

    [Fact]
    public void removing_and_clearing_tracks_changes_the_song()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));

        //Act
        var removed = player.Remove(track);
        var removedAgain = player.Remove(track);
        player.ClearTracks();

        //Assert
        removed.Should().BeTrue();
        removedAgain.Should().BeFalse();
        player.Tracks.Should().BeEmpty();
    }

    [Fact]
    public void adding_a_null_track_throws()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var act = () => player.Add<PlayerTrack>(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- finding a track by name -----

    [Fact]
    public void the_indexer_finds_a_track_by_name_ignoring_case_and_surrounding_space()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var drums = player.Add(new AudioTrack(song.WriteConstantWav("d.wav", 0.5f, 0.5f, 4410), "Drums"));
        player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410), "Bass"));

        //Act
        var found = player["  drums "];

        //Assert
        found.Should().BeSameAs(drums);
        player["DRUMS"].Should().BeSameAs(drums);
    }

    [Fact]
    public void the_indexer_throws_naming_the_tracks_the_song_does_have()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("d.wav", 0.5f, 0.5f, 4410), "Drums"));
        player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410), "Bass"));

        //Act
        var act = () => player["Strings"];

        //Assert
        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*Strings*")
            .WithMessage("*'Drums'*")
            .WithMessage("*'Bass'*");
    }

    [Fact]
    public void the_indexer_says_so_when_the_song_has_no_tracks_at_all()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var act = () => player["Drums"];

        //Assert
        act.Should().Throw<KeyNotFoundException>().WithMessage("*no tracks*");
    }

    [Fact]
    public void the_indexer_rejects_a_null_name()
    {
        //Arrange
        using var player = new MultiTrackPlayer();

        //Act
        var act = () => player[null];

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void two_tracks_of_the_same_name_resolve_to_the_first_one_added()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var first = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410), "Guitar"));
        var second = player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410), "guitar"));

        //Act
        var found = player["Guitar"];

        //Assert
        found.Should().BeSameAs(first);
        found.Should().NotBeSameAs(second);
    }

    [Fact]
    public void find_track_returns_null_for_a_name_the_song_does_not_have()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var drums = player.Add(new AudioTrack(song.WriteConstantWav("d.wav", 0.5f, 0.5f, 4410), "Drums"));

        //Act
        var missing = player.FindTrack("Strings");

        //Assert
        missing.Should().BeNull();
        player.FindTrack(null).Should().BeNull();
        player.FindTrack("drums").Should().BeSameAs(drums);
    }

    [Fact]
    public void try_get_track_reports_whether_the_song_has_that_name()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var bass = player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410), "Bass"));

        //Act
        var hit = player.TryGetTrack("BASS", out var found);
        var miss = player.TryGetTrack("Strings", out var absent);

        //Assert
        hit.Should().BeTrue();
        found.Should().BeSameAs(bass);
        miss.Should().BeFalse();
        absent.Should().BeNull();
    }

    // ----- duration and offsets -----

    [Fact]
    public void the_song_is_as_long_as_its_longest_track()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("short.wav", 0.5f, 0.5f, 4410)));
        player.Add(new AudioTrack(song.WriteConstantWav("long.wav", 0.5f, 0.5f, 22050)));

        //Act
        var duration = player.Duration;

        //Assert
        duration.TotalSeconds.Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void a_positive_offset_lengthens_the_song_by_the_delay()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));
        var late = player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410)));

        //Act
        late.Offset = TimeSpan.FromSeconds(0.25);

        //Assert
        player.Duration.TotalSeconds.Should().BeApproximately(0.35, 1e-6);
    }

    [Fact]
    public void a_positive_offset_delays_the_track_in_the_render()
    {
        //Arrange - 0.05 s of delay is 2205 frames at 44.1 kHz.
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));
        track.Offset = TimeSpan.FromSeconds(0.05);

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Length.Should().Be((2205 + 4410) * 2);
        samples[0].Should().Be(0.0f);
        samples[2204 * 2].Should().Be(0.0f);
        samples[2205 * 2].Should().Be(0.5f);
        samples[2205 * 2 + 1].Should().Be(0.25f);
        samples[^2].Should().Be(0.5f);
    }

    [Fact]
    public void a_negative_offset_pulls_the_track_earlier_and_drops_what_falls_off_the_front()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteSineWav("tone.wav", 200, 4410)));
        track.Offset = TimeSpan.FromSeconds(-0.02);

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);
        var reference = RenderAlone(song.WriteSineWav("tone2.wav", 200, 4410));

        //Assert - the song now starts 882 frames into the tone.
        samples.Length.Should().Be((4410 - 882) * 2);
        samples[0].Should().BeApproximately(reference[882 * 2], 1e-6f);
        samples[100 * 2].Should().BeApproximately(reference[(882 + 100) * 2], 1e-6f);
    }

    // ----- per-track mixing arithmetic -----

    [Fact]
    public void a_single_track_renders_at_unity_gain_untouched()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Length.Should().Be(4410 * 2);
        samples[0].Should().Be(0.5f);
        samples[1].Should().Be(0.25f);
        samples[^2].Should().Be(0.5f);
        samples[^1].Should().Be(0.25f);
    }

    [Fact]
    public void gain_scales_a_track_linearly()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));

        //Act
        track.Gain = 0.5f;
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples[0].Should().Be(0.25f);
        samples[1].Should().Be(0.125f);
    }

    [Fact]
    public void mute_silences_a_track_without_shortening_the_song()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));

        //Act
        track.Mute = true;
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Length.Should().Be(4410 * 2);
        samples.Should().OnlyContain(sample => sample == 0.0f);
    }

    [Fact]
    public void solo_on_one_track_silences_every_other_track()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));
        var soloed = player.Add(new AudioTrack(song.WriteConstantWav("b.wav", 0.125f, 0.125f, 4410)));

        //Act
        soloed.Solo = true;
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples[0].Should().Be(0.125f);
        samples[1].Should().Be(0.125f);
    }

    [Fact]
    public void mute_wins_over_solo_on_the_same_track()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));

        //Act
        track.Solo = true;
        track.Mute = true;
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Should().OnlyContain(sample => sample == 0.0f);
    }

    [Theory]
    [InlineData(-1.0f, 0.5f, 0.0f)]
    [InlineData(1.0f, 0.0f, 0.25f)]
    [InlineData(0.0f, 0.5f, 0.25f)]
    [InlineData(0.5f, 0.25f, 0.25f)]
    public void pan_attenuates_the_far_channel_and_leaves_the_near_one_alone(float pan, float left, float right)
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));

        //Act
        track.Pan = pan;
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples[0].Should().BeApproximately(left, 1e-6f);
        samples[1].Should().BeApproximately(right, 1e-6f);
    }

    // ----- lockstep -----

    [Fact]
    public void the_mix_is_the_sum_of_the_tracks_rendered_on_their_own()
    {
        //Arrange - two tones of different lengths, so the sum is not a trivial one.
        using var song = MultiTrackTestSong.Create();
        var first = song.WriteSineWav("one.wav", 220, 8000);
        var second = song.WriteSineWav("two.wav", 331, 5000);

        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(first));
        player.Add(new AudioTrack(second));

        //Act
        var mixed = player.Render(Rate, TimeSpan.Zero);
        var alone1 = RenderAlone(first);
        var alone2 = RenderAlone(second);

        //Assert
        mixed.Length.Should().Be(8000 * 2);
        for (var i = 0; i < mixed.Length; i++)
        {
            var expected = alone1[i] + (i < alone2.Length ? alone2[i] : 0f);
            mixed[i].Should().BeApproximately(expected, 1e-6f);
        }
    }

    [Fact]
    public void a_midi_track_mixes_in_lockstep_with_an_audio_track()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var audioPath = song.WriteSineWav("tone.wav", 220, 40000);
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74, 76], noteTicks: 400, stepTicks: 500);

        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(audioPath));
        player.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        using var audioOnly = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        audioOnly.Add(new AudioTrack(audioPath));

        using var midiOnly = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        midiOnly.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        //Act
        var mixed = player.Render(Rate, TimeSpan.Zero);
        var audio = audioOnly.Render(Rate, TimeSpan.Zero);
        var midi = midiOnly.Render(Rate, TimeSpan.Zero);

        //Assert
        var shared = Math.Min(mixed.Length, Math.Min(audio.Length, midi.Length));
        shared.Should().BeGreaterThan(0);
        for (var i = 0; i < shared; i++)
        {
            mixed[i].Should().BeApproximately(audio[i] + midi[i], 1e-5f);
        }
    }

    // ----- the percussion rule -----

    [Fact]
    public void a_zero_length_note_still_sounds_for_the_minimum_hold()
    {
        //Arrange - the note-off lands two ticks after the note-on: half a millisecond of note.
        var sequence = MultiTrackTestSong.BuildSingleNoteSequence(72, noteTicks: 2, totalTicks: 1000);

        using var held = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        held.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        using var literal = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var literalTrack = literal.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));
        literalTrack.MinimumNoteHold = TimeSpan.Zero;

        //Act
        var withHold = held.Render(Rate, TimeSpan.Zero);
        var withoutHold = literal.Render(Rate, TimeSpan.Zero);

        //Assert - measured over the window from 40 ms to 55 ms, which the 60 ms hold covers and
        // the two-tick note does not.
        var heldEnergy = Rms(withHold, MillisecondsToFrames(40), MillisecondsToFrames(55));
        var literalEnergy = Rms(withoutHold, MillisecondsToFrames(40), MillisecondsToFrames(55));

        heldEnergy.Should().BeGreaterThan(1e-3);
        heldEnergy.Should().BeGreaterThan(literalEnergy * 10);
    }

    [Fact]
    public void the_minimum_hold_does_not_cut_a_note_that_is_written_longer_than_it()
    {
        //Arrange - a half-second note against a 60 ms minimum hold.
        var sequence = MultiTrackTestSong.BuildSingleNoteSequence(72, noteTicks: 1000, totalTicks: 1400);

        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert - still sounding well past the 60 ms the hold would have released it at.
        var early = Rms(samples, MillisecondsToFrames(20), MillisecondsToFrames(50));
        var late = Rms(samples, MillisecondsToFrames(300), MillisecondsToFrames(450));

        early.Should().BeGreaterThan(1e-3);
        late.Should().BeGreaterThan(early * 0.25);
    }

    [Fact]
    public void ignore_note_off_leaves_the_note_ringing_past_its_written_end()
    {
        //Arrange
        var sequence = MultiTrackTestSong.BuildSingleNoteSequence(72, noteTicks: 2, totalTicks: 1400);

        using var ringing = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var ringingTrack = ringing.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));
        ringingTrack.IgnoreNoteOff = true;

        using var literal = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var literalTrack = literal.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));
        literalTrack.MinimumNoteHold = TimeSpan.Zero;

        //Act
        var ringingSamples = ringing.Render(Rate, TimeSpan.Zero);
        var literalSamples = literal.Render(Rate, TimeSpan.Zero);

        //Assert - half a second in, one is still sounding and the other stopped long ago.
        var ringingEnergy = Rms(ringingSamples, MillisecondsToFrames(400), MillisecondsToFrames(600));
        var literalEnergy = Rms(literalSamples, MillisecondsToFrames(400), MillisecondsToFrames(600));

        ringingEnergy.Should().BeGreaterThan(1e-3);
        ringingEnergy.Should().BeGreaterThan(literalEnergy * 10);
    }

    // ----- automatic level matching -----

    [Fact]
    public void level_matching_left_off_never_touches_a_gain()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));
        track.SetMidiSource(
            MultiTrackTestSong.BuildNoteSequence([72], noteTicks: 200, stepTicks: 400),
            MultiTrackTestSong.SoundFontFactory());
        track.MidiSourceGain = 0.75f;

        //Act
        player.Render(Rate, TimeSpan.Zero);

        //Assert
        player.AutoSetRelativeTrackLevels.Should().BeFalse();
        player.LevelMeasurement.IsCompleted.Should().BeTrue();
        track.Gain.Should().Be(1.0f);
        track.MidiSourceGain.Should().Be(0.75f);
    }

    [Fact]
    public void level_matching_reproduces_the_planted_loudness_ratio()
    {
        //Arrange - the recording is the track's OWN midi rendition, amplified by a known factor, so
        // the ratio the measurement has to find is exactly that factor.
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 400, stepTicks: 500);

        var loudPath = WriteRenderedMidiAsWav(song, "loud.wav", sequence, amplify: 3.0f);
        var quietPath = WriteRenderedMidiAsWav(song, "quiet.wav", sequence, amplify: 0.25f);

        using var player = new MultiTrackPlayer();
        var loud = player.Add(new AudioTrack(loudPath));
        loud.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());
        var quiet = player.Add(new AudioTrack(quietPath));
        quiet.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        //Act
        player.MeasureRelativeTrackLevels();

        //Assert
        loud.MidiSourceGain.Should().BeApproximately(3.0f, 0.05f);
        quiet.MidiSourceGain.Should().BeApproximately(0.25f, 0.01f);
        loud.Gain.Should().Be(1.0f);
        quiet.Gain.Should().Be(1.0f);
    }

    [Fact]
    public void the_synchronous_measurement_leaves_a_completed_level_measurement_behind()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 400, stepTicks: 500);
        var loudPath = WriteRenderedMidiAsWav(song, "loud.wav", sequence, amplify: 3.0f);

        using var player = new MultiTrackPlayer();
        var loud = player.Add(new AudioTrack(loudPath));
        loud.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        //Act
        player.MeasureRelativeTrackLevels();

        //Assert
        player.LevelMeasurement.Should().NotBeNull();
        player.LevelMeasurement.IsCompletedSuccessfully.Should().BeTrue();
        loud.MidiSourceGain.Should().BeApproximately(3.0f, 0.05f);
    }

    [Fact]
    public async Task the_asynchronous_measurement_is_the_level_measurement_task()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 400, stepTicks: 500);
        var quietPath = WriteRenderedMidiAsWav(song, "quiet.wav", sequence, amplify: 0.25f);

        using var player = new MultiTrackPlayer();
        var quiet = player.Add(new AudioTrack(quietPath));
        quiet.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        //Act
        var measurement = player.MeasureRelativeTrackLevelsAsync();
        var reported = player.LevelMeasurement;
        await measurement;

        //Assert
        reported.Should().BeSameAs(measurement);
        player.LevelMeasurement.IsCompletedSuccessfully.Should().BeTrue();
        quiet.MidiSourceGain.Should().BeApproximately(0.25f, 0.01f);
    }

    [Fact]
    public void level_matching_leaves_a_track_that_has_only_one_source_alone()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var audioOnly = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));
        var midiOnly = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([72], noteTicks: 200, stepTicks: 400),
            MultiTrackTestSong.SoundFontFactory()));

        //Act
        player.MeasureRelativeTrackLevels();

        //Assert
        audioOnly.MidiSourceGain.Should().Be(1.0f);
        midiOnly.MidiSourceGain.Should().Be(1.0f);
    }

    // ----- offline rendering -----

    [Fact]
    public void render_to_wav_writes_a_stereo_float_file_of_the_right_length()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));
        var output = Path.Combine(song.Directory, "mix.wav");

        //Act
        player.RenderToWav(output, Rate, TimeSpan.Zero);

        //Assert
        using var reader = new WaveFileReader(output);
        reader.WaveFormat.Channels.Should().Be(2);
        reader.WaveFormat.SampleRate.Should().Be(Rate);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        reader.TotalTime.TotalSeconds.Should().BeApproximately(0.1, 1e-6);
    }

    [Fact]
    public void the_tail_extends_the_render_without_changing_the_songs_length()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));

        //Act
        var withTail = player.Render(Rate, TimeSpan.FromSeconds(0.2));

        //Assert
        player.Duration.TotalSeconds.Should().BeApproximately(0.1, 1e-6);
        withTail.Length.Should().Be((4410 + 8820) * 2);
        withTail[^1].Should().Be(0.0f);
    }

    [Fact]
    public void rendering_at_another_rate_converts_the_source()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 4410)));

        //Act
        var samples = player.Render(48000, TimeSpan.Zero);

        //Assert - a tenth of a second at 48 kHz, and the constant survives the conversion.
        (samples.Length / 2).Should().BeCloseTo(4800, 4);
        samples[1000].Should().BeApproximately(0.5f, 1e-5f);
        samples[1001].Should().BeApproximately(0.25f, 1e-5f);
    }

    [Fact]
    public void a_compressed_source_mixes_like_any_other_track()
    {
        //Arrange - an Ogg Vorbis stem beside a WAV one, both converted to the mix's rate.
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(TestAssets.Path(TestAssets.VorbisToneStereo)));
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.1f, 0.1f, 4410)));

        //Act
        var samples = player.Render(Rate, TimeSpan.Zero);

        //Assert
        samples.Length.Should().BeGreaterThan(4410 * 2);
        Rms(samples, 0, 4410).Should().BeGreaterThan(0.1);
    }

    // ----- source switching -----

    [Fact]
    public void a_track_renders_whichever_source_is_active()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 400, stepTicks: 500);
        var audioPath = song.WriteConstantWav("a.wav", 0.5f, 0.25f, 44100);

        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(audioPath));
        track.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        using var midiOnly = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        midiOnly.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        //Act
        var asAudio = player.Render(Rate, TimeSpan.Zero);
        track.ActiveSource = TrackSource.Midi;
        var asMidi = player.Render(Rate, TimeSpan.Zero);
        var reference = midiOnly.Render(Rate, TimeSpan.Zero);

        //Assert
        asAudio[20000].Should().Be(0.5f);
        for (var i = 0; i < reference.Length; i++)
        {
            asMidi[i].Should().BeApproximately(reference[i], 1e-5f);
        }
    }

    [Fact]
    public void the_midi_source_gain_applies_only_while_the_midi_source_is_heard()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72], noteTicks: 400, stepTicks: 500);

        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        var track = player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.25f, 44100)));
        track.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());
        track.MidiSourceGain = 0.5f;

        using var reference = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        reference.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));

        //Act
        var asAudio = player.Render(Rate, TimeSpan.Zero);
        track.ActiveSource = TrackSource.Midi;
        var asMidi = player.Render(Rate, TimeSpan.Zero);
        var plain = reference.Render(Rate, TimeSpan.Zero);

        //Assert
        asAudio[20000].Should().Be(0.5f);
        for (var i = 0; i < plain.Length; i += 97)
        {
            asMidi[i].Should().BeApproximately(plain[i] * 0.5f, 1e-5f);
        }
    }

    // ----- merged General MIDI export -----

    [Fact]
    public void the_merged_export_puts_each_track_on_its_own_channel_with_percussion_on_ten()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();

        var bass = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([40, 43], noteTicks: 400, stepTicks: 500),
            MultiTrackTestSong.SoundFontFactory(),
            "Bass"));
        bass.GmProgram = 32;

        var drums = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([36, 38], noteTicks: 2, stepTicks: 500),
            MultiTrackTestSong.SoundFontFactory(),
            "Drums"));
        drums.IsPercussion = true;
        drums.GmProgram = 118;

        var path = Path.Combine(song.Directory, "merged.mid");

        //Act
        var problems = player.ExportMergedMidi(path);
        var reloaded = new MidiFile(path, strictChecking: false);

        //Assert
        problems.Should().BeEmpty();
        reloaded.FileFormat.Should().Be(1);
        reloaded.DeltaTicksPerQuarterNote.Should().Be(480);

        var all = Enumerable.Range(0, reloaded.Tracks).SelectMany(t => reloaded.Events[t]).ToList();
        var noteOns = all.Where(MidiEvent.IsNoteOn).Cast<NoteEvent>().ToList();

        noteOns.Where(n => n.NoteNumber is 40 or 43).Should().OnlyContain(n => n.Channel == 1);
        noteOns.Where(n => n.NoteNumber is 36 or 38).Should().OnlyContain(n => n.Channel == 10);

        var patches = all.OfType<PatchChangeEvent>().ToList();
        patches.Should().Contain(p => p.Channel == 1 && p.Patch == 32);
        patches.Should().Contain(p => p.Channel == 10 && p.Patch == 118);

        all.OfType<TextEvent>()
            .Where(t => t.MetaEventType == MetaEventType.SequenceTrackName)
            .Select(t => t.Text)
            .Should().Contain(["Bass", "Drums"]);
    }

    [Fact]
    public void the_merged_export_carries_the_tempo_map_and_reloads_as_a_playable_sequence()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildTempoSequence([(0, 100.0), (960, 150.0)], lengthTicks: 1920),
            MultiTrackTestSong.SoundFontFactory(),
            "Lead"));

        var path = Path.Combine(song.Directory, "merged.mid");

        //Act
        player.ExportMergedMidi(path);
        var sequence = new MidiSequence(path);

        //Assert
        sequence.TempoMap.Changes.Should().HaveCount(2);
        sequence.TempoMap.Changes[0].BeatsPerMinute.Should().BeApproximately(100.0, 0.05);
        sequence.TempoMap.Changes[1].BeatsPerMinute.Should().BeApproximately(150.0, 0.05);
        sequence.TempoMap.Changes[1].BeatPosition.Should().BeApproximately(2.0, 1e-3);
    }

    [Fact]
    public void the_merged_export_gives_a_zero_length_drum_note_the_minimum_hold()
    {
        //Arrange - notes written two ticks long at 1000 ppq, which is one millisecond.
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        var drums = player.Add(new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([36], noteTicks: 2, stepTicks: 1000),
            MultiTrackTestSong.SoundFontFactory(),
            "Drums"));
        drums.IsPercussion = true;

        var path = Path.Combine(song.Directory, "merged.mid");

        //Act
        player.ExportMergedMidi(path);
        var reloaded = new MidiFile(path, strictChecking: false);

        //Assert - 60 ms at 120 BPM is 0.12 of a beat, which at 480 ppq is 58 ticks.
        var all = Enumerable.Range(0, reloaded.Tracks).SelectMany(t => reloaded.Events[t]).ToList();
        var on = all.Where(MidiEvent.IsNoteOn).Cast<NoteEvent>().Single(n => n.NoteNumber == 36);
        var off = all.Where(MidiEvent.IsNoteOff).Cast<NoteEvent>().Single(n => n.NoteNumber == 36);

        (off.AbsoluteTime - on.AbsoluteTime).Should().BeCloseTo(58, 2);
    }

    [Fact]
    public void the_merged_export_of_a_song_with_no_midi_reports_the_problem()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));

        //Act
        var events = player.BuildMergedMidi(out var problems);

        //Assert
        problems.Should().HaveCount(1);
        events.Should().NotBeNull();
    }

    // ----- musical time -----

    [Fact]
    public void a_song_with_no_midi_reports_the_default_tempo()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410)));

        //Act & Assert
        player.TempoSource.BeatsPerMinute.Should().Be(120.0);
        player.TempoSource.IsPlaying.Should().BeFalse();
    }

    // ----- audible (device) -----

    [Fact]
    public void plays_the_close_encounters_motif_as_two_tracks_in_unison()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - the same five notes twice over: once as a recorded WAV stem, once as a MIDI stem
        // through the synthetic SoundFont, started together so the two are one statement of the
        // tune rather than a round.
        using var scope = new AudibleTestScope();
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Add(new AudioTrack(song.WriteCloseEncountersWav("motif.wav"), "Recording"));
        player.Add(new MidiTrack(BuildMotifSequence(), MultiTrackTestSong.SoundFontFactory(), "Synth"));
        player.Volume = 0.7f;

        //Act
        player.Play();
        var fired = ended.Wait(player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.TempoSource.BeatsPerMinute.Should().Be(120.0);
    }

    [Fact]
    public async Task auto_level_matching_writes_the_gains_by_the_time_its_task_completes()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - Prepare is what starts the automatic measurement, and Prepare opens the device.
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 400, stepTicks: 500);
        var loudPath = WriteRenderedMidiAsWav(song, "auto.wav", sequence, amplify: 3.0f);

        using var player = new MultiTrackPlayer { AutoSetRelativeTrackLevels = true };
        var loud = player.Add(new AudioTrack(loudPath));
        loud.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        //Act - nothing is ever played; the device is only opened.
        player.Prepare();
        var started = player.LevelMeasurement;
        await started;

        //Assert
        started.Should().NotBeNull();
        player.LevelMeasurement.IsCompletedSuccessfully.Should().BeTrue();
        loud.MidiSourceGain.Should().BeApproximately(3.0f, 0.05f);
    }

    // ----- helpers -----

    private static float[] RenderAlone(string path)
    {
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new AudioTrack(path));
        return player.Render(Rate, TimeSpan.Zero);
    }

    private static string WriteRenderedMidiAsWav(MultiTrackTestSong song, string name, MidiSequence sequence,
        float amplify)
    {
        using var player = new MultiTrackPlayer { Tail = TimeSpan.Zero };
        player.Add(new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory()));
        var samples = player.Render(MultiTrackPlayer.LevelMeasurementSampleRate, TimeSpan.Zero);

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= amplify;
        }

        var path = Path.Combine(song.Directory, name);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(MultiTrackPlayer.LevelMeasurementSampleRate, 2);
        using (var writer = new WaveFileWriter(path, format))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    private static int MillisecondsToFrames(double milliseconds) => (int)(milliseconds * Rate / 1000.0);

    private static double Rms(float[] interleaved, int startFrame, int endFrame)
    {
        var start = Math.Max(0, startFrame * 2);
        var end = Math.Min(interleaved.Length, endFrame * 2);
        if (end <= start)
        {
            return 0.0;
        }

        var sum = 0.0;
        for (var i = start; i < end; i++)
        {
            sum += (double)interleaved[i] * interleaved[i];
        }

        return Math.Sqrt(sum / (end - start));
    }

    /// <summary>
    /// The five-tone "Close Encounters" motif as a MIDI sequence: G5, A5, F5, F4, C5 - the same
    /// tune, at the same timings, as the WAV stem it plays alongside.
    /// </summary>
    private static MidiSequence BuildMotifSequence() =>
        MultiTrackTestSong.BuildNoteSequence([79, 81, 77, 65, 72], noteTicks: 600, stepTicks: 720);
}

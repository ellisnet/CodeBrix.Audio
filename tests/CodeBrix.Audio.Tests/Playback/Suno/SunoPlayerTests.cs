using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Maps the synthetic export onto a <see cref="MultiTrackPlayer"/> and checks what came out. No
/// audio device is opened anywhere here: everything is either a property of an unprepared player or
/// an offline render.
/// </summary>
[Collection("SunoStems")]
public class SunoPlayerTests
{
    private static readonly TimeSpan NoTail = TimeSpan.Zero;

    // The fixture's WAVs are 8 kHz, which is below the lowest rate a SoundFont synthesizer will
    // render at, so the offline renders here run at a rate both sources can meet.
    private const int RenderRate = 22050;

    // ---------------------------------------------------------------------------------------
    // The shape of the player
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void a_loaded_song_becomes_one_track_per_stem_in_stem_order()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer();

        //Assert
        player.Tracks.Select(track => track.Name).Should()
            .Equal(song.Stems.Select(stem => stem.Name));
    }

    [Fact]
    public void the_default_mix_is_every_stems_recording()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(Instruments());

        //Assert
        player.Tracks.Should().OnlyContain(track => track.ActiveSource == TrackSource.Audio);
        player.Tracks.Should().OnlyContain(track => track.HasAudioSource);
    }

    [Fact]
    public void a_stem_with_midi_carries_it_as_a_second_source_when_there_is_an_instrument()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(Instruments());

        //Assert - FX is the one stem of the fixture with no MIDI file
        player.Tracks.Where(track => track.Name != "FX").Should().OnlyContain(track => track.HasMidiSource);
        Track(player, "FX").HasMidiSource.Should().BeFalse();
    }

    [Fact]
    public void no_midi_source_is_attached_when_there_is_no_instrument_to_play_it()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer();

        //Assert
        player.Tracks.Should().OnlyContain(track => !track.HasMidiSource);
    }

    [Fact]
    public void a_midi_only_stem_becomes_a_midi_track_and_is_dropped_when_it_cannot_be_played()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary, new FakeSongOptions { IncludeMidiOnlyStem = true });

        //Act
        using var withInstruments = song.CreatePlayer(Instruments());
        using var without = song.CreatePlayer();

        //Assert
        Track(withInstruments, "Brass").Should().BeOfType<MidiTrack>();
        Track(withInstruments, "Brass").ActiveSource.Should().Be(TrackSource.Midi);
        without.Tracks.Select(track => track.Name).Should().NotContain("Brass");
    }

    [Fact]
    public void the_stems_program_percussion_rule_and_hold_reach_the_track()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(Instruments());

        //Assert
        var drums = Track(player, "Drums");
        drums.GmProgram.Should().Be(118);
        drums.IsPercussion.Should().BeTrue();
        drums.MinimumNoteHold.Should().Be(song.Options.MinimumNoteHold);
        drums.IgnoreNoteOff.Should().BeFalse();
        Track(player, "Bass").IsPercussion.Should().BeFalse();
    }

    [Fact]
    public void ignoring_note_offs_is_applied_to_percussion_only()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentFactory = Instruments(),
            IgnoreNoteOffOnPercussion = true,
        });

        //Assert
        Track(player, "Drums").IgnoreNoteOff.Should().BeTrue();
        Track(player, "Bass").IgnoreNoteOff.Should().BeFalse();
    }

    [Fact]
    public void each_tracks_midi_source_offset_comes_from_the_stems_alignment()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var applied = song.CreatePlayer(Instruments());
        using var ignored = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentFactory = Instruments(),
            ApplyAlignmentOffsets = false,
        });

        //Assert - the recording never moves; only the transcription does
        Track(applied, "Bass").MidiSourceOffset.Should().Be(song["Bass"].AlignmentOffset);
        Track(applied, "Bass").MidiSourceOffset.Should().NotBe(TimeSpan.Zero);
        Track(applied, "Bass").Offset.Should().Be(TimeSpan.Zero);
        Track(ignored, "Bass").MidiSourceOffset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void the_level_matching_option_is_passed_through()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var on = song.CreatePlayer(new SunoPlayerOptions { AutoSetRelativeTrackLevels = true });
        using var off = song.CreatePlayer();

        //Assert
        on.AutoSetRelativeTrackLevels.Should().BeTrue();
        off.AutoSetRelativeTrackLevels.Should().BeFalse();
    }

    [Fact]
    public void the_instrument_factory_is_asked_for_the_stem_and_the_render_rate()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var asked = new List<(string Stem, int Rate)>();

        using var player = song.CreatePlayer((stem, rate) =>
        {
            lock (asked) { asked.Add((stem.Name, rate)); }
            return new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, rate);
        });

        //Act - an offline render is what builds the synthesizers
        player.Render(22050, NoTail);

        //Assert
        asked.Should().OnlyContain(call => call.Rate == 22050);
        asked.Select(call => call.Stem).Distinct().Should().BeEquivalentTo(
            song.Stems.Where(stem => stem.Midi != null).Select(stem => stem.Name));
    }

    [Fact]
    public void a_soundfont_path_on_the_load_options_is_enough_to_build_instruments()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path), new SunoLoadOptions
        {
            GeneralMidiSoundFontPath = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName),
        });

        //Act
        using var player = song.CreatePlayer();

        //Assert
        Track(player, "Bass").HasMidiSource.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // Playing it
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void a_track_of_a_loaded_song_switches_between_its_recording_and_its_instrument()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        using var player = song.CreatePlayer(Instruments());
        var bass = Track(player, "Bass");

        //Act
        var onAudio = Energy(player.Render(RenderRate, NoTail));
        bass.ActiveSource = TrackSource.Midi;
        var onMidi = Energy(player.Render(RenderRate, NoTail));

        //Assert - a different source is a different sound, and the song is the same length
        bass.ActiveSource.Should().Be(TrackSource.Midi);
        onMidi.Should().NotBeApproximately(onAudio, 1e-6);
        player.Duration.Should().Be(song.Duration + song["Bass"].AlignmentOffset);
    }

    [Fact]
    public void an_offline_render_of_a_loaded_song_is_the_sum_of_its_tracks()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        using var whole = song.CreatePlayer(Instruments());
        foreach (var track in whole.Tracks)
        {
            if (track.HasMidiSource) { track.ActiveSource = TrackSource.Midi; }
        }

        //Act
        var mix = whole.Render(RenderRate, NoTail);
        var sum = new float[mix.Length];
        foreach (var name in song.Stems.Select(stem => stem.Name))
        {
            using var one = song.CreatePlayer(Instruments());
            foreach (var track in one.Tracks)
            {
                track.Mute = track.Name != name;
                if (track.HasMidiSource) { track.ActiveSource = TrackSource.Midi; }
            }

            var part = one.Render(RenderRate, NoTail);
            for (var i = 0; i < Math.Min(sum.Length, part.Length); i++) { sum[i] += part[i]; }
        }

        //Assert
        mix.Length.Should().Be(sum.Length);
        for (var i = 0; i < mix.Length; i++)
        {
            mix[i].Should().BeApproximately(sum[i], 1e-4f);
        }
    }

    [Fact]
    public void the_song_volume_applies_to_an_offline_render()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        using var player = song.CreatePlayer();

        //Act
        var full = player.Render(RenderRate, NoTail);
        player.Volume = 0.25f;
        var quiet = player.Render(RenderRate, NoTail);

        //Assert
        quiet.Length.Should().Be(full.Length);
        for (var i = 0; i < full.Length; i += 97)
        {
            quiet[i].Should().BeApproximately(full[i] * 0.25f, 1e-6f);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The merged General MIDI export
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void a_loaded_song_exports_one_merged_midi_file_that_reads_back()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var path = Path.Combine(temporary.Path, "merged.mid");

        //Act
        var problems = song.ExportMergedMidi(path);
        var reread = new MidiFile(path);

        //Assert - one conductor track plus one per stem that has MIDI
        problems.Should().BeEmpty();
        reread.FileFormat.Should().Be(1);
        reread.Tracks.Should().Be(1 + song.Stems.Count(stem => stem.Midi != null));
        reread.DeltaTicksPerQuarterNote.Should().Be(480);
    }

    [Fact]
    public void the_merged_export_puts_percussion_on_channel_ten_and_everything_else_elsewhere()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var path = Path.Combine(temporary.Path, "merged.mid");

        //Act
        song.ExportMergedMidi(path);
        var reread = new MidiFile(path);

        //Assert
        var channels = new Dictionary<string, int>();
        for (var track = 1; track < reread.Tracks; track++)
        {
            var name = reread.Events[track].OfType<TextEvent>()
                .FirstOrDefault(text => text.MetaEventType == MetaEventType.SequenceTrackName);
            var note = reread.Events[track].OfType<NoteOnEvent>().FirstOrDefault();
            if (name != null && note != null) { channels[name.Text] = note.Channel; }
        }

        channels["Drums"].Should().Be(10);
        channels.Where(pair => pair.Key != "Drums").Should().OnlyContain(pair => pair.Value != 10);
    }

    [Fact]
    public void the_merged_export_needs_no_instrument_and_no_audio()
    {
        //Arrange - a song loaded with nothing configured at all
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var output = new MemoryStream();
        var problems = song.ExportMergedMidi(output, leaveOpen: true);

        //Assert
        problems.Should().BeEmpty();
        output.Length.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------------------------------

    private static SunoSong Load(TemporaryFolder temporary, FakeSongOptions options = null) =>
        SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path, options), new SunoLoadOptions());

    private static Func<SunoStem, int, IMidiSynthesizer> Instruments() =>
        (stem, rate) => new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, rate);

    private static PlayerTrack Track(MultiTrackPlayer player, string name) =>
        player.Tracks.Single(track => track.Name == name);

    private static double Energy(float[] samples)
    {
        var total = 0.0;
        foreach (var sample in samples) { total += Math.Abs(sample); }
        return total;
    }
}

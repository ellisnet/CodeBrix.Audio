using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Playback.Suno;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// What a stem will tell you about itself beyond its files: which notes its transcription holds,
/// and its recording as mono samples.
/// </summary>
[Collection("SunoStems")]
public class SunoStemTests
{
    [Fact]
    public void UsedNotes_are_the_distinct_note_numbers_of_the_transcription_ascending()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        var bass = song["Bass"];

        //Assert - the fixture's bass part cycles three pitches from 36
        bass.UsedNotes.Should().Equal([36, 37, 38]);
        bass.NoteCount.Should().BeGreaterThan(bass.UsedNotes.Count);
    }

    [Fact]
    public void UsedNotes_of_a_percussion_stem_are_its_kit_pieces()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        var drums = song["Drums"];

        //Assert
        drums.IsPercussion.Should().BeTrue();
        drums.UsedNotes.Should().Equal([36, 38]);
    }

    [Fact]
    public void LowestNote_and_HighestNote_bracket_the_used_notes()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        var vocals = song["Vocals"];

        //Assert
        vocals.LowestNote.Should().Be(vocals.UsedNotes.Min());
        vocals.HighestNote.Should().Be(vocals.UsedNotes.Max());
    }

    [Fact]
    public void a_stem_with_no_transcription_holds_no_notes_and_says_so_with_minus_one()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act - FX is the one stem of the fixture with no MIDI file
        var fx = song["FX"];

        //Assert
        fx.HasMidi.Should().BeFalse();
        fx.UsedNotes.Should().BeEmpty();
        fx.LowestNote.Should().Be(-1);
        fx.HighestNote.Should().Be(-1);
    }

    [Fact]
    public void the_coverage_check_the_manual_recommends_is_two_lines_over_UsedNotes()
    {
        //Arrange - a library that plays this program from 60 up, which the bass part is below
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var bass = song["Bass"];
        var coverage = new InstrumentCoverage(
            [new KeyValuePair<int, InstrumentKeyRange>(bass.GmProgram, new InstrumentKeyRange(60, 90))],
            []);

        //Act
        var missing = bass.UsedNotes.Where(note => !coverage.CoversNote(bass.GmProgram, note)).ToArray();

        //Assert
        missing.Should().Equal(bass.UsedNotes);
    }

    [Fact]
    public void ReadMonoAudio_returns_the_recording_at_its_own_rate()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var vocals = song["Vocals"];

        //Act
        var samples = vocals.ReadMonoAudio(out var sampleRate);

        //Assert
        sampleRate.Should().Be(FakeSongStems.SampleRate);
        samples.Length.Should().Be((int)Math.Round(FakeSongStems.SongSeconds * FakeSongStems.SampleRate));
        samples.Any(sample => Math.Abs(sample) > 0.1f).Should().BeTrue();
    }

    [Fact]
    public void ReadMonoAudio_returns_an_empty_array_for_a_stem_with_no_recording()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary, new FakeSongOptions { IncludeMidiOnlyStem = true });

        //Act
        var samples = song["Brass"].ReadMonoAudio(out var sampleRate);

        //Assert
        song["Brass"].HasAudio.Should().BeFalse();
        samples.Should().BeEmpty();
        sampleRate.Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------

    private static SunoSong Load(TemporaryFolder temporary, FakeSongOptions options = null) =>
        SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path, options), new SunoLoadOptions());
}

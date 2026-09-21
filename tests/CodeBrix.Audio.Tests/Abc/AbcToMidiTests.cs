using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Abc;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Abc;

/// <summary>
/// Converting an abc tune to MIDI. Every timing case names the tick it expects, worked out from the
/// written lengths: at the default 480 ticks per quarter note a whole note is 1920 ticks, a quarter
/// 480, an eighth 240 and a sixteenth 120.
/// </summary>
public class AbcToMidiTests
{
    private const int Whole = 1920;
    private const int Half = 960;
    private const int Quarter = 480;
    private const int Eighth = 240;
    private const int Sixteenth = 120;

    private static AbcTune Tune(string text)
    {
        var book = AbcReader.Parse(text);
        book.Tunes.Should().HaveCount(1);
        return book.Tunes[0];
    }

    private static MidiEventCollection Convert(string text, AbcToMidiOptions options = null) =>
        AbcToMidi.Convert(Tune(text), options);

    private static NoteOnEvent[] NotesOn(MidiEventCollection collection, int track) =>
        collection[track].OfType<NoteOnEvent>()
            .OrderBy(n => n.AbsoluteTime)
            .ThenBy(n => n.NoteNumber)
            .ToArray();

    // --- the shape of the result ---------------------------------------------------------------

    [Fact]
    public void the_result_is_a_type_one_collection_with_a_conductor_and_a_track_per_voice()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCDEF|\n";

        //Act
        var collection = Convert(text);

        //Assert
        collection.MidiFileType.Should().Be(1);
        collection.DeltaTicksPerQuarterNote.Should().Be(480);
        collection.Tracks.Should().Be(2);
        collection[0].OfType<NoteOnEvent>().Should().BeEmpty();
        collection[1].OfType<NoteOnEvent>().Should().HaveCount(4);
    }

    [Fact]
    public void the_conductor_track_carries_the_tempo_time_signature_key_title_and_composer()
    {
        //Arrange
        string text = "X:1\nT:A Title\nC:A Composer\nM:6/8\nL:1/8\nQ:3/8=60\nK:Bb\nBcd|\n";

        //Act
        var conductor = Convert(text)[0];

        //Assert
        var tempo = conductor.OfType<TempoEvent>().Single();
        tempo.AbsoluteTime.Should().Be(0);
        tempo.MicrosecondsPerQuarterNote.Should().Be(666667); // 3/8 = 60 is 90 quarter notes a minute

        var meter = conductor.OfType<TimeSignatureEvent>().Single();
        meter.Numerator.Should().Be(6);
        meter.TimeSignature.Should().Be("6/8");

        var key = conductor.OfType<KeySignatureEvent>().Single();
        key.SharpsFlats.Should().Be(-2);
        key.MajorMinor.Should().Be(0);

        var texts = conductor.OfType<TextEvent>().ToArray();
        texts.Should().Contain(t => t.MetaEventType == MetaEventType.SequenceTrackName && t.Text == "A Title");
        texts.Should().Contain(t => t.MetaEventType == MetaEventType.TextEvent && t.Text == "A Composer");
    }

    [Fact]
    public void a_minor_key_is_written_as_a_minor_key_signature()
    {
        //Arrange
        string text = "X:1\nK:Am\nC|\n";

        //Act
        var key = Convert(text)[0].OfType<KeySignatureEvent>().Single();

        //Assert
        key.SharpsFlats.Should().Be(0);
        key.MajorMinor.Should().Be(1);
    }

    [Fact]
    public void every_other_mode_is_written_as_a_major_key_signature()
    {
        //Arrange
        string text = "X:1\nK:DDor\nD|\n";

        //Act
        var key = Convert(text)[0].OfType<KeySignatureEvent>().Single();

        //Assert
        key.SharpsFlats.Should().Be(0);
        key.MajorMinor.Should().Be(0);
    }

    [Fact]
    public void a_key_of_none_writes_no_key_signature()
    {
        //Arrange
        string text = "X:1\nK:none\nC|\n";

        //Act
        var conductor = Convert(text)[0];

        //Assert
        conductor.OfType<KeySignatureEvent>().Should().BeEmpty();
    }

    [Fact]
    public void free_meter_writes_no_time_signature()
    {
        //Arrange
        string text = "X:1\nM:none\nK:C\nC|\n";

        //Act
        var conductor = Convert(text)[0];

        //Assert
        conductor.OfType<TimeSignatureEvent>().Should().BeEmpty();
    }

    [Fact]
    public void a_tune_with_no_tempo_takes_the_default()
    {
        //Arrange
        var options = new AbcToMidiOptions { DefaultBeatsPerMinute = 90 };

        //Act
        var tempo = Convert("X:1\nK:C\nC|\n", options)[0].OfType<TempoEvent>().Single();

        //Assert
        tempo.MicrosecondsPerQuarterNote.Should().Be(666667);
    }

    [Fact]
    public void a_tempo_with_only_a_label_takes_the_default()
    {
        //Arrange
        var options = new AbcToMidiOptions { DefaultBeatsPerMinute = 60 };

        //Act
        var tempo = Convert("X:1\nQ:\"Andante\"\nK:C\nC|\n", options)[0].OfType<TempoEvent>().Single();

        //Assert
        tempo.MicrosecondsPerQuarterNote.Should().Be(1000000);
    }

    // --- notes ---------------------------------------------------------------------------------

    [Fact]
    public void notes_land_on_the_ticks_their_lengths_give_them()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCDEF|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.AbsoluteTime).Should().Equal(0L, Quarter, 2L * Quarter, 3L * Quarter);
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65);
        notes.Should().OnlyContain(n => n.NoteLength == Quarter);
        notes.Should().OnlyContain(n => n.Channel == 1);
    }

    [Fact]
    public void middle_C_is_note_sixty_and_the_octave_marks_move_by_twelve()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nC,, C, C c c' c''|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(36, 48, 60, 72, 84, 96);
    }

    [Fact]
    public void the_whole_diatonic_octave_is_where_the_standard_puts_it()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nCDEFGABc|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65, 67, 69, 71, 72);
    }

    [Fact]
    public void both_the_note_on_and_its_note_off_reach_the_track()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nC|\n";

        //Act
        var track = Convert(text)[1];

        //Assert
        var noteOn = track.OfType<NoteOnEvent>().Single();
        track.Should().Contain(noteOn.OffEvent);
        noteOn.OffEvent.AbsoluteTime.Should().Be(Quarter);
    }

    [Theory]
    [InlineData("A", Eighth)]
    [InlineData("A2", Quarter)]
    [InlineData("A4", Half)]
    [InlineData("A/2", Sixteenth)]
    [InlineData("A/", Sixteenth)]
    [InlineData("A//", 60)]
    [InlineData("A3/2", 360)]
    [InlineData("A8", Whole)]
    public void every_length_form_becomes_the_right_number_of_ticks(string written, int ticks)
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n" + written + "|\n";

        //Act
        var note = NotesOn(Convert(text), 1).Single();

        //Assert
        note.NoteLength.Should().Be(ticks);
    }

    [Theory]
    [InlineData("a>b", 360, 120)]
    [InlineData("a<b", 120, 360)]
    [InlineData("a>>b", 420, 60)]
    [InlineData("a<<b", 60, 420)]
    public void broken_rhythm_moves_the_ticks_both_ways(string written, int firstLength, int secondLength)
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n" + written + "|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes[0].AbsoluteTime.Should().Be(0);
        notes[0].NoteLength.Should().Be(firstLength);
        notes[1].AbsoluteTime.Should().Be(firstLength);
        notes[1].NoteLength.Should().Be(secondLength);
        (notes[0].NoteLength + notes[1].NoteLength).Should().Be(2 * Eighth);
    }

    // --- rests ---------------------------------------------------------------------------------

    [Fact]
    public void a_rest_moves_the_notes_after_it_without_sounding()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nC z2 D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(2);
        notes[0].AbsoluteTime.Should().Be(0);
        notes[1].AbsoluteTime.Should().Be(3L * Quarter);
    }

    [Fact]
    public void an_invisible_rest_takes_exactly_as_long_as_a_printed_one()
    {
        //Arrange
        var printed = NotesOn(Convert("X:1\nL:1/4\nK:C\nC z2 D|\n"), 1);
        var invisible = NotesOn(Convert("X:1\nL:1/4\nK:C\nC x2 D|\n"), 1);

        //Act
        var printedTimes = printed.Select(n => n.AbsoluteTime).ToArray();
        var invisibleTimes = invisible.Select(n => n.AbsoluteTime).ToArray();

        //Assert
        invisibleTimes.Should().Equal(printedTimes);
    }

    [Fact]
    public void a_multi_measure_rest_covers_that_many_bars_of_the_meter()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nZ2|CD|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes[0].AbsoluteTime.Should().Be(2L * Whole);
        notes[1].AbsoluteTime.Should().Be((2L * Whole) + Quarter);
    }

    [Fact]
    public void a_multi_measure_rest_in_three_four_covers_three_quarter_notes_a_bar()
    {
        //Arrange
        string text = "X:1\nM:3/4\nL:1/4\nK:C\nZ4|C|\n";

        //Act
        var note = NotesOn(Convert(text), 1).Single();

        //Assert
        note.AbsoluteTime.Should().Be(4L * 3L * Quarter);
    }

    // --- chords --------------------------------------------------------------------------------

    [Fact]
    public void a_chord_sounds_its_notes_together()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n[CEG]|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(3);
        notes.Should().OnlyContain(n => n.AbsoluteTime == 0);
        notes.Should().OnlyContain(n => n.NoteLength == Quarter);
        notes.Select(n => n.NoteNumber).Should().Equal(60, 64, 67);
    }

    [Fact]
    public void a_length_after_a_chord_lengthens_the_whole_chord()
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n[CEG]3 D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Where(n => n.AbsoluteTime == 0).Should().HaveCount(3);
        notes.Where(n => n.AbsoluteTime == 0).Should().OnlyContain(n => n.NoteLength == 3 * Eighth);
        notes.Single(n => n.NoteNumber == 62).AbsoluteTime.Should().Be(3L * Eighth);
    }

    [Fact]
    public void a_chord_of_mixed_lengths_lasts_as_long_as_its_first_note()
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n[C4EG] D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Where(n => n.AbsoluteTime == 0).Should().OnlyContain(n => n.NoteLength == Half);
        notes.Single(n => n.NoteNumber == 62).AbsoluteTime.Should().Be(Half);
    }

    // --- ties and slurs ------------------------------------------------------------------------

    [Fact]
    public void a_tie_makes_one_longer_note_out_of_two()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\nC4-C4|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().ContainSingle();
        notes[0].AbsoluteTime.Should().Be(0);
        notes[0].NoteLength.Should().Be(Whole);
    }

    [Fact]
    public void a_tie_works_across_a_bar_line()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCDEF-|FEDC|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(7);
        var tied = notes.Single(n => n.AbsoluteTime == 3L * Quarter);
        tied.NoteNumber.Should().Be(65);
        tied.NoteLength.Should().Be(2 * Quarter);
    }

    [Fact]
    public void a_tie_into_another_pitch_is_reported_and_the_notes_play_separately()
    {
        //Arrange
        var tune = Tune("X:1\nM:4/4\nL:1/4\nK:C\nC-D E F|\n");

        //Act
        var notes = NotesOn(AbcToMidi.Convert(tune), 1);

        //Assert
        notes.Should().HaveCount(4);
        notes[0].NoteNumber.Should().Be(60);
        notes[0].NoteLength.Should().Be(Quarter);
        notes[1].NoteNumber.Should().Be(62);
        notes[1].AbsoluteTime.Should().Be(Quarter);
        tune.Problems.Should().ContainSingle(p => p.Contains("different pitches"));
    }

    [Fact]
    public void a_tie_with_nothing_after_it_is_reported_and_the_note_still_sounds()
    {
        //Arrange
        var tune = Tune("X:1\nM:4/4\nL:1/4\nK:C\nCDEF-|\n");

        //Act
        var notes = NotesOn(AbcToMidi.Convert(tune), 1);

        //Assert
        notes.Should().HaveCount(4);
        tune.Problems.Should().ContainSingle(p => p.Contains("no note to join to"));
    }

    [Fact]
    public void a_slur_changes_no_tick_at_all()
    {
        //Arrange
        var slurred = NotesOn(Convert("X:1\nL:1/4\nK:C\n(CDEF)|\n"), 1);
        var plain = NotesOn(Convert("X:1\nL:1/4\nK:C\nCDEF|\n"), 1);

        //Act
        var slurredTimes = slurred.Select(n => (n.AbsoluteTime, n.NoteNumber, n.NoteLength)).ToArray();
        var plainTimes = plain.Select(n => (n.AbsoluteTime, n.NoteNumber, n.NoteLength)).ToArray();

        //Assert
        slurredTimes.Should().Equal(plainTimes);
    }

    // --- grace notes ---------------------------------------------------------------------------

    [Fact]
    public void a_grace_note_takes_its_time_from_the_note_it_precedes()
    {
        //Arrange - the default grace note is 1/64 of a whole note, which is 30 ticks.
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n{g}C D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(3);
        var grace = notes.Single(n => n.NoteNumber == 79);
        grace.AbsoluteTime.Should().Be(0);
        grace.NoteLength.Should().Be(30);

        var middleC = notes.Single(n => n.NoteNumber == 60);
        middleC.AbsoluteTime.Should().Be(30);
        middleC.NoteLength.Should().Be(Quarter - 30);

        notes.Single(n => n.NoteNumber == 62).AbsoluteTime.Should().Be(Quarter);
    }

    [Fact]
    public void several_grace_notes_run_one_after_another_into_the_note()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n{gag}C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Where(n => n.NoteNumber != 60).Select(n => n.AbsoluteTime).OrderBy(t => t)
            .Should().Equal(0L, 30L, 60L);
        notes.Single(n => n.NoteNumber == 60).AbsoluteTime.Should().Be(90);
        notes.Single(n => n.NoteNumber == 60).NoteLength.Should().Be(Quarter - 90);
    }

    [Fact]
    public void grace_notes_never_take_more_than_half_the_note_they_precede()
    {
        //Arrange - eight graces of 1/64 would be 1/8, which is far more than half of a 1/16 note.
        string text = "X:1\nM:4/4\nL:1/16\nK:C\n{gggggggg}C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        var middleC = notes.Single(n => n.NoteNumber == 60);
        middleC.AbsoluteTime.Should().Be(Sixteenth / 2);
        middleC.NoteLength.Should().Be(Sixteenth / 2);
        notes.Where(n => n.NoteNumber == 79).Should().HaveCount(8);
        notes.Where(n => n.NoteNumber == 79).Max(n => n.OffEvent.AbsoluteTime).Should().Be(Sixteenth / 2);
    }

    [Fact]
    public void grace_notes_before_a_rest_take_their_time_from_nothing()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n{g}z C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        var grace = notes.Single(n => n.NoteNumber == 79);
        grace.AbsoluteTime.Should().Be(0);
        grace.NoteLength.Should().Be(30);
        notes.Single(n => n.NoteNumber == 60).AbsoluteTime.Should().Be(Quarter);
    }

    [Fact]
    public void grace_notes_at_the_very_end_still_sound()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nC{g}|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(2);
        notes.Single(n => n.NoteNumber == 79).AbsoluteTime.Should().Be(Quarter);
    }

    [Fact]
    public void the_grace_note_length_is_an_option()
    {
        //Arrange
        var options = new AbcToMidiOptions { GraceNoteLength = new AbcDuration(1, 32) };

        //Act
        var notes = NotesOn(Convert("X:1\nM:4/4\nL:1/4\nK:C\n{g}C|\n", options), 1);

        //Assert
        notes.Single(n => n.NoteNumber == 79).NoteLength.Should().Be(60);
        notes.Single(n => n.NoteNumber == 60).AbsoluteTime.Should().Be(60);
    }

    // --- tuplets -------------------------------------------------------------------------------

    [Fact]
    public void a_triplet_fits_three_notes_into_the_time_of_two()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\n(3ceg C2|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Take(3).Select(n => n.AbsoluteTime).Should().Equal(0L, 160L, 320L);
        notes.Take(3).Should().OnlyContain(n => n.NoteLength == 160);
        notes.Last().AbsoluteTime.Should().Be(Quarter);
    }

    [Fact]
    public void a_quintuplet_in_a_simple_meter_is_five_in_the_time_of_two()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\n(5cdefg|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(5);
        notes.Should().OnlyContain(n => n.NoteLength == 96);
        notes.Last().OffEvent.AbsoluteTime.Should().Be(2L * Eighth);
    }

    [Fact]
    public void a_quintuplet_in_a_compound_meter_is_five_in_the_time_of_three()
    {
        //Arrange
        string text = "X:1\nM:6/8\nL:1/8\nK:C\n(5cdefg|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(5);
        notes.Should().OnlyContain(n => n.NoteLength == 144);
        notes.Last().OffEvent.AbsoluteTime.Should().Be(3L * Eighth);
    }

    [Fact]
    public void an_explicit_tuplet_ratio_covers_only_the_notes_it_names()
    {
        //Arrange - (3:2:2 puts the next TWO notes into the time of two thirds of what they wrote.
        string text = "X:1\nM:4/4\nL:1/8\nK:C\n(3:2:2 G4c2 D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes[0].NoteLength.Should().Be(640);   // four eighths at two thirds
        notes[1].AbsoluteTime.Should().Be(640);
        notes[1].NoteLength.Should().Be(320);   // two eighths at two thirds
        notes[2].AbsoluteTime.Should().Be(960); // the D is outside the tuplet, at its full length
        notes[2].NoteLength.Should().Be(Eighth);
    }

    [Fact]
    public void a_tuplet_that_does_not_divide_into_whole_ticks_is_reported_once()
    {
        //Arrange - a septuplet at 480 ticks per quarter note gives 68.57 ticks a note.
        var tune = Tune("X:1\nM:4/4\nL:1/8\nK:C\n(7cdefgab (7cdefgab|\n");

        //Act
        AbcToMidi.Convert(tune);

        //Assert
        tune.Problems.Count(p => p.Contains("whole ticks")).Should().Be(1);
    }

    [Fact]
    public void a_tuplet_that_divides_exactly_reports_nothing()
    {
        //Arrange
        var tune = Tune("X:1\nM:4/4\nL:1/8\nK:C\n(3ceg|\n");

        //Act
        AbcToMidi.Convert(tune);

        //Assert
        tune.Problems.Should().NotContain(p => p.Contains("whole ticks"));
    }

    [Fact]
    public void a_tuplet_never_pushes_what_follows_out_of_place()
    {
        //Arrange - three septuplets do not divide, but the bar after them still starts on the beat.
        string text = "X:1\nM:4/4\nL:1/8\nK:C\n(7cdefgab (7cdefgab|C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Last().AbsoluteTime.Should().Be(4L * Eighth);
    }

    // --- repeats -------------------------------------------------------------------------------

    [Fact]
    public void a_repeated_section_is_played_twice()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n|:CDEF:|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65, 60, 62, 64, 65);
        notes.Select(n => n.AbsoluteTime).Should()
            .Equal(0L, Quarter, 2L * Quarter, 3L * Quarter, 4L * Quarter, 5L * Quarter, 6L * Quarter, 7L * Quarter);
    }

    [Fact]
    public void extra_dots_repeat_a_section_more_than_twice()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n|::CD::|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Should().HaveCount(6);
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 60, 62, 60, 62);
    }

    [Fact]
    public void first_and_second_endings_select_the_right_bars_in_the_right_order()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/2\nK:C\n|:CD|[1 EF:|[2 GA|]\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65, 60, 62, 67, 69);
        notes.Select(n => n.AbsoluteTime).Should()
            .Equal(0L, Half, Whole, Whole + Half, 2L * Whole, (2L * Whole) + Half, 3L * Whole, (3L * Whole) + Half);
    }

    [Fact]
    public void endings_work_in_the_bar_line_spelling_too()
    {
        //Arrange
        var bracket = NotesOn(Convert("X:1\nL:1/2\nK:C\n|:CD|[1 EF:|[2 GA|]\n"), 1);
        var pipe = NotesOn(Convert("X:1\nL:1/2\nK:C\n|:CD|1 EF:|2 GA|]\n"), 1);

        //Act
        var bracketNotes = bracket.Select(n => n.NoteNumber).ToArray();
        var pipeNotes = pipe.Select(n => n.NoteNumber).ToArray();

        //Assert
        pipeNotes.Should().Equal(bracketNotes);
    }

    [Fact]
    public void four_endings_are_played_in_order()
    {
        //Arrange
        string text = "X:1\nL:1/2\nK:C\n|:C|[1 D:|[2 E:|[3 F:|[4 G|]\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 60, 64, 60, 65, 60, 67);
    }

    [Fact]
    public void an_ending_list_plays_on_each_pass_it_names()
    {
        //Arrange
        string text = "X:1\nL:1/2\nK:C\n|:C|[1,3 D:|[2,4 E:|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 60, 64, 60, 62, 60, 64);
    }

    [Fact]
    public void a_repeat_end_with_no_start_repeats_from_the_beginning_of_the_tune()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/2\nK:C\nCD|EF:|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65, 60, 62, 64, 65);
    }

    [Fact]
    public void a_repeat_that_is_never_closed_is_played_once_and_reported()
    {
        //Arrange
        var tune = Tune("X:1\nM:4/4\nL:1/2\nK:C\nCD||:EF|\n");

        //Act
        var notes = NotesOn(AbcToMidi.Convert(tune), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65);
        tune.Problems.Should().ContainSingle(p => p.Contains("never closed"));
    }

    [Fact]
    public void music_before_a_repeat_is_played_once()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/2\nK:C\nCD||:EF:|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(60, 62, 64, 65, 64, 65);
    }

    // --- accidentals ---------------------------------------------------------------------------

    [Fact]
    public void an_accidental_holds_for_the_rest_of_the_bar_in_every_octave()
    {
        //Arrange - the standard's default is %%propagate-accidentals pitch.
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n^C C c C,|C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(61, 61, 73, 49, 60);
    }

    [Fact]
    public void a_written_natural_cancels_an_accidental_earlier_in_the_bar()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n^C C =C C|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(61, 61, 60, 60);
    }

    [Fact]
    public void a_written_natural_cancels_the_key_signature()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:G\nF =F F|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(66, 65, 65);
    }

    [Fact]
    public void the_key_signature_applies_to_every_octave()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:D\nF f C c|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(66, 78, 61, 73);
    }

    [Fact]
    public void a_flat_key_lowers_the_notes_its_signature_names()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:Bb\nB E A D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(70, 63, 69, 62);
    }

    [Fact]
    public void an_accidental_written_after_the_key_applies_to_the_notes()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:D maj =c\nc C F|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(72, 60, 66);
    }

    [Fact]
    public void double_accidentals_move_the_note_two_semitones()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n^^C __D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(62, 60);
    }

    [Fact]
    public void an_inline_key_change_applies_from_where_it_stands()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nF|[K:G]F|\n";

        //Act
        var collection = Convert(text);
        var notes = NotesOn(collection, 1);

        //Assert
        notes.Select(n => n.NoteNumber).Should().Equal(65, 66);
        collection[0].OfType<KeySignatureEvent>().Should().HaveCount(2);
        collection[0].OfType<KeySignatureEvent>().Last().AbsoluteTime.Should().Be(Quarter);
    }

    [Fact]
    public void an_inline_tempo_change_reaches_the_conductor_track()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nQ:1/4=120\nK:C\nCDEF|[Q:1/4=60]GABc|\n";

        //Act
        var tempos = Convert(text)[0].OfType<TempoEvent>().OrderBy(t => t.AbsoluteTime).ToArray();

        //Assert
        tempos.Should().HaveCount(2);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(500000);
        tempos[1].AbsoluteTime.Should().Be(4L * Quarter);
        tempos[1].MicrosecondsPerQuarterNote.Should().Be(1000000);
    }

    [Theory]
    [InlineData(false, 120.0, 500000)]
    [InlineData(true, 120.0, 500000)]
    [InlineData(false, 150.0, 400000)]
    public void a_body_tempo_without_a_header_tempo_starts_at_the_default_then_changes_where_written(
        bool fieldOnOwnLine, double defaultBpm, int initialMicroseconds)
    {
        //Arrange - the first eight eighth notes fill one 4/4 bar, or 1920 ticks
        string change = fieldOnOwnLine ? "\nQ:1/4=90\n" : " [Q:1/4=90] ";
        string text = "X:1\nL:1/8\nM:4/4\nK:C\nCDEF GABc |" + change + "defg abc'd' |\n";
        var options = new AbcToMidiOptions { DefaultBeatsPerMinute = defaultBpm };

        //Act
        var tempos = Convert(text, options)[0].OfType<TempoEvent>()
            .OrderBy(t => t.AbsoluteTime).ToArray();

        //Assert
        tempos.Should().HaveCount(2);
        tempos[0].AbsoluteTime.Should().Be(0);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(initialMicroseconds);
        tempos[1].AbsoluteTime.Should().Be(Whole);
        tempos[1].MicrosecondsPerQuarterNote.Should().Be(666667);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Q:1/4=60\n")]
    public void a_body_tempo_at_tick_zero_replaces_the_header_or_default_tempo(string headerTempo)
    {
        //Arrange
        string text = "X:1\nL:1/4\n" + headerTempo + "K:C\n[Q:1/4=90]C|\n";

        //Act
        var tempos = Convert(text)[0].OfType<TempoEvent>().ToArray();

        //Assert - a conductor track needs only the tempo that actually takes effect at tick zero
        tempos.Should().HaveCount(1);
        tempos[0].AbsoluteTime.Should().Be(0);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(666667);
    }

    [Fact]
    public void the_last_of_two_body_tempos_at_one_tick_is_the_one_written()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nCDEF|[Q:1/4=90][Q:1/4=100]GABc|\n";

        //Act
        var tempos = Convert(text)[0].OfType<TempoEvent>()
            .OrderBy(t => t.AbsoluteTime).ToArray();

        //Assert
        tempos.Should().HaveCount(2);
        tempos[0].AbsoluteTime.Should().Be(0);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(500000);
        tempos[1].AbsoluteTime.Should().Be(Whole);
        tempos[1].MicrosecondsPerQuarterNote.Should().Be(600000);
    }

    [Fact]
    public void a_body_tempo_with_only_a_label_does_not_change_the_default()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nCDEF|[Q:\"Andante\"]GABc|\n";
        var options = new AbcToMidiOptions { DefaultBeatsPerMinute = 90 };

        //Act
        var tempos = Convert(text, options)[0].OfType<TempoEvent>().ToArray();

        //Assert
        tempos.Should().HaveCount(1);
        tempos[0].AbsoluteTime.Should().Be(0);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(666667);
    }

    [Fact]
    public void the_same_body_tempo_in_two_voices_is_written_once_at_its_tick()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nV:1\nV:2\n" +
            "[V:1]CDEF|[Q:1/4=90]GABc|\n" +
            "[V:2]CDEF|[Q:1/4=90]GABc|\n";

        //Act
        var tempos = Convert(text)[0].OfType<TempoEvent>()
            .OrderBy(t => t.AbsoluteTime).ToArray();

        //Assert
        tempos.Should().HaveCount(2);
        tempos[0].AbsoluteTime.Should().Be(0);
        tempos[0].MicrosecondsPerQuarterNote.Should().Be(500000);
        tempos[1].AbsoluteTime.Should().Be(Whole);
        tempos[1].MicrosecondsPerQuarterNote.Should().Be(666667);
    }

    [Fact]
    public void an_inline_unit_note_length_change_shortens_what_follows()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nC|[L:1/8]D|\n";

        //Act
        var notes = NotesOn(Convert(text), 1);

        //Assert
        notes[0].NoteLength.Should().Be(Quarter);
        notes[1].NoteLength.Should().Be(Eighth);
    }

    // --- voices --------------------------------------------------------------------------------

    [Fact]
    public void two_voices_get_two_tracks_two_channels_and_their_own_names()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nV:1 name=\"Upper\"\nV:2 name=\"Lower\"\n[V:1] cdef|\n[V:2] CDEF|\n";

        //Act
        var collection = Convert(text);

        //Assert
        collection.Tracks.Should().Be(3);
        NotesOn(collection, 1).Should().OnlyContain(n => n.Channel == 1);
        NotesOn(collection, 2).Should().OnlyContain(n => n.Channel == 2);
        collection[1].OfType<TextEvent>()
            .Should().ContainSingle(t => t.MetaEventType == MetaEventType.SequenceTrackName && t.Text == "Upper");
        collection[2].OfType<TextEvent>()
            .Should().ContainSingle(t => t.MetaEventType == MetaEventType.SequenceTrackName && t.Text == "Lower");
    }

    [Fact]
    public void voices_skip_the_percussion_channel()
    {
        //Arrange
        var body = new System.Text.StringBuilder("X:1\nM:4/4\nL:1/1\nK:C\n");
        for (int i = 1; i <= 12; i++)
        {
            body.Append("[V:").Append(i).Append("] C|\n");
        }

        //Act
        var collection = Convert(body.ToString());

        //Assert
        var channels = Enumerable.Range(1, 12).Select(t => NotesOn(collection, t).Single().Channel).ToArray();
        channels.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13);
    }

    [Fact]
    public void more_voices_than_channels_wrap_and_are_reported_once()
    {
        //Arrange
        var body = new System.Text.StringBuilder("X:1\nM:4/4\nL:1/1\nK:C\n");
        for (int i = 1; i <= 17; i++)
        {
            body.Append("[V:").Append(i).Append("] C|\n");
        }

        var tune = Tune(body.ToString());

        //Act
        var collection = AbcToMidi.Convert(tune);

        //Assert
        NotesOn(collection, 16).Single().Channel.Should().Be(1);
        NotesOn(collection, 17).Single().Channel.Should().Be(2);
        tune.Problems.Count(p => p.Contains("channels were reused")).Should().Be(1);
    }

    [Fact]
    public void a_caller_can_choose_a_voices_channel()
    {
        //Arrange
        var options = new AbcToMidiOptions();
        options.VoiceChannels["2"] = 10;

        //Act
        var collection = Convert("X:1\nL:1/4\nK:C\n[V:1] C|\n[V:2] D|\n", options);

        //Assert
        NotesOn(collection, 1).Single().Channel.Should().Be(1);
        NotesOn(collection, 2).Single().Channel.Should().Be(10);
    }

    [Fact]
    public void the_single_voice_of_a_tune_with_no_voice_field_is_reached_by_an_empty_id()
    {
        //Arrange
        var options = new AbcToMidiOptions();
        options.VoiceChannels[string.Empty] = 10;

        //Act
        var collection = Convert("X:1\nL:1/4\nK:C\nCDEF|\n", options);

        //Assert
        NotesOn(collection, 1).Should().OnlyContain(n => n.Channel == 10);
    }

    // --- %%MIDI --------------------------------------------------------------------------------

    [Fact]
    public void a_midi_program_directive_becomes_a_patch_change()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nV:1\n%%MIDI program 40\nCDEF|\n";

        //Act
        var collection = Convert(text);

        //Assert
        var patch = collection[1].OfType<PatchChangeEvent>().Single();
        patch.AbsoluteTime.Should().Be(0);
        patch.Channel.Should().Be(1);
        patch.Patch.Should().Be(40);
        ((GeneralMidiProgram)patch.Patch).Should().Be(GeneralMidiProgram.Violin);
    }

    [Fact]
    public void a_midi_program_directive_part_way_through_lands_where_it_was_written()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCDEF|\n%%MIDI program 73\nGABc|\n";

        //Act
        var patch = Convert(text)[1].OfType<PatchChangeEvent>().Single();

        //Assert
        patch.AbsoluteTime.Should().Be(4L * Quarter);
        patch.Patch.Should().Be(73);
    }

    [Fact]
    public void a_midi_channel_directive_moves_the_voice()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nV:1\n%%MIDI channel 5\nCDEF|\n";

        //Act
        var collection = Convert(text);

        //Assert
        NotesOn(collection, 1).Should().OnlyContain(n => n.Channel == 5);
        collection[1].OfType<PatchChangeEvent>().Should().BeEmpty();
    }

    [Fact]
    public void the_midi_directives_are_ignored_when_the_option_is_off()
    {
        //Arrange
        var options = new AbcToMidiOptions { HonourMidiDirectives = false };
        string text = "X:1\nL:1/4\nK:C\nV:1\n%%MIDI program 40\n%%MIDI channel 5\nCDEF|\n";

        //Act
        var collection = Convert(text, options);

        //Assert
        collection[1].OfType<PatchChangeEvent>().Should().BeEmpty();
        NotesOn(collection, 1).Should().OnlyContain(n => n.Channel == 1);
    }

    [Fact]
    public void the_two_voice_fixture_converts_to_two_instruments_on_two_channels()
    {
        //Arrange
        var book = AbcReader.Read(AbcTestAssets.Path(AbcTestAssets.TwoVoice));

        //Act
        var collection = AbcToMidi.Convert(book.Tunes[0]);

        //Assert
        collection.Tracks.Should().Be(3);
        collection[1].OfType<PatchChangeEvent>().Single().Patch.Should().Be(40);
        collection[2].OfType<PatchChangeEvent>().Single().Patch.Should().Be(42);
        NotesOn(collection, 1).Should().OnlyContain(n => n.Channel == 1);
        NotesOn(collection, 2).Should().OnlyContain(n => n.Channel == 2);
    }

    // --- options -------------------------------------------------------------------------------

    [Fact]
    public void the_resolution_is_an_option_and_the_ticks_follow_it()
    {
        //Arrange
        var options = new AbcToMidiOptions { TicksPerQuarterNote = 96 };

        //Act
        var collection = Convert("X:1\nL:1/4\nK:C\nCD|\n", options);

        //Assert
        collection.DeltaTicksPerQuarterNote.Should().Be(96);
        NotesOn(collection, 1).Select(n => n.AbsoluteTime).Should().Equal(0L, 96L);
    }

    [Fact]
    public void the_velocity_is_an_option()
    {
        //Arrange
        var options = new AbcToMidiOptions { Velocity = 64 };

        //Act
        var notes = NotesOn(Convert("X:1\nL:1/4\nK:C\nCDEF|\n", options), 1);

        //Assert
        notes.Should().OnlyContain(n => n.Velocity == 64);
        notes.Should().HaveCount(4);
    }

    [Fact]
    public void changing_the_options_after_the_conversion_changes_nothing()
    {
        //Arrange
        var options = new AbcToMidiOptions { Velocity = 64 };
        var collection = Convert("X:1\nL:1/4\nK:C\nC|\n", options);

        //Act
        options.Velocity = 127;

        //Assert
        NotesOn(collection, 1).Single().Velocity.Should().Be(64);
    }

    [Fact]
    public void converting_a_null_tune_throws()
    {
        //Arrange
        AbcTune tune = null;

        //Act
        Action act = () => AbcToMidi.Convert(tune);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void converting_the_same_tune_twice_does_not_double_its_problems()
    {
        //Arrange
        var tune = Tune("X:1\nM:4/4\nL:1/8\nK:C\n(7cdefgab|\n");

        //Act
        AbcToMidi.Convert(tune);
        int after = tune.Problems.Count;
        AbcToMidi.Convert(tune);

        //Assert
        tune.Problems.Count.Should().Be(after);
    }

    // --- the rest of the library ---------------------------------------------------------------

    [Fact]
    public void the_collection_exports_as_a_standard_midi_file_and_reads_back()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mid");
        var collection = Convert("X:1\nT:Round Trip\nM:4/4\nL:1/4\nQ:1/4=120\nK:G\n|:GABc|d2d2:|\n");

        //Act
        MidiFile.Export(path, collection);
        var read = new MidiFile(path);

        //Assert
        read.Problems.Should().BeEmpty();
        read.DeltaTicksPerQuarterNote.Should().Be(480);
        read.Events.Tracks.Should().Be(collection.Tracks);
        read.Events[1].OfType<NoteOnEvent>().Should().HaveCount(collection[1].OfType<NoteOnEvent>().Count());
        File.Delete(path);
    }

    [Fact]
    public void the_collection_loads_as_a_playable_sequence()
    {
        //Arrange
        var collection = Convert("X:1\nM:4/4\nL:1/4\nQ:1/4=120\nK:C\nCDEF|GABc|\n");

        //Act
        var sequence = MidiSequence.FromEvents(collection);

        //Assert
        sequence.Length.Should().BeGreaterThan(TimeSpan.Zero);
        sequence.Length.TotalSeconds.Should().BeApproximately(4.0, 0.05);
    }

    [Fact]
    public void a_tune_renders_to_audible_audio_through_a_soundfont()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var collection = Convert("X:1\nM:4/4\nL:1/4\nQ:1/4=120\nK:C\nCEGc|c4|\n");
        var sequence = MidiSequence.FromEvents(collection);

        //Act
        var samples = SoundFontRenderer.Render(soundFont, sequence, 44100, TimeSpan.FromSeconds(0.5));

        //Assert
        samples.Should().NotBeEmpty();
        samples.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    [Fact]
    public void the_single_tune_fixture_converts_to_sixteen_quarter_notes()
    {
        //Arrange
        var book = AbcReader.Read(AbcTestAssets.Path(AbcTestAssets.SingleTune));

        //Act
        var notes = NotesOn(AbcToMidi.Convert(book.Tunes[0]), 1);

        //Assert
        notes.Should().HaveCount(16);
        notes.Should().OnlyContain(n => n.NoteLength == Quarter);
        notes.Select(n => n.NoteNumber).Take(8).Should().Equal(60, 62, 64, 65, 67, 69, 71, 72);
        notes.Last().OffEvent.AbsoluteTime.Should().Be(16L * Quarter);
    }
}

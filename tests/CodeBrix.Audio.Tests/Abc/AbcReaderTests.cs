using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Abc;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Abc;

/// <summary>
/// Reading abc notation into the tune model: the header fields, the music code, and what is
/// reported rather than thrown.
/// </summary>
public class AbcReaderTests
{
    private static AbcTune ParseOne(string text)
    {
        var book = AbcReader.Parse(text);
        book.Tunes.Should().HaveCount(1);
        return book.Tunes[0];
    }

    private static AbcVoice OnlyVoice(AbcTune tune)
    {
        tune.Voices.Should().HaveCount(1);
        return tune.Voices[0];
    }

    // --- the file ------------------------------------------------------------------------------

    [Fact]
    public void a_file_with_two_tunes_reads_as_two_tunes()
    {
        //Arrange
        string text = "X:1\nT:First\nK:C\nCDEF|\n\nX:2\nT:Second\nK:G\nGABc|\n";

        //Act
        var book = AbcReader.Parse(text);

        //Assert
        book.Tunes.Should().HaveCount(2);
        book.Tunes[0].ReferenceNumber.Should().Be(1);
        book.Tunes[0].Title.Should().Be("First");
        book.Tunes[1].ReferenceNumber.Should().Be(2);
        book.Tunes[1].Title.Should().Be("Second");
        book.Problems.Should().BeEmpty();
    }

    [Fact]
    public void free_text_before_the_first_reference_number_is_ignored()
    {
        //Arrange
        string text = "Here is a note about the collection.\nIt runs over two lines.\n\nX:1\nT:Tune\nK:C\nC4|\n";

        //Act
        var book = AbcReader.Parse(text);

        //Assert
        book.Tunes.Should().HaveCount(1);
        book.Tunes[0].Title.Should().Be("Tune");
        book.Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_file_header_is_reported_rather_than_applied()
    {
        //Arrange
        string text = "M:4/4\nL:1/8\n\nX:1\nT:Tune\nK:C\nC4|\n";

        //Act
        var book = AbcReader.Parse(text);

        //Assert
        book.Tunes.Should().HaveCount(1);
        book.Problems.Should().ContainSingle(p => p.Contains("file header"));
    }

    [Fact]
    public void text_with_no_reference_number_reports_that_no_tune_was_found()
    {
        //Arrange
        string text = "just some words\nand some more";

        //Act
        var book = AbcReader.Parse(text);

        //Assert
        book.Tunes.Should().BeEmpty();
        book.Problems.Should().ContainSingle(p => p.Contains("No tune was found"));
    }

    [Fact]
    public void a_comment_line_does_not_end_a_tune()
    {
        //Arrange
        string text = "X:1\nT:Tune\nK:C\nCDEF|\n% a comment on its own line\nGABc|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars.Should().HaveCount(2);
    }

    [Fact]
    public void an_end_of_line_comment_is_removed()
    {
        //Arrange
        string text = "X:1\nT:Tune\nK:C\nCDEF| % four notes\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars[0].Elements.Should().HaveCount(4);
    }

    [Fact]
    public void a_line_continuation_joins_the_music_without_changing_it()
    {
        //Arrange
        string text = "X:1\nT:Tune\nK:C\nCDEF|\\\nGABc|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars.Should().HaveCount(2);
    }

    // --- header fields -------------------------------------------------------------------------

    [Fact]
    public void every_header_field_the_reader_uses_is_read()
    {
        //Arrange
        string text = "X:7\nT:The Title\nT:Another Title\nC:A Composer\nM:6/8\nL:1/8\nQ:3/8=60\nK:Ador\nABc|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.ReferenceNumber.Should().Be(7);
        tune.Titles.Should().Equal("The Title", "Another Title");
        tune.Title.Should().Be("The Title");
        tune.Composer.Should().Be("A Composer");
        tune.Meter.Numerator.Should().Be(6);
        tune.Meter.Denominator.Should().Be(8);
        tune.UnitNoteLength.Length.Should().Be(new AbcDuration(1, 8));
        tune.UnitNoteLength.WasDefaulted.Should().BeFalse();
        tune.Tempo.HasValue.Should().BeTrue();
        tune.Key.Mode.Should().Be(AbcMode.Dorian);
        tune.Key.SharpsFlats.Should().Be(1);
    }

    [Theory]
    [InlineData("M:C", 4, 4, false)]
    [InlineData("M:C|", 2, 2, false)]
    [InlineData("M:none", 0, 0, true)]
    [InlineData("M:(2+3+2)/8", 7, 8, false)]
    [InlineData("M:2+3/8", 5, 8, false)]
    public void the_meter_field_reads_every_form(string field, int numerator, int denominator, bool free)
    {
        //Arrange
        string text = "X:1\n" + field + "\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Meter.IsFree.Should().Be(free);
        tune.Meter.Numerator.Should().Be(numerator);
        tune.Meter.Denominator.Should().Be(denominator);
    }

    [Fact]
    public void a_complex_meter_keeps_its_parts()
    {
        //Arrange
        string text = "X:1\nM:(2+3+2)/8\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Meter.Numerators.Should().Equal(2, 3, 2);
    }

    [Theory]
    [InlineData("M:2/4", 1, 16)]   // 0.5 is below 0.75
    [InlineData("M:4/4", 1, 8)]    // 1.0 is 0.75 or more
    [InlineData("M:6/8", 1, 8)]    // exactly 0.75
    [InlineData("M:3/8", 1, 16)]   // 0.375
    [InlineData("M:C", 1, 8)]
    [InlineData("M:C|", 1, 8)]
    [InlineData("M:none", 1, 8)]
    public void the_unit_note_length_defaults_from_the_meter(string field, long numerator, long denominator)
    {
        //Arrange
        string text = "X:1\n" + field + "\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.UnitNoteLength.Length.Should().Be(new AbcDuration(numerator, denominator));
        tune.UnitNoteLength.WasDefaulted.Should().BeTrue();
    }

    [Fact]
    public void with_no_meter_at_all_the_unit_note_length_is_an_eighth()
    {
        //Arrange
        string text = "X:1\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Meter.IsFree.Should().BeTrue();
        tune.UnitNoteLength.Length.Should().Be(new AbcDuration(1, 8));
        tune.UnitNoteLength.WasDefaulted.Should().BeTrue();
    }

    [Theory]
    [InlineData("Q:1/4=120", 120.0)]
    [InlineData("Q:3/8=50", 75.0)]
    [InlineData("Q:1/4 3/8 1/4 3/8=40", 200.0)]
    [InlineData("Q:\"Allegro\" 1/4=120", 120.0)]
    [InlineData("Q:1/4=120 \"Allegro\"", 120.0)]
    [InlineData("Q:1/2=60", 120.0)]
    public void the_tempo_field_reads_every_form_with_a_value(string field, double quarterNotesPerMinute)
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\n" + field + "\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Tempo.HasValue.Should().BeTrue();
        tune.Tempo.QuarterNotesPerMinute.Should().BeApproximately(quarterNotesPerMinute, 1e-9);
    }

    [Fact]
    public void a_bare_tempo_number_counts_unit_note_lengths()
    {
        //Arrange - the deprecated form: 90 eighth notes a minute is 45 quarter notes a minute.
        string text = "X:1\nM:4/4\nL:1/8\nQ:90\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Tempo.HasValue.Should().BeTrue();
        tune.Tempo.QuarterNotesPerMinute.Should().BeApproximately(45.0, 1e-9);
    }

    [Fact]
    public void a_tempo_with_only_a_label_carries_no_value()
    {
        //Arrange
        string text = "X:1\nQ:\"Andante\"\nK:C\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Tempo.HasValue.Should().BeFalse();
        tune.Tempo.Label.Should().Be("Andante");
    }

    [Theory]
    [InlineData("K:C", 0, AbcMode.Major)]
    [InlineData("K:Am", 0, AbcMode.Minor)]
    [InlineData("K:A minor", 0, AbcMode.Minor)]
    [InlineData("K:C ionian", 0, AbcMode.Ionian)]
    [InlineData("K:A aeolian", 0, AbcMode.Aeolian)]
    [InlineData("K:G mixolydian", 0, AbcMode.Mixolydian)]
    [InlineData("K:D dorian", 0, AbcMode.Dorian)]
    [InlineData("K:E phrygian", 0, AbcMode.Phrygian)]
    [InlineData("K:F lydian", 0, AbcMode.Lydian)]
    [InlineData("K:B locrian", 0, AbcMode.Locrian)]
    [InlineData("K:F#Mix", 5, AbcMode.Mixolydian)]
    [InlineData("K:F#MIX", 5, AbcMode.Mixolydian)]
    [InlineData("K:Bb", -2, AbcMode.Major)]
    [InlineData("K:Ebm", -6, AbcMode.Minor)]
    [InlineData("K:C#Mix", 6, AbcMode.Mixolydian)]
    [InlineData("K:D", 2, AbcMode.Major)]
    public void the_key_field_maps_a_mode_to_its_signature(string field, int sharpsFlats, AbcMode mode)
    {
        //Arrange
        string text = "X:1\n" + field + "\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Key.Mode.Should().Be(mode);
        tune.Key.SharpsFlats.Should().Be(sharpsFlats);
    }

    [Fact]
    public void a_key_with_no_signature_is_read_as_none()
    {
        //Arrange
        string text = "X:1\nK:none\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Key.Mode.Should().Be(AbcMode.None);
        tune.Key.HasNoSignature.Should().BeTrue();
        tune.Key.SharpsFlats.Should().Be(0);
    }

    [Fact]
    public void a_key_carries_the_accidentals_written_after_it()
    {
        //Arrange
        string text = "X:1\nK:D Phr ^f\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Key.Mode.Should().Be(AbcMode.Phrygian);
        tune.Key.SharpsFlats.Should().Be(-2);
        tune.Key.Accidentals.Should().HaveCount(1);
        tune.Key.Accidentals[0].Letter.Should().Be('F');
        tune.Key.Accidentals[0].Accidental.Should().Be(AbcAccidental.Sharp);
        tune.Key.AccidentalFor('F').Should().Be(AbcAccidental.Sharp);
    }

    [Fact]
    public void an_explicit_key_replaces_the_modes_signature()
    {
        //Arrange
        string text = "X:1\nK:D exp _b _e ^f\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Key.IsExplicit.Should().BeTrue();
        tune.Key.AccidentalFor('B').Should().Be(AbcAccidental.Flat);
        tune.Key.AccidentalFor('E').Should().Be(AbcAccidental.Flat);
        tune.Key.AccidentalFor('F').Should().Be(AbcAccidental.Sharp);
        tune.Key.AccidentalFor('C').Should().Be(AbcAccidental.None);
    }

    [Fact]
    public void a_bagpipe_key_is_listed_once_and_carries_no_signature()
    {
        //Arrange
        string text = "X:1\nK:HP\nC|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Key.Mode.Should().Be(AbcMode.Bagpipe);
        tune.Key.HasNoSignature.Should().BeTrue();
        tune.Problems.Should().ContainSingle(p => p.Contains("bagpipe"));
    }

    [Fact]
    public void a_clef_in_the_key_field_is_listed_once()
    {
        //Arrange
        string text = "X:1\nK:C clef=bass\nC|\nK:G clef=treble\nG|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Count(p => p.Contains("Clef")).Should().Be(1);
    }

    // --- inline fields -------------------------------------------------------------------------

    [Theory]
    [InlineData("CDEF|[Q:1/4=90]GABc|")]
    [InlineData("CDEF|\nQ:1/4=90\nGABc|")]
    public void a_body_tempo_is_an_inline_change_and_not_the_header_tempo(string body)
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n" + body + "\n";

        //Act
        var tune = ParseOne(text);
        var change = OnlyVoice(tune).Bars.SelectMany(bar => bar.Elements)
            .OfType<AbcInlineField>().Single();

        //Assert
        tune.Tempo.Should().BeNull();
        change.Field.Should().Be('Q');
        change.Tempo.QuarterNotesPerMinute.Should().Be(90.0);
    }

    [Fact]
    public void an_inline_field_changes_the_state_from_that_point()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCD|[M:3/4]EF|[K:G]GA|\n";

        //Act
        var tune = ParseOne(text);
        var bars = OnlyVoice(tune).Bars;

        //Assert
        bars.Should().HaveCount(3);
        bars[1].Elements.OfType<AbcInlineField>().Should().ContainSingle();
        bars[1].Elements.OfType<AbcInlineField>().First().Meter.Numerator.Should().Be(3);
        bars[2].Elements.OfType<AbcInlineField>().First().Key.SharpsFlats.Should().Be(1);
    }

    [Fact]
    public void a_field_on_its_own_line_in_the_body_changes_the_state_too()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\nCD|\nL:1/8\nEF|\n";

        //Act
        var tune = ParseOne(text);
        var bars = OnlyVoice(tune).Bars;

        //Assert
        ((AbcNote)bars[0].Elements[0]).Length.Should().Be(new AbcDuration(1, 4));
        bars[1].Elements.OfType<AbcNote>().First().Length.Should().Be(new AbcDuration(1, 8));
    }

    // --- notes ---------------------------------------------------------------------------------

    [Fact]
    public void octave_marks_move_the_note_by_octaves()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nC,, C, C c c' c''|\n";

        //Act
        var tune = ParseOne(text);
        var notes = OnlyVoice(tune).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        notes.Select(n => n.Octave).Should().Equal(-2, -1, 0, 1, 2, 3);
        notes.Should().OnlyContain(n => n.Letter == 'C');
    }

    [Fact]
    public void commas_and_apostrophes_cancel_each_other_in_any_order()
    {
        //Arrange - the standard: C,', means the same as C, and C' the same as c.
        string text = "X:1\nL:1/4\nK:C\nC,', C, C' c|\n";

        //Act
        var tune = ParseOne(text);
        var notes = OnlyVoice(tune).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        notes[0].Octave.Should().Be(notes[1].Octave);
        notes[2].Octave.Should().Be(notes[3].Octave);
    }

    [Theory]
    [InlineData("A", 1, 8)]
    [InlineData("A1", 1, 8)]
    [InlineData("A2", 1, 4)]
    [InlineData("A3", 3, 8)]
    [InlineData("A4", 1, 2)]
    [InlineData("A/2", 1, 16)]
    [InlineData("A/", 1, 16)]
    [InlineData("A//", 1, 32)]
    [InlineData("A/4", 1, 32)]
    [InlineData("A3/2", 3, 16)]
    public void every_length_form_is_read(string written, long numerator, long denominator)
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n" + written + "|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars[0].Elements.OfType<AbcNote>().First().Length
            .Should().Be(new AbcDuration(numerator, denominator));
    }

    [Theory]
    [InlineData("a>b", 3, 16, 1, 16)]
    [InlineData("a<b", 1, 16, 3, 16)]
    [InlineData("a>>b", 7, 32, 1, 32)]
    [InlineData("a<<b", 1, 32, 7, 32)]
    [InlineData("a>>>b", 15, 64, 1, 64)]
    public void broken_rhythm_dots_one_note_and_shortens_the_other(
        string written, long firstNumerator, long firstDenominator, long secondNumerator, long secondDenominator)
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n" + written + "|\n";

        //Act
        var tune = ParseOne(text);
        var notes = OnlyVoice(tune).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        notes.Should().HaveCount(2);
        notes[0].Length.Should().Be(new AbcDuration(firstNumerator, firstDenominator));
        notes[1].Length.Should().Be(new AbcDuration(secondNumerator, secondDenominator));
    }

    [Fact]
    public void broken_rhythm_reads_past_a_grace_group()
    {
        //Arrange - the standard: A<{g}A and A{g}<A both mean A/2 {g} A3/2.
        var first = ParseOne("X:1\nL:1/8\nK:C\nA<{g}A|\n");
        var second = ParseOne("X:1\nL:1/8\nK:C\nA{g}<A|\n");

        //Act
        var firstNotes = OnlyVoice(first).Bars[0].Elements.OfType<AbcNote>().ToArray();
        var secondNotes = OnlyVoice(second).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        firstNotes.Select(n => n.Length).Should().Equal(new AbcDuration(1, 16), new AbcDuration(3, 16));
        secondNotes.Select(n => n.Length).Should().Equal(new AbcDuration(1, 16), new AbcDuration(3, 16));
    }

    // --- rests ---------------------------------------------------------------------------------

    [Fact]
    public void rests_are_read_with_their_lengths()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\nz2 x4|\n";

        //Act
        var tune = ParseOne(text);
        var rests = OnlyVoice(tune).Bars[0].Elements.OfType<AbcRest>().ToArray();

        //Assert
        rests.Should().HaveCount(2);
        rests[0].IsVisible.Should().BeTrue();
        rests[0].Length.Should().Be(new AbcDuration(1, 4));
        rests[1].IsVisible.Should().BeFalse();
        rests[1].Length.Should().Be(new AbcDuration(1, 2));
    }

    [Fact]
    public void a_multi_measure_rest_lasts_that_many_bars_of_the_meter()
    {
        //Arrange
        string text = "X:1\nM:3/4\nL:1/8\nK:C\nZ4|CD|\n";

        //Act
        var tune = ParseOne(text);
        var rest = OnlyVoice(tune).Bars[0].Elements.OfType<AbcRest>().First();

        //Assert
        rest.IsMultiMeasure.Should().BeTrue();
        rest.MeasureCount.Should().Be(4);
        rest.Length.Should().Be(new AbcDuration(3, 1));
    }

    [Fact]
    public void a_multi_measure_rest_with_no_count_is_one_bar()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\nZ|CD|\n";

        //Act
        var tune = ParseOne(text);
        var rest = OnlyVoice(tune).Bars[0].Elements.OfType<AbcRest>().First();

        //Assert
        rest.MeasureCount.Should().Be(1);
        rest.Length.Should().Be(AbcDuration.Whole);
    }

    // --- chords, ties, slurs -------------------------------------------------------------------

    [Fact]
    public void a_chord_holds_its_notes_and_takes_the_length_of_the_first()
    {
        //Arrange
        string text = "X:1\nL:1/8\nK:C\n[C2E2G]|\n";

        //Act
        var tune = ParseOne(text);
        var chord = OnlyVoice(tune).Bars[0].Elements.OfType<AbcChord>().First();

        //Assert
        chord.Notes.Should().HaveCount(3);
        chord.Length.Should().Be(new AbcDuration(1, 4));
    }

    [Fact]
    public void a_length_after_the_bracket_multiplies_the_chord()
    {
        //Arrange - the standard: [C2E2G2]3 means the same as [CEG]6.
        var withBoth = ParseOne("X:1\nL:1/8\nK:C\n[C2E2G2]3|\n");
        var withOuter = ParseOne("X:1\nL:1/8\nK:C\n[CEG]6|\n");

        //Act
        var first = OnlyVoice(withBoth).Bars[0].Elements.OfType<AbcChord>().First();
        var second = OnlyVoice(withOuter).Bars[0].Elements.OfType<AbcChord>().First();

        //Assert
        first.Length.Should().Be(new AbcDuration(3, 4));
        first.Length.Should().Be(second.Length);
    }

    [Fact]
    public void a_tie_is_recorded_on_the_note_before_it()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nC-C D|\n";

        //Act
        var tune = ParseOne(text);
        var notes = OnlyVoice(tune).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        notes[0].TiedToNext.Should().BeTrue();
        notes[1].TiedToNext.Should().BeFalse();
        notes[2].TiedToNext.Should().BeFalse();
    }

    [Fact]
    public void a_slur_changes_nothing_in_the_model()
    {
        //Arrange
        var slurred = ParseOne("X:1\nL:1/4\nK:C\n(CDEF)|\n");
        var plain = ParseOne("X:1\nL:1/4\nK:C\nCDEF|\n");

        //Act
        var slurredNotes = OnlyVoice(slurred).Bars[0].Elements;
        var plainNotes = OnlyVoice(plain).Bars[0].Elements;

        //Assert
        slurredNotes.Should().HaveCount(plainNotes.Count);
        slurredNotes.OfType<AbcNote>().Select(n => n.Length)
            .Should().Equal(plainNotes.OfType<AbcNote>().Select(n => n.Length));
    }

    // --- grace notes and tuplets ---------------------------------------------------------------

    [Fact]
    public void a_grace_group_is_read_with_its_notes()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n{GdGe}C|\n";

        //Act
        var tune = ParseOne(text);
        var grace = OnlyVoice(tune).Bars[0].Elements.OfType<AbcGraceGroup>().First();

        //Assert
        grace.Notes.Should().HaveCount(4);
        grace.IsAcciaccatura.Should().BeFalse();
    }

    [Fact]
    public void a_leading_slash_marks_an_acciaccatura()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n{/g}C|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars[0].Elements.OfType<AbcGraceGroup>().First().IsAcciaccatura.Should().BeTrue();
    }

    [Theory]
    [InlineData("M:4/4", 2, 3)]
    [InlineData("M:4/4", 3, 2)]
    [InlineData("M:4/4", 4, 3)]
    [InlineData("M:4/4", 5, 2)]
    [InlineData("M:6/8", 5, 3)]
    [InlineData("M:4/4", 6, 2)]
    [InlineData("M:4/4", 7, 2)]
    [InlineData("M:9/8", 7, 3)]
    [InlineData("M:4/4", 8, 3)]
    [InlineData("M:4/4", 9, 2)]
    [InlineData("M:12/8", 9, 3)]
    public void an_omitted_tuplet_ratio_comes_from_the_standards_table(string meter, int p, int q)
    {
        //Arrange
        string notes = new string('c', p);
        string text = "X:1\n" + meter + "\nL:1/8\nK:C\n(" + p + notes + "|\n";

        //Act
        var tune = ParseOne(text);
        var tuplet = OnlyVoice(tune).Bars[0].Elements.OfType<AbcTupletGroup>().First();

        //Assert
        tuplet.NotesInTuplet.Should().Be(p);
        tuplet.InTheTimeOf.Should().Be(q);
        tuplet.Elements.Should().HaveCount(p);
    }

    [Fact]
    public void an_explicit_tuplet_ratio_is_used_as_written()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/8\nK:C\n(3:2:2 G4c2|\n";

        //Act
        var tune = ParseOne(text);
        var tuplet = OnlyVoice(tune).Bars[0].Elements.OfType<AbcTupletGroup>().First();

        //Assert
        tuplet.NotesInTuplet.Should().Be(3);
        tuplet.InTheTimeOf.Should().Be(2);
        tuplet.ElementCount.Should().Be(2);
        tuplet.Elements.Should().HaveCount(2);
        tuplet.Ratio.Should().Be(new AbcDuration(2, 3));
    }

    // --- bar lines and endings -----------------------------------------------------------------

    [Theory]
    [InlineData("|", AbcBarLineKind.Thin, false, false)]
    [InlineData("||", AbcBarLineKind.ThinThin, false, false)]
    [InlineData("|]", AbcBarLineKind.ThinThick, false, false)]
    [InlineData("[|", AbcBarLineKind.ThickThin, false, false)]
    [InlineData("[|]", AbcBarLineKind.Invisible, false, false)]
    [InlineData(".|", AbcBarLineKind.Dotted, false, false)]
    [InlineData(":|", AbcBarLineKind.Thin, false, true)]
    [InlineData("::", AbcBarLineKind.Thin, true, true)]
    public void every_bar_line_kind_is_read(string written, AbcBarLineKind kind, bool starts, bool ends)
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nC" + written + "D|\n";

        //Act
        var tune = ParseOne(text);
        var line = OnlyVoice(tune).Bars[0].ClosingBarLine;

        //Assert
        line.Kind.Should().Be(kind);
        line.StartsRepeat.Should().Be(starts);
        line.EndsRepeat.Should().Be(ends);
    }

    [Fact]
    public void a_start_of_repeat_is_recorded_on_the_bar_it_opens()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n|:CD:|\n";

        //Act
        var tune = ParseOne(text);
        var bar = OnlyVoice(tune).Bars[0];

        //Assert
        bar.OpeningBarLine.StartsRepeat.Should().BeTrue();
        bar.ClosingBarLine.EndsRepeat.Should().BeTrue();
        bar.ClosingBarLine.RepeatCount.Should().Be(2);
    }

    [Fact]
    public void a_bare_bar_line_at_the_start_of_a_line_does_not_make_an_empty_bar()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nCD|\n|EF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("[1")]
    [InlineData("|1")]
    public void both_spellings_of_an_ending_mark_are_read(string mark)
    {
        //Arrange
        string body = mark == "[1"
            ? "|:CD|[1 EF:|[2 GA|]"
            : "|:CD|1 EF:|2 GA|]";
        string text = "X:1\nL:1/4\nK:C\n" + body + "\n";

        //Act
        var tune = ParseOne(text);
        var bars = OnlyVoice(tune).Bars;

        //Assert
        bars.Should().HaveCount(3);
        bars[0].Endings.Should().BeEmpty();
        bars[1].Endings.Should().Equal(1);
        bars[2].Endings.Should().Equal(2);
    }

    [Fact]
    public void an_ending_mark_reads_a_list_and_a_range()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n|:CD|[1,3 EF:|[2,4 GA:|\n";

        //Act
        var listed = ParseOne(text);
        var ranged = ParseOne("X:1\nL:1/4\nK:C\n|:CD|[1-3 EF:|[4 GA:|\n");

        //Assert
        OnlyVoice(listed).Bars[1].Endings.Should().Equal(1, 3);
        OnlyVoice(listed).Bars[2].Endings.Should().Equal(2, 4);
        OnlyVoice(ranged).Bars[1].Endings.Should().Equal(1, 2, 3);
    }

    // --- voices --------------------------------------------------------------------------------

    [Fact]
    public void a_tune_with_no_voice_field_has_one_voice()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Voices.Should().HaveCount(1);
        tune.Voices[0].Id.Should().BeEmpty();
    }

    [Fact]
    public void voices_are_read_in_the_order_their_ids_first_appear()
    {
        //Arrange
        string text = "X:1\nM:4/4\nL:1/4\nK:C\n[V:T1] CDEF|\n[V:B1] C,D,E,F,|\n[V:T1] GABc|\n[V:B1] G,A,B,C|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Voices.Select(v => v.Id).Should().Equal("T1", "B1");
        tune.Voices[0].Bars.Should().HaveCount(2);
        tune.Voices[1].Bars.Should().HaveCount(2);
    }

    [Fact]
    public void a_voice_field_carries_its_name()
    {
        //Arrange
        string text = "X:1\nM:C\nK:C\nV:T1 clef=treble-8 name=\"Tenore I\" snm=\"T.I\"\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Voices[0].Id.Should().Be("T1");
        tune.Voices[0].Name.Should().Be("Tenore I");
    }

    [Fact]
    public void the_two_layouts_the_standard_shows_produce_the_same_voices()
    {
        //Arrange
        string interleaved = "X:1\nM:C\nL:1/4\nK:C\n[V:1] CD|\n[V:2] EF|\n[V:1] GA|\n[V:2] Bc|\n";
        string blocked = "X:1\nM:C\nL:1/4\nK:C\nV:1\nCD|GA|\nV:2\nEF|Bc|\n";

        //Act
        var first = ParseOne(interleaved);
        var second = ParseOne(blocked);

        //Assert
        first.Voices.Select(v => v.Id).Should().Equal(second.Voices.Select(v => v.Id));
        first.Voices[0].Bars.Should().HaveCount(second.Voices[0].Bars.Count);
        first.Voices[1].Bars.Should().HaveCount(second.Voices[1].Bars.Count);
    }

    // --- %%MIDI --------------------------------------------------------------------------------

    [Fact]
    public void a_midi_program_directive_is_read_onto_the_voice()
    {
        //Arrange
        string text = "X:1\nM:C\nK:C\nV:1\n%%MIDI program 40\n%%MIDI channel 3\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Voices[0].MidiProgram.Should().Be(40);
        tune.Voices[0].MidiChannel.Should().Be(3);
    }

    [Fact]
    public void a_midi_program_directive_in_the_body_sits_where_it_was_written()
    {
        //Arrange
        string text = "X:1\nM:C\nL:1/4\nK:C\nCDEF|\n%%MIDI program 73\nGABc|\n";

        //Act
        var tune = ParseOne(text);
        var bars = OnlyVoice(tune).Bars;

        //Assert
        bars[0].Elements.OfType<AbcProgramChange>().Should().BeEmpty();
        bars[1].Elements.OfType<AbcProgramChange>().Should().ContainSingle();
        bars[1].Elements.OfType<AbcProgramChange>().First().Program.Should().Be(73);
    }

    [Fact]
    public void an_inline_instruction_carries_a_midi_directive_too()
    {
        //Arrange
        string text = "X:1\nM:C\nL:1/4\nK:C\n[I:MIDI program 56] CDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        OnlyVoice(tune).Bars[0].Elements.OfType<AbcProgramChange>().First().Program.Should().Be(56);
    }

    [Fact]
    public void other_midi_directives_are_listed_once()
    {
        //Arrange
        string text = "X:1\nM:C\nK:C\n%%MIDI gchord fzczfzcz\n%%MIDI bassprog 24\n%%MIDI drummap 36 60\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Count(p => p.Contains("%%MIDI directives other than")).Should().Be(1);
    }

    // --- what is skipped -----------------------------------------------------------------------

    [Fact]
    public void each_skipped_kind_is_listed_exactly_once()
    {
        //Arrange
        string text = string.Join('\n',
            "X:1",
            "M:C",
            "L:1/8",
            "R:reel",
            "N:a note",
            "Z:transcribed by somebody",
            "K:C",
            "%%propagate-accidentals pitch",
            "%%score (1 2)",
            "\"Am\"!trill!.a ~b \"G7\"Hc \"^above\"d|",
            "\"C\"!fermata!e f \"_below\"g a|",
            "w:la la la la",
            "W:printed after the tune",
            "P:A");

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Count(p => p.Contains("Decorations")).Should().Be(1);
        tune.Problems.Count(p => p.Contains("Chord symbols")).Should().Be(1);
        tune.Problems.Count(p => p.Contains("annotations")).Should().Be(1);
        tune.Problems.Count(p => p.Contains("Stylesheet directives")).Should().Be(1);
        tune.Problems.Count(p => p.StartsWith("The w: field")).Should().Be(1);
        tune.Problems.Count(p => p.StartsWith("The W: field")).Should().Be(1);
        tune.Problems.Count(p => p.StartsWith("The P: field")).Should().Be(1);
        tune.Problems.Count(p => p.StartsWith("The R: field")).Should().Be(1);
    }

    [Fact]
    public void decorations_and_chord_symbols_do_not_change_the_notes()
    {
        //Arrange
        var decorated = ParseOne("X:1\nL:1/4\nK:C\n\"Am\"!trill!.C ~D THE|\n");
        var plain = ParseOne("X:1\nL:1/4\nK:C\nCDE|\n");

        //Act
        var decoratedNotes = OnlyVoice(decorated).Bars[0].Elements.OfType<AbcNote>().ToArray();
        var plainNotes = OnlyVoice(plain).Bars[0].Elements.OfType<AbcNote>().ToArray();

        //Assert
        decoratedNotes.Select(n => n.Letter).Should().Equal(plainNotes.Select(n => n.Letter));
        decoratedNotes.Select(n => n.Length).Should().Equal(plainNotes.Select(n => n.Length));
    }

    // --- malformed input -----------------------------------------------------------------------

    [Fact]
    public void garbage_is_reported_rather_than_thrown()
    {
        //Arrange
        string text = "X:1\nK:C\n@@@ ### $$$\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Should().NotBeEmpty();
    }

    [Fact]
    public void an_unterminated_chord_is_reported_and_its_notes_kept()
    {
        //Arrange
        string text = "X:1\nL:1/4\nK:C\n[CEG|D|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Should().ContainSingle(p => p.Contains("unterminated"));
        OnlyVoice(tune).Bars[0].Elements.OfType<AbcChord>().First().Notes.Should().HaveCount(3);
    }

    [Fact]
    public void a_bad_length_is_reported_rather_than_thrown()
    {
        //Arrange
        string text = "X:1\nL:nonsense\nK:C\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Should().ContainSingle(p => p.Contains("unit note length"));
        OnlyVoice(tune).Bars[0].Elements.Should().HaveCount(4);
    }

    [Fact]
    public void an_unknown_key_is_reported_rather_than_thrown()
    {
        //Arrange
        string text = "X:1\nK:Q#weird\nCDEF|\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Should().ContainSingle(p => p.Contains("key"));
        tune.Key.HasNoSignature.Should().BeTrue();
    }

    [Fact]
    public void a_tune_with_no_key_field_is_reported()
    {
        //Arrange
        string text = "X:1\nT:No key\n";

        //Act
        var tune = ParseOne(text);

        //Assert
        tune.Problems.Should().ContainSingle(p => p.Contains("no K: field"));
    }

    // --- entry points --------------------------------------------------------------------------

    [Fact]
    public void parsing_null_text_throws()
    {
        //Arrange
        string text = null;

        //Act
        Action act = () => AbcReader.Parse(text);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void reading_a_null_path_throws()
    {
        //Arrange
        string path = null;

        //Act
        Action act = () => AbcReader.Read(path);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void reading_a_null_stream_throws()
    {
        //Arrange
        Stream stream = null;

        //Act
        Action act = () => AbcReader.Read(stream);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void reading_a_missing_file_throws()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".abc");

        //Act
        Action act = () => AbcReader.Read(path);

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void a_tune_reads_from_a_file()
    {
        //Arrange
        string path = AbcTestAssets.Path(AbcTestAssets.SingleTune);

        //Act
        var book = AbcReader.Read(path);

        //Assert
        book.Tunes.Should().HaveCount(1);
        book.Tunes[0].Title.Should().Be("Scale Practice");
        book.Tunes[0].Composer.Should().Be("CodeBrix.Audio test fixture");
        book.Tunes[0].Voices[0].Bars.Should().HaveCount(4);
        book.Tunes[0].Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_tune_reads_from_a_stream()
    {
        //Arrange
        using var stream = AbcTestAssets.Open(AbcTestAssets.SingleTune);

        //Act
        var book = AbcReader.Read(stream);

        //Assert
        book.Tunes.Should().HaveCount(1);
        book.Tunes[0].Title.Should().Be("Scale Practice");
    }

    [Fact]
    public void the_two_tune_fixture_reads_as_a_tune_book()
    {
        //Arrange
        string path = AbcTestAssets.Path(AbcTestAssets.TwoTuneBook);

        //Act
        var book = AbcReader.Read(path);

        //Assert
        book.Tunes.Should().HaveCount(2);
        book.Tunes[0].Key.SharpsFlats.Should().Be(1);
        book.Tunes[0].Meter.Denominator.Should().Be(4);
        book.Tunes[1].Titles.Should().Equal("Second Tune", "Also Known As");
        book.Tunes[1].Composer.Should().Be("Anon.");
        book.Tunes[1].Key.Mode.Should().Be(AbcMode.Minor);
        book.Tunes[1].Meter.Numerator.Should().Be(6);
    }

    [Fact]
    public void the_two_voice_fixture_reads_two_named_voices_with_their_instruments()
    {
        //Arrange
        string path = AbcTestAssets.Path(AbcTestAssets.TwoVoice);

        //Act
        var book = AbcReader.Read(path);
        var tune = book.Tunes[0];

        //Assert
        tune.Voices.Should().HaveCount(2);
        tune.Voices[0].Id.Should().Be("1");
        tune.Voices[0].Name.Should().Be("Upper");
        tune.Voices[0].MidiProgram.Should().Be(40);
        tune.Voices[1].Name.Should().Be("Lower");
        tune.Voices[1].MidiProgram.Should().Be(42);
    }
}

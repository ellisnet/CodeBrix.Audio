using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// Covers <see cref="InstrumentCoverage"/> and <see cref="InstrumentKeyRange"/> - what a library
/// says it can play, so that a silent part can be explained rather than guessed at.
/// </summary>
public class InstrumentCoverageTests
{
    // ----- the key range -----

    [Fact]
    public void a_key_range_covers_its_own_bounds_and_nothing_outside_them()
    {
        //Arrange & Act
        var range = new InstrumentKeyRange(55, 96);

        //Assert
        range.LowestKey.Should().Be(55);
        range.HighestKey.Should().Be(96);
        range.KeyCount.Should().Be(42);
        range.Contains(55).Should().BeTrue();
        range.Contains(96).Should().BeTrue();
        range.Contains(54).Should().BeFalse();
        range.Contains(97).Should().BeFalse();
        range.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void the_default_key_range_is_empty_rather_than_the_single_note_zero()
    {
        //Arrange & Act
        var range = default(InstrumentKeyRange);

        //Assert
        // A range nobody filled in must read as "nothing covered", not as "note zero covered".
        range.IsEmpty.Should().BeTrue();
        range.Contains(0).Should().BeFalse();
        range.Should().Be(InstrumentKeyRange.Empty);
    }

    [Fact]
    public void the_full_key_range_is_the_whole_keyboard()
    {
        //Act & Assert
        InstrumentKeyRange.Full.LowestKey.Should().Be(0);
        InstrumentKeyRange.Full.HighestKey.Should().Be(127);
        InstrumentKeyRange.Full.Contains(64).Should().BeTrue();
    }

    [Fact]
    public void a_key_range_refuses_bounds_that_are_not_MIDI_notes()
    {
        //Act
        var tooLow = () => new InstrumentKeyRange(-1, 60);
        var tooHigh = () => new InstrumentKeyRange(60, 128);
        var backwards = () => new InstrumentKeyRange(60, 59);

        //Assert
        tooLow.Should().Throw<ArgumentOutOfRangeException>();
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void joining_two_key_ranges_spans_both()
    {
        //Arrange
        var low = new InstrumentKeyRange(0, 40);
        var high = new InstrumentKeyRange(80, 100);

        //Act
        var joined = low.UnionWith(high);

        //Assert
        joined.LowestKey.Should().Be(0);
        joined.HighestKey.Should().Be(100);
        low.UnionWith(InstrumentKeyRange.Empty).Should().Be(low);
        InstrumentKeyRange.Empty.UnionWith(high).Should().Be(high);
    }

    [Fact]
    public void a_key_range_prints_itself_readably()
    {
        //Act & Assert
        new InstrumentKeyRange(35, 81).ToString().Should().Be("35-81");
        InstrumentKeyRange.Empty.ToString().Should().Be("(none)");
    }

    [Fact]
    public void two_key_ranges_over_the_same_notes_are_equal()
    {
        //Arrange
        var first = new InstrumentKeyRange(10, 20);
        var second = new InstrumentKeyRange(10, 20);
        var different = new InstrumentKeyRange(10, 21);

        //Act & Assert
        (first == second).Should().BeTrue();
        (first != different).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    // ----- coverage -----

    [Fact]
    public void General_coverage_is_the_whole_General_MIDI_set()
    {
        //Act
        var coverage = InstrumentCoverage.General;

        //Assert
        coverage.Programs.Should().HaveCount(GeneralMidi.ProgramCount);
        coverage.CoversProgram(0).Should().BeTrue();
        coverage.CoversProgram(127).Should().BeTrue();
        coverage.CoversNote(73, 0).Should().BeTrue();
        coverage.PercussionNotes.Should().HaveCount(
            GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1);
        coverage.CoversPercussionNote(GeneralMidi.LowestPercussionNote).Should().BeTrue();
        coverage.CoversPercussionNote(GeneralMidi.HighestPercussionNote).Should().BeTrue();
        coverage.CoversPercussionNote(GeneralMidi.HighestPercussionNote + 1).Should().BeFalse();
    }

    [Fact]
    public void None_coverage_can_play_nothing()
    {
        //Act
        var coverage = InstrumentCoverage.None;

        //Assert
        coverage.Programs.Should().BeEmpty();
        coverage.PercussionNotes.Should().BeEmpty();
        coverage.CoversProgram(0).Should().BeFalse();
        coverage.CoversPercussionNote(38).Should().BeFalse();
    }

    [Fact]
    public void a_coverage_report_lists_programs_and_percussion_notes_in_order()
    {
        //Arrange & Act
        var coverage = new InstrumentCoverage([40, 0, 73], [46, 35, 42]);

        //Assert
        coverage.Programs.Should().Equal(0, 40, 73);
        coverage.PercussionNotes.Should().Equal(35, 42, 46);
    }

    [Fact]
    public void a_program_listed_without_a_range_covers_the_whole_keyboard()
    {
        //Arrange & Act
        var coverage = new InstrumentCoverage([73], Array.Empty<int>());

        //Assert
        coverage.KeyRangeOf(73).Should().Be(InstrumentKeyRange.Full);
        coverage.CoversNote(73, 0).Should().BeTrue();
        coverage.CoversNote(73, 127).Should().BeTrue();
    }

    [Fact]
    public void a_sampled_program_reports_the_notes_it_was_recorded_over()
    {
        //Arrange
        var ranges = new Dictionary<int, InstrumentKeyRange>
        {
            [73] = new InstrumentKeyRange(59, 96)
        };

        //Act
        var coverage = new InstrumentCoverage(ranges, Array.Empty<int>());

        //Assert
        // The failure this exists for: notes below a flute's lowest recorded note are silent, and
        // a silent part is otherwise indistinguishable from a bug in the music.
        coverage.CoversProgram(73).Should().BeTrue();
        coverage.CoversNote(73, 60).Should().BeTrue();
        coverage.CoversNote(73, 48).Should().BeFalse();
        coverage.KeyRangeOf(73).ToString().Should().Be("59-96");
    }

    [Fact]
    public void an_uncovered_program_reports_an_empty_range_rather_than_throwing()
    {
        //Arrange
        var coverage = new InstrumentCoverage([0], Array.Empty<int>());

        //Act & Assert
        coverage.KeyRangeOf(40).IsEmpty.Should().BeTrue();
        coverage.CoversNote(40, 60).Should().BeFalse();
    }

    [Fact]
    public void a_program_given_an_empty_range_counts_as_not_covered()
    {
        //Arrange
        var ranges = new Dictionary<int, InstrumentKeyRange>
        {
            [40] = InstrumentKeyRange.Empty
        };

        //Act
        var coverage = new InstrumentCoverage(ranges, Array.Empty<int>());

        //Assert
        coverage.CoversProgram(40).Should().BeFalse();
        coverage.Programs.Should().BeEmpty();
    }

    [Fact]
    public void a_coverage_report_refuses_numbers_that_are_not_programs_or_notes()
    {
        //Act
        var badProgram = () => new InstrumentCoverage([128], Array.Empty<int>());
        var negativeProgram = () => new InstrumentCoverage([-1], Array.Empty<int>());
        var badNote = () => new InstrumentCoverage(Array.Empty<int>(), [128]);

        //Assert
        badProgram.Should().Throw<ArgumentOutOfRangeException>();
        negativeProgram.Should().Throw<ArgumentOutOfRangeException>();
        badNote.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_coverage_report_refuses_null_arguments()
    {
        //Act
        var nullPrograms = () => new InstrumentCoverage((IEnumerable<int>)null, Array.Empty<int>());
        var nullNotes = () => new InstrumentCoverage(Array.Empty<int>(), null);

        //Assert
        nullPrograms.Should().Throw<ArgumentNullException>();
        nullNotes.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void a_coverage_report_prints_a_summary()
    {
        //Arrange
        var coverage = new InstrumentCoverage([0, 1], [35]);

        //Act & Assert
        coverage.ToString().Should().Be("2 program(s), 1 percussion note(s)");
    }

    [Fact]
    public void a_library_can_report_a_shape_it_does_not_offer()
    {
        //Arrange
        var perPartOnly = new FakeInstrumentLibrary(
            $"PerPartOnly-{Guid.NewGuid():N}", supportsMultiTimbral: false);
        var multiTimbralOnly = new FakeInstrumentLibrary(
            $"MultiOnly-{Guid.NewGuid():N}", supportsPerPart: false);

        //Act
        var noMultiTimbral = () => perPartOnly.CreateMultiTimbralSynthesizer(44100);
        var noPerPart = () => multiTimbralOnly.CreateSynthesizer(0, 44100);

        //Assert
        // The contract is that the missing shape throws NotSupportedException and the flag says so
        // beforehand, which is how a caller avoids the exception rather than catching it.
        perPartOnly.SupportsMultiTimbral.Should().BeFalse();
        multiTimbralOnly.SupportsPerPart.Should().BeFalse();
        noMultiTimbral.Should().Throw<NotSupportedException>();
        noPerPart.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void a_library_that_offers_only_one_shape_still_creates_the_other_ones_it_has()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(
            $"PerPartOnly-{Guid.NewGuid():N}", supportsMultiTimbral: false);

        //Act
        var synthesizer = library.CreateSynthesizer(0, 44100);
        var percussion = library.CreatePercussionSynthesizer(44100);

        //Assert
        synthesizer.SampleRate.Should().Be(44100);
        percussion.SampleRate.Should().Be(44100);
        library.CreatedCount.Should().Be(2);
    }
}

using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Covers the one musical-time subdivision table the delay, the LFO, the arpeggiator and a
/// <c>musical_time</c> control all share. The numbers come from the reference-player measurements:
/// echoes of a delay in musical-time mode were timed for every index at 120 BPM.
/// </summary>
public class DecentSamplerTempoTests
{
    [Theory]
    [InlineData(0, 1.0 / 96.0)]
    [InlineData(1, 1.0 / 64.0)]
    [InlineData(2, 1.0 / 48.0)]
    [InlineData(3, 3.0 / 128.0)]
    [InlineData(7, 1.0 / 16.0)]
    [InlineData(10, 1.0 / 8.0)]
    [InlineData(13, 1.0 / 4.0)]
    [InlineData(16, 1.0 / 2.0)]
    [InlineData(19, 1.0)]
    [InlineData(20, 1.5)]
    [InlineData(24, 5.0)]
    public void WholeNotesForSubdivision_matches_the_measured_table(int index, double expected) =>
        DecentSamplerTempo.WholeNotesForSubdivision(index).Should().BeApproximately(expected, 1e-12);

    [Fact]
    public void the_table_holds_twenty_five_entries() =>
        DecentSamplerTempo.SubdivisionCount.Should().Be(25);

    [Theory]
    [InlineData(25)]
    [InlineData(30)]
    [InlineData(1000)]
    public void an_index_above_the_table_clamps_to_five_whole_notes(int index) =>
        DecentSamplerTempo.WholeNotesForSubdivision(index).Should().Be(5.0);

    [Fact]
    public void a_negative_index_clamps_to_the_first_entry() =>
        DecentSamplerTempo.WholeNotesForSubdivision(-4).Should().BeApproximately(1.0 / 96.0, 1e-12);

    [Theory]
    [InlineData(10, 0.25)]
    [InlineData(13, 0.5)]
    [InlineData(19, 2.0)]
    [InlineData(24, 10.0)]
    public void SecondsForSubdivision_at_the_standalones_own_tempo(int index, double expected) =>
        DecentSamplerTempo.SecondsForSubdivision(index, 120.0).Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void SecondsForSubdivision_follows_a_tempo_source()
    {
        //Arrange
        var tempo = new TempoSource { BeatsPerMinute = 60.0 };

        //Act
        var seconds = DecentSamplerTempo.SecondsForSubdivision(13, tempo);

        //Assert
        seconds.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void a_null_tempo_source_means_the_standalones_hundred_and_twenty() =>
        DecentSamplerTempo.SecondsForSubdivision(13, null).Should().BeApproximately(0.5, 1e-9);

    [Theory]
    [InlineData(DecentSamplerSyncDivision.NoteOneSixtyFourthTriplet, 0)]
    [InlineData(DecentSamplerSyncDivision.NoteOneEighth, 10)]
    [InlineData(DecentSamplerSyncDivision.NoteOneFourth, 13)]
    [InlineData(DecentSamplerSyncDivision.NoteWhole, 19)]
    [InlineData(DecentSamplerSyncDivision.NoteWholeDotted, 20)]
    public void an_arpeggiator_sync_division_is_a_subdivision_index(
        DecentSamplerSyncDivision division, int expected) =>
        DecentSamplerTempo.SubdivisionOf(division).Should().Be(expected);

    [Fact]
    public void BeatsForSubdivision_counts_quarter_notes() =>
        DecentSamplerTempo.BeatsForSubdivision(19).Should().Be(4.0);

    [Fact]
    public void DescribeSubdivision_names_the_note_value() =>
        DecentSamplerTempo.DescribeSubdivision(12).Should().Be("dotted 1/8");
}

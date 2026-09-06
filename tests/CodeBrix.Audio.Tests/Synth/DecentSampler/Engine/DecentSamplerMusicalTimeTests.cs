using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The musical-time subdivision table, against the twenty-five spacings measured from the reference
/// player at 120 BPM (plan section 7 item 8).
/// </summary>
public class DecentSamplerMusicalTimeTests
{
    // The measured spacings in seconds at 120 BPM, indices 0 to 24.
    private static readonly double[] MeasuredSeconds =
    [
        0.020833, 0.03125, 0.041667, 0.046875, 0.0625, 0.083333, 0.09375, 0.125, 0.166667, 0.1875,
        0.25, 0.333333, 0.375, 0.5, 0.666667, 0.75, 1.0, 1.333333, 1.5, 2.0,
        3.0, 4.0, 6.0, 8.0, 10.0,
    ];

    [Fact]
    public void the_table_holds_twenty_five_entries()
    {
        DecentSamplerMusicalTime.Count.Should().Be(25);
    }

    [Fact]
    public void every_index_matches_the_measured_spacing_at_a_hundred_and_twenty()
    {
        for (var index = 0; index < MeasuredSeconds.Length; index++)
        {
            DecentSamplerMusicalTime.SecondsAt(index, 120.0)
                .Should().BeApproximately(MeasuredSeconds[index], 0.0005);
        }
    }

    [Theory]
    [InlineData(10, 0.125)]
    [InlineData(13, 0.25)]
    [InlineData(19, 1.0)]
    [InlineData(24, 5.0)]
    public void the_four_points_the_guide_gives_are_where_it_says(int index, double wholeNotes)
    {
        DecentSamplerMusicalTime.WholeNotesAt(index).Should().BeApproximately(wholeNotes, 1e-12);
    }

    [Fact]
    public void indices_above_the_table_clamp_to_five_whole_notes()
    {
        DecentSamplerMusicalTime.WholeNotesAt(25).Should().Be(5.0);
        DecentSamplerMusicalTime.WholeNotesAt(127).Should().Be(5.0);
    }

    [Fact]
    public void a_negative_index_reads_as_the_first_entry()
    {
        DecentSamplerMusicalTime.WholeNotesAt(-3)
            .Should().Be(DecentSamplerMusicalTime.WholeNotesAt(0));
    }

    [Fact]
    public void the_spacing_scales_with_the_tempo()
    {
        DecentSamplerMusicalTime.SecondsAt(13, 60.0).Should().BeApproximately(1.0, 1e-9);
        DecentSamplerMusicalTime.SecondsAt(13, 240.0).Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void a_tempo_that_is_not_positive_gives_no_time_at_all()
    {
        DecentSamplerMusicalTime.SecondsAt(13, 0.0).Should().Be(0.0);
        DecentSamplerMusicalTime.SecondsAt(13, -20.0).Should().Be(0.0);
    }

    [Fact]
    public void beats_are_four_to_the_whole_note()
    {
        DecentSamplerMusicalTime.BeatsAt(19).Should().BeApproximately(4.0, 1e-12);
        DecentSamplerMusicalTime.BeatsAt(13).Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void indices_one_to_eighteen_run_in_straight_triplet_dotted_triples()
    {
        for (var group = 0; group <= 5; group++)
        {
            var straight = DecentSamplerMusicalTime.WholeNotesAt(3 * group + 1);
            var triplet = DecentSamplerMusicalTime.WholeNotesAt(3 * group + 2);
            var dotted = DecentSamplerMusicalTime.WholeNotesAt(3 * group + 3);

            straight.Should().BeApproximately(1.0 / 64.0 * (1 << group), 1e-12);
            triplet.Should().BeApproximately(straight * 4.0 / 3.0, 1e-12);
            dotted.Should().BeApproximately(straight * 1.5, 1e-12);
        }
    }
}

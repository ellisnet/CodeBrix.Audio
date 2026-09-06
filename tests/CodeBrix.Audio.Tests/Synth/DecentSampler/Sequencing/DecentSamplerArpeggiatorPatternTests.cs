using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Sequencing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// The nine <c>arpOrder</c> values against the developer guide's own worked examples. The guide gives
/// the chord C4-E4-G4 (60, 64, 67) with <c>arpOctaveRange="2"</c> and
/// <c>arpOctaveMode="replayPerOctave"</c> and prints one full cycle for every order; each of those
/// printed cycles is a test here.
/// </summary>
public class DecentSamplerArpeggiatorPatternTests
{
    [Fact]
    public void up_is_the_pool_from_the_bottom() =>
        Guide(DecentSamplerArpOrder.Up).Should().Equal(60, 64, 67, 72, 76, 79);

    [Fact]
    public void down_is_the_exact_reverse_of_up() =>
        Guide(DecentSamplerArpOrder.Down).Should().Equal(79, 76, 72, 67, 64, 60);

    [Fact]
    public void up_down_plays_each_turnaround_note_once() =>
        Guide(DecentSamplerArpOrder.UpDown).Should().Equal(60, 64, 67, 72, 76, 79, 76, 72, 67, 64);

    [Fact]
    public void up_down_inclusive_repeats_each_turnaround_note() =>
        Guide(DecentSamplerArpOrder.UpDownInclusive)
            .Should().Equal(60, 64, 67, 72, 76, 79, 79, 76, 72, 67, 64, 60);

    [Fact]
    public void down_up_plays_each_turnaround_note_once() =>
        Guide(DecentSamplerArpOrder.DownUp).Should().Equal(79, 76, 72, 67, 64, 60, 64, 67, 72, 76);

    [Fact]
    public void down_up_inclusive_repeats_each_turnaround_note() =>
        Guide(DecentSamplerArpOrder.DownUpInclusive)
            .Should().Equal(79, 76, 72, 67, 64, 60, 60, 64, 67, 72, 76, 79);

    [Fact]
    public void as_played_replays_the_press_order_per_octave()
    {
        //Arrange - the guide: pressed G, then C, then E.
        var held = new List<DecentSamplerArpNote>
        {
            new DecentSamplerArpNote(0, 67, 100, 0),
            new DecentSamplerArpNote(0, 60, 100, 1),
            new DecentSamplerArpNote(0, 64, 100, 2),
        };

        //Act
        var pattern = Build(held, DecentSamplerArpOrder.AsPlayed, 2,
            DecentSamplerArpOctaveMode.ReplayPerOctave, 16);

        //Assert
        pattern.Should().Equal(67, 60, 64, 79, 72, 76);
    }

    [Fact]
    public void as_played_pressed_low_to_high_is_identical_to_up() =>
        Guide(DecentSamplerArpOrder.AsPlayed).Should().Equal(Guide(DecentSamplerArpOrder.Up));

    [Fact]
    public void random_and_random_no_repeat_offer_the_whole_pool()
    {
        //Arrange
        //Act
        var random = Guide(DecentSamplerArpOrder.Random);
        var noRepeat = Guide(DecentSamplerArpOrder.RandomNoRepeat);

        //Assert - the drawing happens per step; the pattern is the pool they draw from.
        random.Should().Equal(60, 64, 67, 72, 76, 79);
        noRepeat.Should().Equal(60, 64, 67, 72, 76, 79);
    }

    [Fact]
    public void one_octave_of_range_adds_no_copies() =>
        Build(Chord(), DecentSamplerArpOrder.Up, 1, DecentSamplerArpOctaveMode.ReplayPerOctave, 16)
            .Should().Equal(60, 64, 67);

    [Fact]
    public void replay_per_octave_repeats_the_chord_an_octave_at_a_time() =>
        Build(WideChord(), DecentSamplerArpOrder.Up, 2,
                DecentSamplerArpOctaveMode.ReplayPerOctave, 16)
            .Should().Equal(60, 75, 72, 87);

    [Fact]
    public void interleave_by_pitch_merges_the_octaves_into_one_sorted_list() =>
        Build(WideChord(), DecentSamplerArpOrder.Up, 2,
                DecentSamplerArpOctaveMode.InterleaveByPitch, 16)
            .Should().Equal(60, 72, 75, 87);

    [Fact]
    public void the_step_count_truncates_the_pool_and_never_pads() =>
        Build(Chord(), DecentSamplerArpOrder.Up, 3,
                DecentSamplerArpOctaveMode.ReplayPerOctave, 4)
            .Should().Equal(60, 64, 67, 72);

    [Fact]
    public void the_step_count_is_applied_before_the_order() =>
        Build(Chord(), DecentSamplerArpOrder.Down, 3,
                DecentSamplerArpOctaveMode.ReplayPerOctave, 4)
            .Should().Equal(72, 67, 64, 60);

    [Fact]
    public void an_octave_copy_above_the_top_of_the_keyboard_is_dropped() =>
        Build(
                [new DecentSamplerArpNote(0, 120, 100, 0)],
                DecentSamplerArpOrder.Up,
                4,
                DecentSamplerArpOctaveMode.ReplayPerOctave,
                16)
            .Should().Equal(120);

    [Fact]
    public void an_empty_chord_makes_an_empty_pattern() =>
        Build([], DecentSamplerArpOrder.Up, 2, DecentSamplerArpOctaveMode.ReplayPerOctave, 16)
            .Should().BeEmpty();

    [Fact]
    public void the_octave_range_is_clamped_to_the_documented_maximum() =>
        Build(
                [new DecentSamplerArpNote(0, 0, 100, 0)],
                DecentSamplerArpOrder.Up,
                99,
                DecentSamplerArpOctaveMode.ReplayPerOctave,
                16)
            .Should().Equal(0, 12, 24, 36, 48, 60, 72, 84);

    // The guide's own chord and settings.
    private static int[] Guide(DecentSamplerArpOrder order) =>
        Build(Chord(), order, 2, DecentSamplerArpOctaveMode.ReplayPerOctave, 16);

    private static List<DecentSamplerArpNote> Chord() =>
    [
        new DecentSamplerArpNote(0, 60, 100, 0),
        new DecentSamplerArpNote(0, 64, 100, 1),
        new DecentSamplerArpNote(0, 67, 100, 2),
    ];

    // A chord wider than an octave, which is the only shape that tells the two octave modes apart.
    private static List<DecentSamplerArpNote> WideChord() =>
    [
        new DecentSamplerArpNote(0, 60, 100, 0),
        new DecentSamplerArpNote(0, 75, 100, 1),
    ];

    private static int[] Build(
        IReadOnlyList<DecentSamplerArpNote> held,
        DecentSamplerArpOrder order,
        int octaveRange,
        DecentSamplerArpOctaveMode octaveMode,
        int stepCount)
    {
        var pool = new List<DecentSamplerArpNote>();
        var pattern = new List<DecentSamplerArpNote>();

        DecentSamplerArpeggiatorPattern.Build(
            held, order, octaveRange, octaveMode, stepCount, pool, pattern);

        return pattern.Select(note => note.Key).ToArray();
    }
}

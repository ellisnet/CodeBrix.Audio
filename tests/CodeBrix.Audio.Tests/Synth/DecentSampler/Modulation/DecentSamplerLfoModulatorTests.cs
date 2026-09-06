using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// The <c>&lt;lfo&gt;</c> modulator: its four shapes, its rate in hertz and in musical time, its
/// delay, and what its trigger does on a note-on.
/// </summary>
/// <remarks>
/// Every LFO here is global in scope and bound to <c>GROUP_VOLUME</c> with <c>modBehavior="set"</c>
/// and an output range of 0 to 1, so the group's volume IS the LFO's output and the trace reads the
/// oscillator directly.
/// </remarks>
public class DecentSamplerLfoModulatorTests
{
    private const string VolumeBinding =
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />";

    [Fact]
    public void a_sine_starts_at_its_centre_and_reaches_both_extremes()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(0.5, 0.001);
        ModulationHarness.Max(trace).Should().BeApproximately(1.0, 0.01);
        ModulationHarness.Min(trace).Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void the_frequency_in_hertz_is_cycles_per_second()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"3\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(2.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        ModulationHarness.RisingCrossings(trace, 0.5).Should().Be(6);
    }

    [Theory]
    [InlineData(120.0, 2)]
    [InlineData(60.0, 1)]
    public void a_musical_time_frequency_is_a_subdivision_that_follows_the_tempo(
        double beatsPerMinute, int expectedCyclesPerSecond)
    {
        //Arrange
        // Index 13 is a quarter note, so the LFO's PERIOD is one beat: two cycles a second at 120 BPM
        // and one at 60.
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequencyFormat=\"musical_time\" frequency=\"13\">" +
            VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();
        synthesizer.TempoSource.BeatsPerMinute = beatsPerMinute;

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(2.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        ModulationHarness.RisingCrossings(trace, 0.5).Should().Be(expectedCyclesPerSecond * 2);
    }

    [Fact]
    public void a_square_holds_each_half_of_its_cycle()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"square\" frequency=\"1\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace.Should().AllSatisfy(value =>
            Math.Min(Math.Abs(value), Math.Abs(value - 1.0)).Should().BeLessThan(0.001));
        trace[0].Should().BeApproximately(1.0, 0.001);
        trace[trace.Length - 1].Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void a_saw_ramps_from_its_minimum_to_its_maximum_across_the_cycle()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"saw\" frequency=\"1\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(0.0, 0.005);
        trace[trace.Length / 2].Should().BeApproximately(0.5, 0.01);
        trace[trace.Length - 1].Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void a_triangle_starts_at_its_centre_and_turns_at_the_quarter_points()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"triangle\" frequency=\"1\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();
        var blocks = ModulationHarness.Blocks(1.0);

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, blocks, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(0.5, 0.005);
        trace[blocks / 4].Should().BeApproximately(1.0, 0.01);
        trace[blocks / 2].Should().BeApproximately(0.5, 0.01);
        trace[(blocks * 3) / 4].Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void delayTime_leaves_the_target_alone_until_it_has_run_out()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"8\" delayTime=\"0.25\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();
        var delayBlocks = ModulationHarness.Blocks(0.25);

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // Nothing is contributed at all during the delay, so the group keeps the volume the preset
        // declared rather than being pulled to the bottom of the binding's output range.
        for (var block = 0; block < delayBlocks - 1; block++)
        {
            trace[block].Should().Be(1.0);
        }

        ModulationHarness.Min(trace).Should().BeLessThan(0.05);
    }

    [Fact]
    public void trigger_attack_restarts_the_phase_on_a_note_on()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"1\" trigger=\"attack\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();
        var quarter = ModulationHarness.Blocks(0.25);

        //Act
        var before = ModulationHarness.Trace(
            synthesizer, quarter, () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOn(0, 60, 100);

        var after = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        before[before.Length - 1].Should().BeApproximately(1.0, 0.01);
        after[0].Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void trigger_none_leaves_a_running_lfo_alone_on_a_note_on()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"1\" trigger=\"none\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();
        var quarter = ModulationHarness.Blocks(0.25);

        //Act
        var before = ModulationHarness.Trace(
            synthesizer, quarter, () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOn(0, 60, 100);

        var after = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        before[before.Length - 1].Should().BeApproximately(1.0, 0.01);
        after[0].Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void modAmount_scales_how_far_the_modulator_moves_the_target()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\" modAmount=\"0.5\">" + VolumeBinding + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // MEASURED: modAmount SCALES the translated value, so `set` at half depth halves the LFO's
        // whole swing rather than pulling it toward the base value of 1.
        ModulationHarness.Max(trace).Should().BeApproximately(0.5, 0.01);
        ModulationHarness.Min(trace).Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void a_MOD_AMOUNT_binding_changes_the_depth_while_the_modulator_runs()
    {
        //Arrange
        const string ui =
            "<ui><tab><labeled_knob x=\"0\" y=\"0\" width=\"10\" parameterName=\"Depth\" " +
            "minValue=\"0\" maxValue=\"1\" value=\"1\">" +
            "<binding type=\"modulator\" level=\"instrument\" modulatorIndex=\"0\" " +
            "parameter=\"MOD_AMOUNT\" translation=\"linear\" /></labeled_knob></tab></ui>";

        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\">" + VolumeBinding + "</lfo>", extraSections: ui);
        var synthesizer = harness.Synthesizer();

        //Act
        var full = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        harness.Instrument.Controls[0].SetValue(0.5);

        var halved = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        ModulationHarness.Max(full).Should().BeApproximately(1.0, 0.01);
        ModulationHarness.Max(halved).Should().BeApproximately(0.5, 0.01);
        ModulationHarness.Min(full).Should().BeApproximately(0.0, 0.01);
        ModulationHarness.Min(halved).Should().BeApproximately(0.0, 0.01);
    }
}

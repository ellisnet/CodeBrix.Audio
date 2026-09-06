using System;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// A modulator's own parameters, driven by bindings: <c>type="modulator"</c> at
/// <c>level="instrument"</c>, addressed by index or by tag.
/// </summary>
public class DecentSamplerModulatorParameterTests
{
    private const string VolumeBinding =
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />";

    [Fact]
    public void a_FREQUENCY_binding_changes_an_lfos_rate()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"1\">" + VolumeBinding + "</lfo>",
            extraSections: Knob("Rate", "FREQUENCY", 0, 20, 1));
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].SetValue(6.0);
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        ModulationHarness.RisingCrossings(trace, 0.5).Should().Be(6);
    }

    [Fact]
    public void a_SHAPE_binding_changes_an_lfos_waveform()
    {
        //Arrange
        const string menu =
            "<ui><tab><menu x=\"0\" y=\"0\" width=\"10\" height=\"10\">" +
            "<option name=\"Sine\"><binding type=\"modulator\" level=\"instrument\" " +
            "modulatorIndex=\"0\" parameter=\"SHAPE\" translation=\"fixed_value\" " +
            "translationValue=\"sine\" /></option>" +
            "<option name=\"Square\"><binding type=\"modulator\" level=\"instrument\" " +
            "modulatorIndex=\"0\" parameter=\"SHAPE\" translation=\"fixed_value\" " +
            "translationValue=\"square\" /></option>" +
            "</menu></tab></ui>";

        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"1\">" + VolumeBinding + "</lfo>", extraSections: menu);
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].Select(1);
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // A square only ever sits at one end or the other; a sine passes through everything between.
        trace.Should().AllSatisfy(value =>
            Math.Min(Math.Abs(value), Math.Abs(value - 1.0)).Should().BeLessThan(0.001));
    }

    [Fact]
    public void a_TRIGGER_binding_changes_whether_a_note_restarts_the_lfo()
    {
        //Arrange
        const string menu =
            "<ui><tab><menu x=\"0\" y=\"0\" width=\"10\" height=\"10\">" +
            "<option name=\"Free\"><binding type=\"modulator\" level=\"instrument\" " +
            "modulatorIndex=\"0\" parameter=\"TRIGGER\" translation=\"fixed_value\" " +
            "translationValue=\"none\" /></option>" +
            "<option name=\"Attack\"><binding type=\"modulator\" level=\"instrument\" " +
            "modulatorIndex=\"0\" parameter=\"TRIGGER\" translation=\"fixed_value\" " +
            "translationValue=\"attack\" /></option>" +
            "</menu></tab></ui>";

        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"1\" trigger=\"none\">" + VolumeBinding + "</lfo>",
            extraSections: menu);
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].Select(1);
        ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.25), () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOn(0, 60, 100);
        var after = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        after[0].Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void a_MOD_DELAY_TIME_binding_changes_the_delay_of_the_next_note()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" delayTime=\"0\" attack=\"0\" sustain=\"1\">" +
            VolumeBinding + "</envelope>",
            extraSections: Knob("Delay", "MOD_DELAY_TIME", 0, 2, 0));
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].SetValue(0.2);
        synthesizer.NoteOn(0, 60, 100);

        var delayed = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.15), () => harness.Instrument.Groups[0].Volume);
        var risen = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.1), () => harness.Instrument.Groups[0].Volume);

        //Assert
        delayed.Should().AllSatisfy(value => value.Should().Be(0.0));
        risen[risen.Length - 1].Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void modulatorTags_reach_every_modulator_carrying_the_tag()
    {
        //Arrange
        const string knob =
            "<ui><tab><labeled_knob x=\"0\" y=\"0\" width=\"10\" parameterName=\"Depth\" " +
            "minValue=\"0\" maxValue=\"1\" value=\"1\">" +
            "<binding type=\"modulator\" level=\"instrument\" modulatorTags=\"wobble\" " +
            "parameter=\"MOD_AMOUNT\" translation=\"linear\" /></labeled_knob></tab></ui>";

        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\" tags=\"wobble\">" + VolumeBinding + "</lfo>",
            extraSections: knob);
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].SetValue(0.25);
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // MEASURED: the depth scales the value, so a quarter depth on a `set` binding caps the LFO's
        // swing at a quarter of the output range.
        ModulationHarness.Max(trace).Should().BeApproximately(0.25, 0.01);
        ModulationHarness.Min(trace).Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void a_disabled_binding_under_a_modulator_contributes_nothing()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\">" +
            "<binding enabled=\"false\" type=\"amp\" level=\"group\" position=\"0\" " +
            "parameter=\"GROUP_VOLUME\" modBehavior=\"set\" translation=\"linear\" " +
            "translationOutputMin=\"0\" translationOutputMax=\"1\" /></lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace.Should().AllSatisfy(value => value.Should().Be(1.0));
    }

    [Fact]
    public void a_modulator_with_no_bindings_is_reported_rather_than_ignored()
    {
        //Arrange
        using var harness = ModulationHarness.Load("<lfo shape=\"sine\" frequency=\"4\" />");

        //Act
        var synthesizer = harness.Synthesizer();

        //Assert
        synthesizer.Problems.Should().Contain(problem => problem.Contains("modulates nothing"));
    }

    [Fact]
    public void resetting_the_synthesizer_takes_every_contribution_back_out()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiCC number=\"1\" channel=\"1\" scope=\"global\">" + VolumeBinding + "</midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, 0);
        var modulated = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume)[0];

        synthesizer.Reset();
        var afterReset = harness.Instrument.Groups[0].Volume;

        //Assert
        modulated.Should().Be(0.0);
        afterReset.Should().Be(1.0);
    }

    private static string Knob(string name, string parameter, double minimum, double maximum, double value) =>
        "<ui><tab><labeled_knob x=\"0\" y=\"0\" width=\"10\" parameterName=\"" + name +
        "\" minValue=\"" + minimum.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "\" maxValue=\"" + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "\" value=\"" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\">" +
        "<binding type=\"modulator\" level=\"instrument\" modulatorIndex=\"0\" parameter=\"" +
        parameter + "\" translation=\"linear\" /></labeled_knob></tab></ui>";
}

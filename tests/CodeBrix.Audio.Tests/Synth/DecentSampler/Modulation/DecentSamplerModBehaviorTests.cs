using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// The four <c>modBehavior</c> values, measured as the effective value a target ends up with.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED against the reference player (measurement round 2, item 23). With a base value <c>b</c>,
/// a translated modulator value <c>v</c>, a depth <c>a</c> (<c>modAmount</c>) and <c>u = a * v</c>:
/// <c>set</c> is <c>u</c>, <c>add</c> is <c>b + u</c>, <c>multiply</c> is <c>b * u</c>, and
/// <c>modulate</c> is <c>b + u - a * n</c> where <c>n</c> is the translation of the modulator's own
/// RESTING raw output - zero for a <c>&lt;midiCC&gt;</c> and 0.5 for an <c>&lt;lfo&gt;</c>.
/// </para>
/// <para>
/// The depth SCALES the translated value; it is not a blend toward the base, so <c>modAmount="0"</c>
/// silences a <c>set</c> or a <c>multiply</c> rather than leaving the base alone. The numbers in the
/// theories below are the reference player's own, read off the item's tables.
/// </para>
/// </remarks>
public class DecentSamplerModBehaviorTests
{
    // The measurement's own preset: a group declared at volume 0.5 with a <midiCC number="20">
    // driven to 0, 64 and 127, bound to the group's volume over an output range of 0 to 1.
    private const double Base = 0.5;

    [Theory]
    [InlineData("set", 1.0, 0, 0.0)]
    [InlineData("set", 1.0, 64, 0.50394)]
    [InlineData("set", 1.0, 127, 1.0)]
    [InlineData("set", 0.5, 0, 0.0)]
    [InlineData("set", 0.5, 64, 0.25197)]
    [InlineData("set", 0.5, 127, 0.5)]
    [InlineData("add", 1.0, 0, 0.5)]
    [InlineData("add", 1.0, 64, 1.00394)]
    [InlineData("add", 1.0, 127, 1.5)]
    [InlineData("add", 0.5, 0, 0.5)]
    [InlineData("add", 0.5, 64, 0.75197)]
    [InlineData("add", 0.5, 127, 1.0)]
    [InlineData("modulate", 1.0, 0, 0.5)]
    [InlineData("modulate", 1.0, 64, 1.00394)]
    [InlineData("modulate", 1.0, 127, 1.5)]
    [InlineData("modulate", 0.5, 0, 0.5)]
    [InlineData("modulate", 0.5, 64, 0.75197)]
    [InlineData("modulate", 0.5, 127, 1.0)]
    [InlineData("multiply", 1.0, 0, 0.0)]
    [InlineData("multiply", 1.0, 64, 0.25197)]
    [InlineData("multiply", 1.0, 127, 0.5)]
    [InlineData("multiply", 0.5, 0, 0.0)]
    [InlineData("multiply", 0.5, 64, 0.12599)]
    [InlineData("multiply", 0.5, 127, 0.25)]
    public void each_behaviour_matches_the_measured_reference_table(
        string behavior, double depth, int controller, double expected)
    {
        //Arrange
        using var harness = Harness(behavior, depth.ToString("0.0###"));
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 20, controller);
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(expected, 0.0005);
    }

    [Theory]
    [InlineData("add", Base)]
    [InlineData("modulate", Base)]
    [InlineData("set", 0.0)]
    [InlineData("multiply", 0.0)]
    public void a_depth_of_zero_scales_the_value_away_rather_than_blending_toward_the_base(
        string behavior, double expected)
    {
        //Arrange
        using var harness = Harness(behavior, depth: "0");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 20, 127);
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(expected, 1.0E-9);
    }

    [Theory]
    [InlineData("modulate", 0, 1000.0)]
    [InlineData("modulate", 64, 4930.7)]
    [InlineData("modulate", 127, 8800.0)]
    [InlineData("add", 0, 1200.0)]
    [InlineData("add", 64, 5130.7)]
    [InlineData("add", 127, 9000.0)]
    public void a_controllers_neutral_point_is_its_translation_of_zero(
        string behavior, int controller, double expected)
    {
        //Arrange
        // The measurement's second target: a lowpass at 1000 Hz with the controller translated over
        // 200 to 8000 Hz, so a neutral of translationOutputMin can be told from a neutral of zero and
        // from the middle of the output range.
        using var harness = FilterHarness(behavior);
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 20, controller);
        var trace = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Effects.Effects[0].Frequency ?? 0.0);

        //Assert
        trace[0].Should().BeApproximately(expected, 1.0);
    }

    [Theory]
    [InlineData("set", 0.5)]
    [InlineData("add", 1.0)]
    [InlineData("modulate", 0.5)]
    [InlineData("multiply", 0.25)]
    public void an_lfo_rests_at_half_scale_so_modulate_leaves_the_base_alone(
        string behavior, double expected)
    {
        //Arrange
        // The measurement drove a 0.001 Hz sine LFO, which sits at its resting output for the whole
        // recording, and read set 0.5029, add 1.0029, multiply 0.2515 and modulate 0.5029.
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"0.001\" modAmount=\"1.0\" scope=\"global\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"" + behavior + "\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></lfo>",
            groupAttributes: "volume=\"0.5\"");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(expected, 0.005);
    }

    [Fact]
    public void a_binding_of_its_own_overrides_the_modulators_depth()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiCC number=\"20\" channel=\"1\" scope=\"global\" modAmount=\"1.0\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"set\" modAmount=\"0.25\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></midiCC>",
            groupAttributes: "volume=\"0.5\"");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 20, 127);
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        // set at a quarter depth is a quarter of the translated value, not a quarter of the way from
        // the base toward it.
        trace[0].Should().BeApproximately(0.25, 0.0005);
    }

    private static ModulationHarness Harness(string behavior, string depth)
    {
        var amount = depth == null ? string.Empty : " modAmount=\"" + depth + "\"";

        return ModulationHarness.Load(
            "<midiCC number=\"20\" channel=\"1\" scope=\"global\"" + amount + ">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"" + behavior + "\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></midiCC>",
            groupAttributes: "volume=\"0.5\"");
    }

    private static ModulationHarness FilterHarness(string behavior) =>
        ModulationHarness.Load(
            "<midiCC number=\"20\" channel=\"1\" scope=\"global\" modAmount=\"1.0\">" +
            "<binding type=\"effect\" level=\"instrument\" effectIndex=\"0\" " +
            "parameter=\"FX_FILTER_FREQUENCY\" modBehavior=\"" + behavior + "\" translation=\"linear\" " +
            "translationOutputMin=\"200\" translationOutputMax=\"8000\" /></midiCC>",
            extraSections:
            "<effects><effect type=\"lowpass\" frequency=\"1000\" resonance=\"0.7\" /></effects>");
}

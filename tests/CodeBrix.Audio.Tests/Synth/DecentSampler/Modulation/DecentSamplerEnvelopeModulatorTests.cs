using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// The <c>&lt;envelope&gt;</c> modulator: its four segments, its curves, its delay, and the way a
/// global-scope one is gated by the keyboard as a whole rather than by one note.
/// </summary>
public class DecentSamplerEnvelopeModulatorTests
{
    private const string VolumeBinding =
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />";

    [Fact]
    public void an_envelope_that_has_not_been_played_contributes_nothing()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.1\" sustain=\"0.5\">" + VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.2), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace.Should().AllSatisfy(value => value.Should().Be(1.0));
    }

    [Fact]
    public void the_four_segments_run_in_order()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.1\" decay=\"0.1\" sustain=\"0.5\" release=\"0.2\" " +
            "attackCurve=\"0\" decayCurve=\"0\" releaseCurve=\"0\">" + VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);

        var attack = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.05), () => harness.Instrument.Groups[0].Volume);
        var peak = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.05), () => harness.Instrument.Groups[0].Volume);
        var decay = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.05), () => harness.Instrument.Groups[0].Volume);
        var sustain = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.2), () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOff(0, 60);

        var release = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.1), () => harness.Instrument.Groups[0].Volume);
        var finished = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.2), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // A linear attack is halfway up at half its length, and the peak is reached at its end.
        attack[attack.Length - 1].Should().BeApproximately(0.5, 0.02);
        ModulationHarness.Max(peak).Should().BeApproximately(1.0, 0.02);
        decay[decay.Length - 1].Should().BeApproximately(0.75, 0.02);
        sustain[sustain.Length - 1].Should().BeApproximately(0.5, 0.001);
        release[release.Length - 1].Should().BeApproximately(0.25, 0.02);
        finished[finished.Length - 1].Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void the_attack_curve_bends_the_rise()
    {
        //Arrange
        using var linear = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.2\" sustain=\"1\" attackCurve=\"0\">" +
            VolumeBinding + "</envelope>");
        using var logarithmic = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.2\" sustain=\"1\" attackCurve=\"-100\">" +
            VolumeBinding + "</envelope>");

        var linearSynth = linear.Synthesizer();
        var logarithmicSynth = logarithmic.Synthesizer();

        //Act
        linearSynth.NoteOn(0, 60, 100);
        logarithmicSynth.NoteOn(0, 60, 100);

        var linearTrace = ModulationHarness.Trace(
            linearSynth, ModulationHarness.Blocks(0.1), () => linear.Instrument.Groups[0].Volume);
        var logarithmicTrace = ModulationHarness.Trace(
            logarithmicSynth, ModulationHarness.Blocks(0.1),
            () => logarithmic.Instrument.Groups[0].Volume);

        //Assert
        // The default attack curve is logarithmic: fast at first, so it is well ahead of the straight
        // line halfway through.
        linearTrace[linearTrace.Length - 1].Should().BeApproximately(0.5, 0.02);
        logarithmicTrace[logarithmicTrace.Length - 1].Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void delayTime_holds_the_envelope_at_zero_before_it_starts()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" delayTime=\"0.1\" attack=\"0.05\" sustain=\"1\" " +
            "attackCurve=\"0\">" + VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);

        var delayed = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.09), () => harness.Instrument.Groups[0].Volume);
        var risen = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.1), () => harness.Instrument.Groups[0].Volume);

        //Assert
        delayed.Should().AllSatisfy(value => value.Should().BeApproximately(0.0, 0.001));
        risen[risen.Length - 1].Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void a_global_envelope_releases_only_when_the_last_key_comes_up()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0\" sustain=\"1\" release=\"0.2\" releaseCurve=\"0\">" +
            VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        ModulationHarness.Trace(synthesizer, 4, () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOff(0, 60);
        var oneKeyDown = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.1), () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOff(0, 64);
        var released = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.1), () => harness.Instrument.Groups[0].Volume);

        //Assert
        oneKeyDown.Should().AllSatisfy(value => value.Should().BeApproximately(1.0, 0.001));
        released[released.Length - 1].Should().BeApproximately(0.5, 0.03);
    }

    [Theory]
    // MEASURED (round 3, item 45): a two-second attack with NO curve attributes written, normalised
    // against its own peak. The reference's own model row for gain = (1 - exp(-4x)) / (1 - exp(-4)),
    // which fits its measured trace to within 0.05 over the whole stage.
    [InlineData(0.05, 0.185)]
    [InlineData(0.10, 0.336)]
    [InlineData(0.15, 0.462)]
    [InlineData(0.20, 0.561)]
    [InlineData(0.25, 0.640)]
    [InlineData(0.30, 0.712)]
    [InlineData(0.35, 0.766)]
    [InlineData(0.40, 0.813)]
    [InlineData(0.45, 0.851)]
    [InlineData(0.50, 0.881)]
    [InlineData(0.60, 0.930)]
    [InlineData(0.75, 0.968)]
    public void the_default_stage_shape_is_the_measured_exponential_approach(
        double fraction, double expected)
    {
        //Arrange - no attackCurve, so the default shape is the one under test.
        const double attack = 2.0;
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"2.0\" sustain=\"1\">" + VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var trace = ModulationHarness.Trace(
            synthesizer,
            ModulationHarness.Blocks(fraction * attack),
            () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[trace.Length - 1].Should().BeApproximately(expected, 0.01);
    }

    [Fact]
    public void the_sustain_is_a_linear_amplitude()
    {
        //Arrange
        // MEASURED (round 3, item 45): sustain="0.5" settled 6.02 dB below the envelope's own peak, so
        // the attribute is a LINEAR AMPLITUDE exactly as the group amplitude envelope's is.
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.05\" decay=\"0.05\" sustain=\"0.5\">" +
            VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var settled = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5), () => harness.Instrument.Groups[0].Volume);

        //Assert
        var sustain = settled[settled.Length - 1];
        sustain.Should().BeApproximately(0.5, 0.001);
        (20.0 * Math.Log10(sustain)).Should().BeApproximately(-6.02, 0.05);
    }

    [Fact]
    public void the_release_runs_its_own_time_and_ends_at_zero()
    {
        //Arrange
        // MEASURED (round 3, item 45): the release runs for exactly `release` seconds and ENDS AT
        // ZERO - a release="1.0" envelope was silent at note-off + 1.0 s while the group's own four
        // second release was still open, so the silence was the modulator's.
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0\" sustain=\"1\" release=\"1.0\">" +
            VolumeBinding + "</envelope>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        ModulationHarness.Trace(synthesizer, 8, () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOff(0, 60);
        var halfway = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5), () => harness.Instrument.Groups[0].Volume);
        var ended = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.52), () => harness.Instrument.Groups[0].Volume);

        //Assert - still sounding halfway through, exactly zero once the release time has run.
        halfway[halfway.Length - 1].Should().BeGreaterThan(0.0);
        ended[ended.Length - 1].Should().Be(0.0);
    }

    [Fact]
    public void the_curve_attributes_bend_the_stages_here_and_are_ignored_by_the_reference()
    {
        //Arrange
        // A PUBLISHED DIVERGENCE. MEASURED (round 3, item 45): a two-second attack at attackCurve
        // -100 and at +100 agreed to 0.1 dB at THIRTEEN sample points in the reference, so the three
        // curve attributes on an <envelope> MODULATOR are accepted and ignored there. They work here,
        // which is what the format documents and what the group AMPLITUDE envelope's own curves do in
        // the reference too (round 1, item 4).
        using var logarithmic = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"2.0\" sustain=\"1\" attackCurve=\"-100\">" +
            VolumeBinding + "</envelope>");
        using var exponential = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"2.0\" sustain=\"1\" attackCurve=\"100\">" +
            VolumeBinding + "</envelope>");

        var logarithmicSynth = logarithmic.Synthesizer();
        var exponentialSynth = exponential.Synthesizer();

        //Act - a quarter of the way through the attack, where the reference measured the two equal.
        logarithmicSynth.NoteOn(0, 60, 100);
        exponentialSynth.NoteOn(0, 60, 100);

        var fast = ModulationHarness.Trace(
            logarithmicSynth, ModulationHarness.Blocks(0.5),
            () => logarithmic.Instrument.Groups[0].Volume);
        var slow = ModulationHarness.Trace(
            exponentialSynth, ModulationHarness.Blocks(0.5),
            () => exponential.Instrument.Groups[0].Volume);

        //Assert - the measured default shape on the logarithmic side, and its mirror on the other.
        fast[fast.Length - 1].Should().BeApproximately(0.640, 0.01);
        slow[slow.Length - 1].Should().BeLessThan(0.15);
    }

    [Fact]
    public void an_ENV_ATTACK_binding_changes_the_attack_before_the_next_note()
    {
        //Arrange
        const string ui =
            "<ui><tab><labeled_knob x=\"0\" y=\"0\" width=\"10\" parameterName=\"Attack\" " +
            "minValue=\"0\" maxValue=\"2\" value=\"0.05\" triggerOnLoad=\"false\">" +
            "<binding type=\"modulator\" level=\"instrument\" modulatorIndex=\"0\" " +
            "parameter=\"ENV_ATTACK\" translation=\"linear\" /></labeled_knob></tab></ui>";

        using var harness = ModulationHarness.Load(
            "<envelope scope=\"global\" attack=\"0.05\" sustain=\"1\" attackCurve=\"0\">" +
            VolumeBinding + "</envelope>", extraSections: ui);
        var synthesizer = harness.Synthesizer();

        //Act
        harness.Instrument.Controls[0].SetValue(0.4);
        synthesizer.NoteOn(0, 60, 100);

        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.2), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // Halfway through a 0.4 s linear attack rather than long past the end of a 0.05 s one.
        trace[trace.Length - 1].Should().BeApproximately(0.5, 0.02);
    }
}

using System;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The amplitude envelope as it sounds: attack, decay, sustain and release in seconds with the measured
/// defaults, the curve attributes applied with the measured sign convention, and
/// <c>ampEnvEnabled="false"</c> as a one-shot.
/// </summary>
/// <remarks>
/// Every reading is an RMS over a window, never a peak: a peak cannot tell a fade from a steady level,
/// because the first frames of the window still carry the pre-fade signal.
/// </remarks>
public class DecentSamplerEnvelopeTests
{
    [Fact]
    public void a_linear_attack_reaches_full_level_at_the_attack_time()
    {
        //Arrange
        using var world = Build(@"attack=""0.5"" attackCurve=""0"" ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.7);

        //Assert - measured trace: 0.185 at 0.1 s, 0.383 at 0.2, 0.587 at 0.3, 0.787 at 0.4, full at 0.5.
        Level(left, 0.1).Should().BeApproximately(0.2 * 0.5, 0.006);
        Level(left, 0.2).Should().BeApproximately(0.4 * 0.5, 0.006);
        Level(left, 0.3).Should().BeApproximately(0.6 * 0.5, 0.006);
        Level(left, 0.4).Should().BeApproximately(0.8 * 0.5, 0.006);
        Level(left, 0.6).Should().BeApproximately(0.5, 0.004);
    }

    [Fact]
    public void the_attack_curve_default_is_the_concave_shape()
    {
        //Arrange - a group with no attackCurve tracks the attackCurve=-100 trace, which is measured to
        //be the concave shape.
        using var defaults = Build(@"attack=""1.0"" ampVelTrack=""0""");
        using var declared = Build(@"attack=""1.0"" attackCurve=""-100"" ampVelTrack=""0""");

        //Act
        var withDefault = Trace(defaults, 1.0);
        var withCurve = Trace(declared, 1.0);

        //Assert
        for (var i = 0; i < withDefault.Length; i++)
        {
            ((double)withDefault[i]).Should().BeApproximately(withCurve[i], 0.004);
        }

        // And it really is concave: half way through the attack the level is well past half.
        Level(withDefault, 0.5, 0.02).Should().BeGreaterThan(0.5 * 0.5 * 1.5);
    }

    [Fact]
    public void sustain_holds_a_linear_amplitude()
    {
        //Arrange
        using var world = Build(@"attack=""0"" decay=""0"" sustain=""0.25"" ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert
        Level(left, 0.2).Should().BeApproximately(0.5 * 0.25, 0.003);
    }

    [Fact]
    public void decay_falls_from_full_level_to_the_sustain_level()
    {
        //Arrange
        using var world = Build(@"attack=""0"" decay=""0.4"" sustain=""0.5"" decayCurve=""0"" ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.7);

        //Assert - a straight line from 1.0 to 0.5 over 0.4 s, then held.
        Level(left, 0.02).Should().BeApproximately(0.5 * 0.975, 0.01);
        Level(left, 0.2).Should().BeApproximately(0.5 * 0.75, 0.01);
        Level(left, 0.6).Should().BeApproximately(0.5 * 0.5, 0.005);
    }

    [Fact]
    public void a_linear_release_falls_over_the_release_time()
    {
        //Arrange
        using var world = Build(@"release=""1.0"" releaseCurve=""0"" ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.1);

        //Act
        synthesizer.NoteOff(0, 60);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 1.2);

        //Assert
        Level(left, 0.25).Should().BeApproximately(0.5 * 0.75, 0.01);
        Level(left, 0.5).Should().BeApproximately(0.5 * 0.5, 0.01);
        Level(left, 0.75).Should().BeApproximately(0.5 * 0.25, 0.01);
        Level(left, 1.1).Should().BeLessThan(0.002);
    }

    [Fact]
    public void the_undocumented_release_default_is_half_a_second()
    {
        //Arrange - a preset that declares no envelope at all.
        using var world = Build(@"ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.1);

        //Act
        synthesizer.NoteOff(0, 60);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.8);

        //Assert - measured: the default release trajectory is the releaseCurve=100 shape over 0.5 s,
        //which is 0.128 of full level a quarter of a second in and silent by 0.53 s.
        Level(left, 0.25).Should().BeApproximately(0.5 * 0.128, 0.02);
        Level(left, 0.45).Should().BeLessThan(0.5 * 0.06);
        Level(left, 0.7).Should().BeLessThan(0.001);
    }

    [Fact]
    public void an_explicit_release_of_zero_is_honoured()
    {
        //Arrange
        using var world = Build(@"release=""0"" ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.1);

        //Act
        synthesizer.NoteOff(0, 60);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert - gone within the one block the mixer's anti-pop ramp needs.
        DecentSamplerRenderProbe.Rms(left, 128, 256).Should().BeLessThan(0.001);
    }

    [Fact]
    public void the_default_release_only_applies_when_the_preset_declares_none()
    {
        //Arrange
        using var declaredAtGroups = BuildGroups(@"release=""0.05""", @"ampVelTrack=""0""");
        var synthesizer = declaredAtGroups.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.1);

        //Act
        synthesizer.NoteOff(0, 60);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert - a 50 ms release declared on <groups> wins over the half-second default.
        Level(left, 0.15).Should().BeLessThan(0.001);
        declaredAtGroups.Dispose();
    }

    [Fact]
    public void ampEnvEnabled_false_plays_the_whole_sample_and_ignores_note_off()
    {
        //Arrange - a 0.2 s sample, released after 20 ms.
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/short.wav", value: 0.5f, frames: 8820);
        var instrument = fixtures.LoadPreset("""
            <DecentSampler>
              <groups>
                <group ampEnvEnabled="false" ampVelTrack="0">
                  <sample path="Samples/short.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        using var _ = fixtures;
        using var __ = instrument;

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.02);
        synthesizer.NoteOff(0, 60);
        var (left, _2) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert - full level right up to the end of the sample data, then nothing.
        Level(left, 0.05).Should().BeApproximately(0.5, 0.004);
        Level(left, 0.15).Should().BeApproximately(0.5, 0.004);
        Level(left, 0.25).Should().BeLessThan(0.001);
    }

    [Fact]
    public void the_envelope_defaults_to_full_sustain_with_no_decay()
    {
        //Arrange
        using var world = Build(@"ampVelTrack=""0""");
        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert
        Level(left, 0.01).Should().BeApproximately(0.5, 0.004);
        Level(left, 0.4).Should().BeApproximately(0.5, 0.004);
    }

    [Fact]
    public void the_measured_release_defaults_are_named_in_one_place()
    {
        DecentSamplerDefaults.ReleaseSeconds.Should().Be(0.5);
        DecentSamplerDefaults.SustainLevel.Should().Be(1.0);
        DecentSamplerDefaults.AttackSeconds.Should().Be(0.0);
        DecentSamplerDefaults.AmpVelTrack.Should().Be(1.0);
    }

    private static double Level(float[] samples, double seconds, double window = 0.01)
    {
        var rate = DecentSamplerEngineFixtures.SampleRate;
        var length = (int)(window * rate);
        var offset = Math.Clamp((int)(seconds * rate) - length / 2, 0, samples.Length - length);

        return DecentSamplerRenderProbe.Rms(samples, offset, length);
    }

    private static float[] Trace(EnvelopeWorld world, double seconds)
    {
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, seconds);
        return left;
    }

    private static EnvelopeWorld Build(string groupAttributes) =>
        BuildGroups(string.Empty, groupAttributes);

    private static EnvelopeWorld BuildGroups(string groupsAttributes, string groupAttributes)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f, frames: 44100 * 3);

        var preset = string.Format(
            CultureInfo.InvariantCulture,
            """
            <DecentSampler>
              <groups {0}>
                <group {1}>
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """,
            groupsAttributes,
            groupAttributes);

        return new EnvelopeWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class EnvelopeWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer() =>
            DecentSamplerRenderProbe.Synthesizer(instrument);

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

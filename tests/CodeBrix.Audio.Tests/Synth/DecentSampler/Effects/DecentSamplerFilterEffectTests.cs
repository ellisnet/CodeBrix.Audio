using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The two-pole filter family against the closed-form RBJ cookbook response, which is what the
/// reference player measured as - every case inside 0.15 dB from 125 Hz to 16 kHz.
/// </summary>
public class DecentSamplerFilterEffectTests
{
    private const double Tolerance = 0.2;

    [Theory]
    [InlineData(125, -0.00)]
    [InlineData(500, -0.30)]
    [InlineData(1000, -3.10)]
    [InlineData(2000, -12.43)]
    [InlineData(4000, -24.56)]
    public void lowpass_matches_the_cookbook_response(double frequency, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass\" frequency=\"1000\" resonance=\"0.7\" />", frequency)
            .Should().BeApproximately(expected, Tolerance);

    [Fact]
    public void lowpass_4pl_is_the_same_two_pole_filter_as_lowpass()
    {
        //Arrange
        const string legacy = "<effect type=\"lowpass_4pl\" frequency=\"1000\" resonance=\"0.7\" />";
        const string modern = "<effect type=\"lowpass\" frequency=\"1000\" resonance=\"0.7\" />";

        //Act
        var legacyResponse = DecentSamplerEffectHarness.MagnitudeDecibels(legacy, 2000);
        var modernResponse = DecentSamplerEffectHarness.MagnitudeDecibels(modern, 2000);

        //Assert
        legacyResponse.Should().BeApproximately(modernResponse, 0.001);
    }

    [Fact]
    public void resonance_is_the_biquad_q_used_unscaled() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass\" frequency=\"1000\" resonance=\"5.0\" />", 1000)
            .Should().BeApproximately(13.98, Tolerance);

    [Fact]
    public void the_default_resonance_is_the_documented_point_seven() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass\" frequency=\"1000\" />", 1000)
            .Should().BeApproximately(-3.10, Tolerance);

    [Theory]
    [InlineData(250, -24.14)]
    [InlineData(1000, -3.10)]
    [InlineData(4000, -0.03)]
    public void highpass_matches_the_cookbook_response(double frequency, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"highpass\" frequency=\"1000\" resonance=\"0.7\" />", frequency)
            .Should().BeApproximately(expected, Tolerance);

    [Theory]
    [InlineData(250, -12.08)]
    [InlineData(1000, -3.10)]
    [InlineData(4000, -12.29)]
    public void bandpass_is_the_constant_skirt_form(double frequency, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"bandpass\" frequency=\"1000\" resonance=\"0.7\" />", frequency)
            .Should().BeApproximately(expected, Tolerance);

    [Fact]
    public void a_constant_skirt_bandpass_peaks_at_its_own_q() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"bandpass\" frequency=\"1000\" resonance=\"3.0\" />", 1000)
            .Should().BeApproximately(20.0 * Math.Log10(3.0), 0.3);

    [Theory]
    [InlineData(250, -0.59)]
    [InlineData(500, -2.79)]
    [InlineData(4000, -0.56)]
    public void notch_matches_the_cookbook_response(double frequency, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"notch\" frequency=\"1000\" q=\"0.7\" />", frequency)
            .Should().BeApproximately(expected, Tolerance);

    [Fact]
    public void notch_is_a_true_zero_at_its_centre() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"notch\" frequency=\"1000\" q=\"0.7\" />", 1000)
            .Should().BeLessThan(-40.0);

    [Theory]
    [InlineData("2.0", 6.02)]
    [InlineData("0.5", -6.02)]
    public void peak_gain_is_linear_and_works_above_one(string gain, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels($"<effect type=\"peak\" frequency=\"1000\" q=\"0.7\" gain=\"{gain}\" />", 1000)
            .Should().BeApproximately(expected, Tolerance);

    [Fact]
    public void a_peak_at_unity_gain_is_a_wire() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"peak\" frequency=\"1000\" q=\"0.7\" gain=\"1.0\" />", 1000)
            .Should().BeApproximately(0.0, 0.05);

    [Fact]
    public void a_frequency_of_zero_is_clamped_rather_than_thrown()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"lowpass\" frequency=\"0\" resonance=\"0.7\" />");

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, DecentSamplerEffectHarness.Sine(1000, 0.1));

        //Assert
        DecentSamplerEffectHarness.Peak(left, 0, left.Length).Should().BeLessThan(0.01);
    }

    [Fact]
    public void a_disabled_filter_is_a_bypass()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"lowpass\" frequency=\"200\" resonance=\"0.7\" />");
        effect.Enabled = false;
        var input = DecentSamplerEffectHarness.Sine(4000, 0.2);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        element.Enabled.Should().BeFalse();
        DecentSamplerEffectHarness.Rms(left, 1000, 4000)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 1000, 4000), 1.0e-6);
    }

    [Fact]
    public void a_live_frequency_change_takes_effect_inside_a_block()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"lowpass\" frequency=\"20000\" resonance=\"0.7\" />");
        var input = DecentSamplerEffectHarness.Sine(4000, 0.4);

        var (before, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Act - what a binding does: it writes the new value onto the parsed element.
        effect.TrySetParameter("FX_FILTER_FREQUENCY", 200.0);
        var (after, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        element.Frequency.Should().Be(200.0);
        DecentSamplerEffectHarness.Rms(after, 4410, 4410)
            .Should().BeLessThan(DecentSamplerEffectHarness.Rms(before, 4410, 4410) / 10.0);
    }

    [Fact]
    public void an_effect_only_answers_for_the_parameters_its_type_has()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build("<effect type=\"lowpass\" frequency=\"1000\" />");

        //Act
        var known = effect.TryGetParameter("FX_FILTER_FREQUENCY", out var frequency);
        var unknown = effect.TryGetParameter("FX_REVERB_DAMPING", out _);

        //Assert
        known.Should().BeTrue();
        frequency.Should().Be(1000.0);
        unknown.Should().BeFalse();
    }
}

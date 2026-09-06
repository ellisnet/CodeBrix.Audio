using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// lowpass_1pl: the one-pole smoother, measured as y += a * (x - y) with a = 1 - exp(-2*pi*fc/sr).
/// </summary>
public class DecentSamplerOnePoleLowpassEffectTests
{
    [Fact]
    public void it_reads_three_decibels_down_at_its_own_frequency() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass_1pl\" frequency=\"1000\" />", 1000)
            .Should().BeApproximately(-3.04, 0.2);

    [Theory]
    [InlineData(250, -0.27)]
    [InlineData(2000, -6.99)]
    [InlineData(8000, -17.68)]
    public void its_response_follows_the_one_pole_law(double frequency, double expected) =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass_1pl\" frequency=\"1000\" />", frequency)
            .Should().BeApproximately(expected, 0.2);

    [Fact]
    public void high_up_it_follows_the_one_pole_law_rather_than_a_clean_slope()
    {
        //Arrange - the closed form of y += a * (x - y): |H| = a / |1 - (1-a) exp(-jw)|.
        var pole = Math.Exp(-2.0 * Math.PI * 1000.0 / DecentSamplerEffectHarness.SampleRate);
        var a = 1.0 - pole;
        var w = 2.0 * Math.PI * 16000.0 / DecentSamplerEffectHarness.SampleRate;
        var expected = 20.0 * Math.Log10(a / Math.Sqrt(1.0 - 2.0 * pole * Math.Cos(w) + pole * pole));

        //Act
        var measured = DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"lowpass_1pl\" frequency=\"1000\" />", 16000);

        //Assert - the reference measured -22.13 dB here, four octaves up from a filter whose slope
        //would be 24 dB per octave if it were a clean one.
        measured.Should().BeApproximately(expected, 0.2);
        measured.Should().BeApproximately(-22.13, 0.3);
    }

    [Fact]
    public void a_step_reaches_one_time_constant_when_the_law_says_it_should()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build("<effect type=\"lowpass_1pl\" frequency=\"100\" />");
        var step = DecentSamplerEffectHarness.Step(DecentSamplerEffectHarness.SampleRate / 2);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, step);

        //Assert - one time constant is 1/(2*pi*fc) seconds, where the step has reached 1 - 1/e.
        var atTimeConstant = (int)(DecentSamplerEffectHarness.SampleRate / (2.0 * Math.PI * 100.0));
        left[atTimeConstant].Should().BeApproximately(1.0f - (float)(1.0 / Math.E), 0.01f);
    }
}

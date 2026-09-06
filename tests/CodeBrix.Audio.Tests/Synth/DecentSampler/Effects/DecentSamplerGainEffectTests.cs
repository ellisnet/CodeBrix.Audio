using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The gain effect, whose level is decibels by default and linear with <c>levelUnit="linear"</c>.
/// </summary>
public class DecentSamplerGainEffectTests
{
    [Fact]
    public void level_is_decibels_by_default() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"gain\" level=\"-6\" />", 1000)
            .Should().BeApproximately(-6.0, 0.02);

    [Fact]
    public void level_is_linear_when_the_unit_says_so() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"gain\" levelUnit=\"linear\" level=\"0.5\" />", 1000)
            .Should().BeApproximately(-6.02, 0.02);

    [Fact]
    public void the_unit_is_honoured_whichever_order_the_attributes_come_in() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"gain\" level=\"2.0\" levelUnit=\"linear\" />", 1000)
            .Should().BeApproximately(6.02, 0.02);

    [Fact]
    public void no_level_at_all_is_unity() =>
        DecentSamplerEffectHarness
            .MagnitudeDecibels("<effect type=\"gain\" />", 1000)
            .Should().BeApproximately(0.0, 0.001);

    [Fact]
    public void the_level_is_live()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build("<effect type=\"gain\" level=\"0\" />");
        var input = DecentSamplerEffectHarness.Step(DecentSamplerEffectHarness.BlockSize * 4, 0.5f);

        //Act
        effect.TrySetParameter("LEVEL", -12.0);
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        left[0].Should().BeApproximately(0.5f * 0.251188f, 1.0e-5f);
    }
}

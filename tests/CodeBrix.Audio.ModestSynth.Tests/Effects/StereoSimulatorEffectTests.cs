using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="StereoSimulatorEffect" />.
/// </summary>
/// <remarks>
/// Both measurements the reference player supplied are checked here: the mono sum falls 6.02 dB at
/// <c>width="0.5"</c> for every algorithm, and the three algorithms decorrelate in the order
/// lauridsen, adt, schroeder. The correlation figures are taken on a 440 Hz sine because that is
/// what the reference was measured with, and a delayed sine is not decorrelated the way delayed
/// noise is.
/// </remarks>
public class StereoSimulatorEffectTests
{
    private const int Skip = 4800;
    private const int Window = 38400;

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        StereoSimulatorEffect effect = new StereoSimulatorEffect();

        //Assert
        effect.Algorithm.Should().Be(ModestStereoAlgorithm.Adt);
        effect.Width.Should().Be(0.5);
        effect.DelayTime.Should().Be(0.005);
        effect.ModRate.Should().Be(0.5);
        effect.ModDepth.Should().Be(0.3);
    }

    [Theory]
    [InlineData(ModestStereoAlgorithm.Lauridsen)]
    [InlineData(ModestStereoAlgorithm.Schroeder)]
    [InlineData(ModestStereoAlgorithm.Adt)]
    public void Width_of_one_half_puts_the_mono_sum_six_decibels_down(ModestStereoAlgorithm algorithm)
    {
        //Arrange
        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 12345u, 0.25);

        //Act
        float[] left;
        float[] right;
        StereoSimulatorEffect effect = new StereoSimulatorEffect { Algorithm = algorithm, Width = 0.5 };
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        float[] sum = EffectSignals.MonoSum(left, right);
        double change = EffectSignals.RmsDb(sum, Skip, Window) - EffectSignals.RmsDb(source, Skip, Window);
        change.Should().BeApproximately(-6.02, 0.25);
    }

    [Fact]
    public void The_three_algorithms_decorrelate_in_the_reference_players_order()
    {
        //Act
        double lauridsen = CorrelationOnSine(ModestStereoAlgorithm.Lauridsen);
        double adt = CorrelationOnSine(ModestStereoAlgorithm.Adt);
        double schroeder = CorrelationOnSine(ModestStereoAlgorithm.Schroeder);

        //Assert
        lauridsen.Should().BeApproximately(0.0, 0.05);
        adt.Should().BeApproximately(0.606, 0.08);
        schroeder.Should().BeGreaterThan(adt);
        schroeder.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Width_of_zero_leaves_a_mono_signal_alone()
    {
        //Arrange
        StereoSimulatorEffect effect = new StereoSimulatorEffect { Width = 0.0 };
        float[] source = EffectSignals.Noise(4096, 17u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
        right.Should().Equal(source);
    }

    [Fact]
    public void Width_of_one_removes_the_middle_entirely()
    {
        //Arrange
        StereoSimulatorEffect effect = new StereoSimulatorEffect
        {
            Algorithm = ModestStereoAlgorithm.Lauridsen, Width = 1.0,
        };

        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 23u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        float[] sum = EffectSignals.MonoSum(left, right);
        EffectSignals.RmsDb(sum, Skip, Window).Should().BeLessThan(-100.0);
    }

    [Fact]
    public void A_stereo_input_is_summed_to_mono_first()
    {
        //Arrange
        StereoSimulatorEffect effect = new StereoSimulatorEffect { Width = 0.0 };
        effect.Prepare(EffectSignals.SampleRate);

        float[] left = EffectSignals.Noise(2048, 31u, 0.5);
        float[] right = EffectSignals.Noise(2048, 37u, 0.5);
        float[] expected = EffectSignals.MonoSum(left, right);

        //Act
        EffectSignals.Render(effect, left, right);

        //Assert
        left.Should().Equal(expected);
        right.Should().Equal(expected);
    }

    [Fact]
    public void Algorithm_can_be_set_by_the_name_a_preset_writes()
    {
        //Arrange
        StereoSimulatorEffect effect = new StereoSimulatorEffect();

        //Act
        bool set = effect.TrySetParameter("FX_ALGORITHM", "schroeder");

        //Assert
        set.Should().BeTrue();
        effect.Algorithm.Should().Be(ModestStereoAlgorithm.Schroeder);
    }

    private static double CorrelationOnSine(ModestStereoAlgorithm algorithm)
    {
        StereoSimulatorEffect effect = new StereoSimulatorEffect { Algorithm = algorithm, Width = 0.5 };
        float[] source = EffectSignals.Sine(EffectSignals.SampleRate * 2, 440.0, 0.5);

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        return EffectSignals.Correlation(left, right, Skip, EffectSignals.SampleRate);
    }
}

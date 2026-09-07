using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="BitCrusherEffect" />.
/// </summary>
public class BitCrusherEffectTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect();

        //Assert
        effect.BitDepth.Should().Be(24.0);
        effect.SampleRateReduction.Should().Be(1.0);
        effect.Mix.Should().Be(1.0);
    }

    [Fact]
    public void The_defaults_leave_the_signal_where_a_float_cannot_tell()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect();
        float[] source = EffectSignals.Noise(4096, 41u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        for (int i = 0; i < source.Length; i++)
        {
            ((double)left[i]).Should().BeApproximately(source[i], 1.0e-6);
        }
    }

    [Fact]
    public void BitDepth_rounds_every_sample_to_one_of_its_levels()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect { BitDepth = 4.0, Mix = 1.0 };
        float[] source = EffectSignals.Sine(4096, 300.0, 0.9);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        // MEASURED (round 4, item 55): STEP = 2*sqrt(2) / 2^(bitDepth-1), read off the reference's
        // output staircase at bit depths 3, 4 and 8 to within 0.2 %.
        double step = BitCrusherEffect.FullScale / Math.Pow(2.0, 3.0);
        for (int i = 0; i < left.Length; i++)
        {
            double levels = left[i] / step;
            levels.Should().BeApproximately(Math.Round(levels), 1.0e-4);
            Math.Abs(left[i] - source[i]).Should().BeLessThan((float)(step * 0.51));
        }
    }

    [Fact]
    public void SampleRateReduction_holds_each_sample_for_that_many()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect { SampleRateReduction = 4.0, Mix = 1.0 };
        float[] source = new float[64];
        for (int i = 0; i < source.Length; i++) { source[i] = i / 128.0f; }

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        for (int i = 0; i < source.Length; i++)
        {
            ((double)left[i]).Should().BeApproximately(source[(i / 4) * 4], 1.0e-6);
        }
    }

    [Fact]
    public void SampleRateReduction_drops_the_spectral_centroid()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect { SampleRateReduction = 8.0, Mix = 1.0 };
        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 12345u, 0.25);
        double dry = Spectrum.PowerCentroid(source, source.Length);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double reduced = Spectrum.PowerCentroid(left, left.Length);
        (reduced / dry).Should().BeInRange(0.1, 0.4);
    }

    [Fact]
    public void The_step_is_two_root_two_over_the_levels_at_every_bit_depth()
    {
        //Arrange
        // MEASURED (round 4, item 55): the reference's internal step read 0.70846, 0.35431 and
        // 0.022087 at bit depths 3, 4 and 8, against 2*sqrt(2)/2^(bitDepth-1) = 0.70711, 0.35355 and
        // 0.022097. Zero is one of the levels in every case, so the quantiser is MID-TREAD.
        double[] measured = [0.70846, 0.35431, 0.022087];
        double[] depths = [3.0, 4.0, 8.0];

        //Assert
        for (int i = 0; i < depths.Length; i++)
        {
            double step = BitCrusherEffect.FullScale / Math.Pow(2.0, depths[i] - 1.0);
            (step / measured[i]).Should().BeApproximately(1.0, 0.003);
        }
    }

    [Theory]
    [InlineData(4.0, 0.0447, -400.0)]
    [InlineData(4.0, 0.1414, -400.0)]
    [InlineData(4.0, 0.3548, 1.21)]
    [InlineData(4.0, 0.7079, 0.44)]
    [InlineData(8.0, 0.0447, 0.40)]
    [InlineData(8.0, 0.1414, -0.12)]
    [InlineData(8.0, 0.3548, 0.01)]
    [InlineData(8.0, 0.7079, 0.00)]
    [InlineData(12.0, 0.0447, -0.02)]
    [InlineData(12.0, 0.1414, -0.01)]
    [InlineData(12.0, 0.3548, -0.01)]
    [InlineData(12.0, 0.7079, -0.01)]
    public void The_level_sweep_reproduces_the_measured_table(
        double bitDepth, double peak, double expectedChangeDb)
    {
        //Arrange
        // MEASURED (round 4, item 55): the level sweep of a 440 Hz sine at peaks 0.0447, 0.1414,
        // 0.3548 and 0.7079, against the dry signal at the SAME level. The apparent gain is not a
        // constant - it is what the step does to a signal spanning three levels, then five, then
        // many - and at bitDepth 4 a signal whose peak is below STEP/2 = 0.1768 becomes DIGITAL
        // SILENCE, which -400 dB stands for here.
        BitCrusherEffect effect = new BitCrusherEffect { BitDepth = bitDepth, Mix = 1.0 };
        float[] source = EffectSignals.Sine(EffectSignals.SampleRate, 440.0, peak);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        if (expectedChangeDb <= -400.0)
        {
            left.Should().AllSatisfy(sample => sample.Should().Be(0f));
            return;
        }

        double change = EffectSignals.RmsDb(left, 0, left.Length)
            - EffectSignals.RmsDb(source, 0, source.Length);
        change.Should().BeApproximately(expectedChangeDb, 0.05);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        BitCrusherEffect effect = new BitCrusherEffect { BitDepth = 2.0, SampleRateReduction = 16.0, Mix = 0.0 };
        float[] source = EffectSignals.Noise(2048, 43u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }
}

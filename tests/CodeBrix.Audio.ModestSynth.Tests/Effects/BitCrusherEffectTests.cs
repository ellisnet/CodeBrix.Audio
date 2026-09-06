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
        double step = 1.0 / Math.Pow(2.0, 3.0);
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
    public void The_measured_level_change_at_four_bits_is_the_published_divergence()
    {
        //Arrange
        // The reference player LOST 3.1 dB here. A rounding quantiser loses nothing, and the
        // measurement was one setting at medium confidence with an unknown noise distribution, so
        // the textbook quantiser stands and the divergence is stated.
        BitCrusherEffect effect = new BitCrusherEffect { BitDepth = 4.0, Mix = 1.0 };
        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 12345u, 0.25);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double change = EffectSignals.RmsDb(left, 0, left.Length)
            - EffectSignals.RmsDb(source, 0, source.Length);
        change.Should().BeApproximately(0.5, 0.5);
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

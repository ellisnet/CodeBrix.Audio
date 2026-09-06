using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="WaveFolderEffect" />, against the closed form round 3 item 47 measured:
/// with <c>k = drive / (2*sqrt(2)*threshold)</c>, <c>out = foldOnce(in * k) / k</c> where
/// <c>foldOnce</c> is the identity inside -1..1 and <c>sign(u) * (2 - |u|)</c> outside it.
/// </summary>
public class WaveFolderEffectTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        WaveFolderEffect effect = new WaveFolderEffect();

        //Assert
        effect.Drive.Should().Be(1.0);
        effect.Threshold.Should().Be(0.25);
        effect.Mix.Should().Be(1.0);
    }

    [Fact]
    public void Process_leaves_a_signal_below_the_threshold_untouched()
    {
        //Arrange
        WaveFolderEffect effect = new WaveFolderEffect { Drive = 1.0, Threshold = 0.25, Mix = 1.0 };
        float[] source = EffectSignals.Sine(2048, 500.0, 0.2);

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
    public void Process_reflects_a_signal_above_the_fold_point_back_down()
    {
        //Arrange
        // MEASURED: the fold point is 1/k, which at drive 1 and threshold 0.25 is 0.7071, and the
        // reflection takes an input of 0.9 to 0.5142 - not to 0.25.
        WaveFolderEffect effect = new WaveFolderEffect { Drive = 1.0, Threshold = 0.25, Mix = 1.0 };
        float[] source = EffectSignals.Sine(4096, 500.0, 0.9);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        // The transfer curve rises to the fold point and then turns back down, so the loudest sample
        // in the output is the fold point itself - 1/k = 0.7071 here - and the input's own peak of 0.9
        // comes out at 0.5142, which the closed-form theory below pins directly.
        double peak = 0.0;
        for (int i = 0; i < left.Length; i++) { peak = Math.Max(peak, Math.Abs(left[i])); }

        peak.Should().BeApproximately(0.7071068, 0.01);
    }

    [Theory]
    [InlineData(1.0, 0.25, 0.2, 0.2)]
    [InlineData(1.0, 0.25, 0.7, 0.7)]
    [InlineData(1.0, 0.25, 0.9, 0.5142136)]
    [InlineData(1.0, 0.25, 1.4142136, 0.0)]
    [InlineData(10.0, 0.25, 0.1, 0.0414214)]
    [InlineData(4.0, 1.0, 0.5, 0.5)]
    public void The_transfer_curve_is_the_measured_closed_form(
        double drive, double threshold, double input, double expected)
    {
        //Arrange
        WaveFolderEffect effect = new WaveFolderEffect { Drive = drive, Threshold = threshold, Mix = 1.0 };
        float[] source = { (float)input, (float)-input };

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        ((double)left[0]).Should().BeApproximately(expected, 1.0e-5);
        ((double)left[1]).Should().BeApproximately(-expected, 1.0e-5);
    }

    [Fact]
    public void Below_the_fold_point_the_effect_is_a_bit_identical_pass_through()
    {
        //Arrange
        // MEASURED: the output is divided by the same k the input was multiplied by, so DRIVE DOES
        // NOT EVEN AMPLIFY until the signal reaches the fold point.
        WaveFolderEffect effect = new WaveFolderEffect { Drive = 8.0, Threshold = 1.0, Mix = 1.0 };
        float[] source = EffectSignals.Noise(2048, 5u, 0.3);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Copy(source), out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Fact]
    public void An_input_of_twice_the_fold_point_folds_to_zero()
    {
        //Arrange
        // MEASURED: foldOnce reaches zero at u = 2, which is an input of 2/k - here 1.4142, not the
        // 0.5 a triangle folder bounded by the threshold would have given.
        WaveFolderEffect effect = new WaveFolderEffect { Drive = 1.0, Threshold = 0.25, Mix = 1.0 };
        float[] source = { 1.4142136f, -1.4142136f, 0.25f, -0.25f, 0.0f };

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        ((double)left[0]).Should().BeApproximately(0.0, 1.0e-6);
        ((double)left[1]).Should().BeApproximately(0.0, 1.0e-6);
        ((double)left[2]).Should().BeApproximately(0.25, 1.0e-6);
        ((double)left[3]).Should().BeApproximately(-0.25, 1.0e-6);
        ((double)left[4]).Should().BeApproximately(0.0, 1.0e-6);
    }

    [Fact]
    public void Drive_pushes_the_signal_into_the_fold_and_makes_harmonics()
    {
        //Arrange
        // A tenth of full scale against the default threshold does not reach the fold at all - the
        // fold point at drive 1 is 0.707 and at drive 5 it is still 0.141 - so a drive of one is a
        // clean sine and a drive of twenty is not.
        float[] source = EffectSignals.Sine(Spectrum.BlockLength, Spectrum.BinFrequency(101), 0.1);

        //Act
        double gentle = HarmonicEnergyDb(source, 1.0);
        double hard = HarmonicEnergyDb(source, 20.0);

        //Assert
        gentle.Should().BeLessThan(-100.0);
        hard.Should().BeGreaterThan(-20.0);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        WaveFolderEffect effect = new WaveFolderEffect { Drive = 50.0, Threshold = 0.1, Mix = 0.0 };
        float[] source = EffectSignals.Noise(2048, 11u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    private static double HarmonicEnergyDb(float[] source, double drive)
    {
        WaveFolderEffect effect = new WaveFolderEffect { Drive = drive, Threshold = 0.25, Mix = 1.0 };

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Copy(source), out left, out right);

        double[] magnitudes = Spectrum.PreciseMagnitudes(left);
        double fundamental = magnitudes[101];
        double harmonics = 0.0;

        for (int harmonic = 2; harmonic * 101 < magnitudes.Length; harmonic++)
        {
            double value = magnitudes[harmonic * 101];
            harmonics += value * value;
        }

        return 20.0 * Math.Log10(Math.Sqrt(harmonics) / fundamental);
    }
}

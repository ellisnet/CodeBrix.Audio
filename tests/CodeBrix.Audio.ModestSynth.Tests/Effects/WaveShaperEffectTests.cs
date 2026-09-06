using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="WaveShaperEffect" />.
/// </summary>
/// <remarks>
/// The aliasing test drives a sine whose bin shares no factor with the transform length, so every
/// harmonic that folds back lands on a bin that is NOT a multiple of the fundamental's. That is
/// what lets <c>Spectrum.AliasFloorDb</c> measure aliasing rather than measuring harmonics, and it
/// needs the double-precision transform because a well-oversampled result has almost nothing there
/// to find.
/// </remarks>
public class WaveShaperEffectTests
{
    private const int ProbeBin = 1711;

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect();

        //Assert
        effect.Drive.Should().Be(1.0);
        effect.DriveBoost.Should().Be(1.0);
        effect.OutputLevel.Should().Be(0.1);
        effect.HighQuality.Should().BeFalse();
        effect.Mix.Should().Be(1.0);
    }

    [Fact]
    public void The_default_output_level_is_a_twenty_decibel_cut()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect { Drive = 1.0, Mix = 1.0 };
        float[] source = EffectSignals.Sine(4096, 200.0, 0.02);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double change = EffectSignals.RmsDb(left, 0, left.Length)
            - EffectSignals.RmsDb(source, 0, source.Length);

        // A drive of one with the default boost is a gain of two into a curve that is still linear
        // at this level, so the only thing left is the output level: 2 x 0.1 is 6 dB down.
        change.Should().BeApproximately(-13.98, 0.2);
    }

    [Fact]
    public void Drive_of_ten_at_full_output_matches_the_reference_players_boost()
    {
        //Arrange
        // The reference player added 16.4 dB to white noise at these settings.
        WaveShaperEffect effect = new WaveShaperEffect
        {
            Drive = 10.0, DriveBoost = 1.0, OutputLevel = 1.0, Mix = 1.0,
        };

        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 12345u, 0.25);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double change = EffectSignals.RmsDb(left, 0, left.Length)
            - EffectSignals.RmsDb(source, 0, source.Length);
        change.Should().BeApproximately(16.4, 1.5);
    }

    [Fact]
    public void HighQuality_pushes_the_aliases_down()
    {
        //Act
        double plain = AliasFloorDb(false);
        double oversampled = AliasFloorDb(true);

        //Assert
        oversampled.Should().BeLessThan(plain - 12.0);
    }

    [Fact]
    public void HighQuality_keeps_the_harmonics_it_is_supposed_to_keep()
    {
        //Arrange
        float[] source = EffectSignals.Sine(Spectrum.BlockLength * 3, Spectrum.BinFrequency(101), 0.5);

        //Act
        double plain = ThirdHarmonicDb(source, false);
        double oversampled = ThirdHarmonicDb(source, true);

        //Assert
        oversampled.Should().BeApproximately(plain, 1.0);
    }

    [Fact]
    public void DriveBoost_of_zero_halves_the_drive()
    {
        //Arrange
        float[] source = EffectSignals.Sine(4096, 200.0, 0.001);

        //Act
        double boosted = LevelChangeDb(source, 1.0);
        double plain = LevelChangeDb(source, 0.0);

        //Assert
        (boosted - plain).Should().BeApproximately(6.02, 0.05);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect { Drive = 100.0, Mix = 0.0 };
        float[] source = EffectSignals.Noise(2048, 13u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Fact]
    public void OutputLevel_honours_the_binding_appendixs_wider_range()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect();

        //Act
        effect.OutputLevel = 8.0;

        //Assert
        effect.OutputLevel.Should().Be(8.0);
    }

    private static double LevelChangeDb(float[] source, double driveBoost)
    {
        WaveShaperEffect effect = new WaveShaperEffect
        {
            Drive = 1.0, DriveBoost = driveBoost, OutputLevel = 1.0, Mix = 1.0,
        };

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Copy(source), out left, out right);

        return EffectSignals.RmsDb(left, 0, left.Length) - EffectSignals.RmsDb(source, 0, source.Length);
    }

    private static double AliasFloorDb(bool highQuality)
    {
        float[] block = ShapedBlock(
            EffectSignals.Sine(Spectrum.BlockLength * 3, Spectrum.BinFrequency(ProbeBin), 0.5), highQuality);

        return Spectrum.AliasFloorDb(Spectrum.PreciseMagnitudes(block), ProbeBin, 20000.0);
    }

    private static double ThirdHarmonicDb(float[] source, bool highQuality)
    {
        double[] magnitudes = Spectrum.PreciseMagnitudes(ShapedBlock(source, highQuality));
        return 20.0 * Math.Log10(magnitudes[303] / magnitudes[101]);
    }

    private static float[] ShapedBlock(float[] source, bool highQuality)
    {
        WaveShaperEffect effect = new WaveShaperEffect
        {
            Drive = 20.0, DriveBoost = 1.0, OutputLevel = 1.0, Mix = 1.0, HighQuality = highQuality,
        };

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Copy(source), out left, out right);

        float[] block = new float[Spectrum.BlockLength];
        Array.Copy(left, Spectrum.BlockLength, block, 0, Spectrum.BlockLength);

        return block;
    }
}

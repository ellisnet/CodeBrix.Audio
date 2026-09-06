using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="PitchShiftEffect" />.
/// </summary>
/// <remarks>
/// Pitch is measured by autocorrelation rather than by the loudest bin, because an overlap-add
/// shifter leaves an amplitude ripple at the grain rate whose sidebands sit within a bin or two of
/// the carrier. Autocorrelation reads the period, which the ripple does not touch, and a parabola
/// through the peak takes it to a fraction of a sample.
/// </remarks>
public class PitchShiftEffectTests
{
    private const double ReferenceHz = 440.0;

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        PitchShiftEffect effect = new PitchShiftEffect();

        //Assert
        effect.PitchShift.Should().Be(0.0);
        effect.Mix.Should().Be(0.5);
    }

    [Fact]
    public void PitchShift_of_seven_semitones_puts_440_hertz_at_659_3()
    {
        //Act
        double measured = ShiftedFrequency(7.0);

        //Assert
        measured.Should().BeApproximately(659.26, 1.0);
    }

    [Fact]
    public void PitchShift_of_minus_an_octave_halves_the_frequency()
    {
        //Act
        double measured = ShiftedFrequency(-12.0);

        //Assert
        measured.Should().BeApproximately(220.0, 0.5);
    }

    [Fact]
    public void PitchShift_keeps_the_level_it_was_given()
    {
        //Arrange
        float[] source = EffectSignals.Sine(EffectSignals.SampleRate * 2, ReferenceHz, 0.5);
        PitchShiftEffect effect = new PitchShiftEffect { PitchShift = 7.0, Mix = 1.0 };

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double shifted = EffectSignals.RmsDb(left, EffectSignals.SampleRate, EffectSignals.SampleRate);
        double original = EffectSignals.RmsDb(source, EffectSignals.SampleRate, EffectSignals.SampleRate);
        shifted.Should().BeApproximately(original, 1.0);
    }

    [Fact]
    public void Mix_of_one_leaves_no_trace_of_the_original_pitch()
    {
        //Act
        double dryLevelDb = DryBinLevelDb(1.0);

        //Assert
        dryLevelDb.Should().BeLessThan(WetOnlyThresholdDb);
    }

    [Fact]
    public void Mix_of_one_half_keeps_both_pitches()
    {
        //Arrange
        double[] magnitudes = SpectrumAtMix(0.5);

        //Act
        double dry = magnitudes[DryBin];
        double wet = Peak(magnitudes, ShiftedBin - 2, ShiftedBin + 2);

        //Assert
        dry.Should().BeGreaterThan(0.0);
        (20.0 * Math.Log10(wet / dry)).Should().BeInRange(-12.0, 3.0);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        PitchShiftEffect effect = new PitchShiftEffect { PitchShift = 12.0, Mix = 0.0 };
        float[] source = EffectSignals.Noise(4096, 5u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Fact]
    public void PitchShift_of_zero_is_a_plain_delay_of_half_a_grain()
    {
        //Arrange
        PitchShiftEffect effect = new PitchShiftEffect { PitchShift = 0.0, Mix = 1.0 };
        float[] source = EffectSignals.Noise(8192, 21u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        int delay = effect.GrainLengthSamples / 2;
        for (int i = 4096; i < 4196; i++)
        {
            ((double)left[i]).Should().BeApproximately(source[i - delay], 1.0e-5);
        }
    }

    [Fact]
    public void PitchShift_is_clamped_to_the_documented_two_octaves()
    {
        //Arrange
        PitchShiftEffect effect = new PitchShiftEffect();

        //Act
        effect.PitchShift = 40.0;
        double high = effect.PitchShift;
        effect.PitchShift = -40.0;
        double low = effect.PitchShift;

        //Assert
        high.Should().Be(PitchShiftEffect.MaximumPitchShift);
        low.Should().Be(PitchShiftEffect.MinimumPitchShift);
    }

    private const int DryBin = 75;
    private const int ShiftedBin = 112;
    private const double WetOnlyThresholdDb = 30.0;

    private static double ShiftedFrequency(double semitones)
    {
        PitchShiftEffect effect = new PitchShiftEffect { PitchShift = semitones, Mix = 1.0 };
        float[] source = EffectSignals.Sine(EffectSignals.SampleRate * 2, ReferenceHz, 0.5);

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        double expected = ReferenceHz * Math.Pow(2.0, semitones / 12.0);

        // The search window holds exactly one period, which is what keeps autocorrelation off the
        // sub-harmonics a pure tone's own correlation peaks at just as strongly.
        return EffectSignals.EstimateFrequency(
            left, EffectSignals.SampleRate, EffectSignals.SampleRate, expected * 0.8, expected * 1.25);
    }

    private static double DryBinLevelDb(double mix)
    {
        double[] magnitudes = SpectrumAtMix(mix);
        return 20.0 * Math.Log10(magnitudes[DryBin]);
    }

    private static double[] SpectrumAtMix(double mix)
    {
        PitchShiftEffect effect = new PitchShiftEffect { PitchShift = 7.0, Mix = mix };
        float[] source = EffectSignals.Sine(Spectrum.BlockLength * 4, Spectrum.BinFrequency(DryBin), 0.5);

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        float[] block = new float[Spectrum.BlockLength];
        Array.Copy(left, Spectrum.BlockLength * 2, block, 0, Spectrum.BlockLength);

        return Spectrum.PreciseMagnitudes(block);
    }

    private static double Peak(double[] magnitudes, int first, int last)
    {
        double best = 0.0;
        for (int i = first; i <= last; i++)
        {
            if (magnitudes[i] > best) { best = magnitudes[i]; }
        }

        return best;
    }
}

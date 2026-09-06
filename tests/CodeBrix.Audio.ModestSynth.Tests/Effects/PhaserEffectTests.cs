using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="PhaserEffect" />.
/// </summary>
/// <remarks>
/// <para>
/// The notch tests hold the oscillator still (<c>modRate="0"</c>) and turn the feedback off, which
/// makes the effect linear and time-invariant, so its impulse response IS its transfer function and
/// a rectangular-window transform of it measures the notches exactly. At <c>mix="0.5"</c> the dry
/// and wet paths are equal, so wherever the all-pass cascade has turned the signal through 180
/// degrees the two cancel completely.
/// </para>
/// <para>
/// A six-section cascade cancels at three frequencies - where each section contributes 30, 90 and
/// 150 degrees - which lands them at 0.268, 1.0 and 3.73 times the all-pass corner.
/// </para>
/// </remarks>
public class PhaserEffectTests
{
    private const double NotchThresholdDb = -30.0;

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect();

        //Assert
        effect.Mix.Should().Be(0.5);
        effect.ModDepth.Should().Be(0.2);
        effect.ModRate.Should().Be(0.2);
        effect.CenterFrequency.Should().Be(400.0);
        effect.Feedback.Should().Be(0.7);
        effect.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Process_puts_one_notch_per_pair_of_all_pass_sections()
    {
        //Act
        IReadOnlyList<double> notches = NotchFrequencies(400.0);

        //Assert
        notches.Should().HaveCount(PhaserEffect.AllPassStageCount / 2);
    }

    [Fact]
    public void Process_puts_the_notches_where_a_six_section_cascade_turns_through_half_a_cycle()
    {
        //Arrange
        double[] expected = { Math.Tan(Math.PI / 12.0), 1.0, Math.Tan(5.0 * Math.PI / 12.0) };

        //Act
        IReadOnlyList<double> notches = NotchFrequencies(400.0);

        //Assert
        for (int i = 0; i < expected.Length; i++)
        {
            (notches[i] / 400.0).Should().BeApproximately(expected[i], expected[i] * 0.05);
        }
    }

    [Fact]
    public void CenterFrequency_moves_every_notch_with_it()
    {
        //Arrange
        IReadOnlyList<double> low = NotchFrequencies(400.0);

        //Act
        IReadOnlyList<double> high = NotchFrequencies(800.0);

        //Assert
        // The lowest notch of the three sits on bin 18 of 4,096, so a fifth of a bin of rounding is
        // three per cent of the ratio; the tolerance is the transform's, not the filter's.
        for (int i = 0; i < low.Count; i++)
        {
            (high[i] / low[i]).Should().BeApproximately(2.0, 0.1);
        }
    }

    [Fact]
    public void ModRate_sweeps_the_corner_at_that_many_cycles_a_second()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect { ModRate = 2.0, ModDepth = 1.0, CenterFrequency = 400.0 };
        effect.Prepare(EffectSignals.SampleRate);

        //Act - half a second is exactly one cycle at two hertz, so the peak is unambiguous.
        double[] corners = SweepCorners(effect, EffectSignals.SampleRate / 2);

        //Assert
        // MEASURED (round 3, item 43): the sweep is exponential, five octaves EACH WAY at full depth,
        // and it runs downward first from the centre.
        Lowest(corners).Should().BeApproximately(
            400.0 / Math.Pow(2.0, PhaserEffect.SweepOctavesAtFullDepth), 1.0);
        Highest(corners).Should().BeApproximately(
            400.0 * Math.Pow(2.0, PhaserEffect.SweepOctavesAtFullDepth), 40.0);

        // Two hertz means the corner reaches the top of its travel three quarters of a cycle in, which
        // is 0.375 s, having reached the bottom at 0.125 s.
        (IndexOfHighest(corners) / (double)EffectSignals.SampleRate).Should().BeApproximately(0.375, 0.01);
    }

    [Fact]
    public void ModDepth_of_zero_holds_the_corner_at_the_center_frequency()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect { ModRate = 5.0, ModDepth = 0.0, CenterFrequency = 900.0 };
        effect.Prepare(EffectSignals.SampleRate);

        //Act
        double[] corners = SweepCorners(effect, EffectSignals.SampleRate / 4);

        //Assert
        Lowest(corners).Should().BeApproximately(900.0, 0.001);
        Highest(corners).Should().BeApproximately(900.0, 0.001);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect { Mix = 0.0 };
        float[] source = EffectSignals.Noise(4096, 99u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Fact]
    public void Feedback_at_the_top_of_its_range_still_decays_to_silence()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect { Feedback = 1.0, Mix = 1.0, ModRate = 1.0, ModDepth = 1.0 };
        float[] source = new float[EffectSignals.SampleRate];
        float[] noise = EffectSignals.Noise(EffectSignals.SampleRate / 2, 7u, 0.5);
        Array.Copy(noise, source, noise.Length);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double whileRinging = EffectSignals.RmsDb(left, EffectSignals.SampleRate / 2, 2400);
        double afterHalfASecond = EffectSignals.RmsDb(left, EffectSignals.SampleRate - 2400, 2400);
        afterHalfASecond.Should().BeLessThan(whileRinging - 20.0);
    }

    [Theory]
    [InlineData(400.0, 108.0, 401.0, 1486.0)]
    [InlineData(1000.0, 269.0, 999.0, 3655.0)]
    [InlineData(4000.0, 1098.0, 4000.0, 11639.0)]
    public void The_three_notches_land_where_the_reference_measured_them(
        double centerFrequency, double low, double middle, double high)
    {
        //Arrange
        // MEASURED (round 2, item 22): three notches, the middle one exactly on centerFrequency, and a
        // ratio of 3.71 between them everywhere the bilinear warping does not pull the top one in - at
        // a 4 kHz centre the reference's own top notch had come down to 11639 Hz, a ratio of 2.91.
        IReadOnlyList<double> notches = NotchFrequencies(centerFrequency);

        //Assert
        notches.Should().HaveCount(3);
        notches[0].Should().BeApproximately(low, low * 0.06);
        notches[1].Should().BeApproximately(middle, middle * 0.03);
        notches[2].Should().BeApproximately(high, high * 0.06);
    }

    [Fact]
    public void A_full_mix_is_flat_because_an_all_pass_cascade_is()
    {
        //Arrange
        // MEASURED: at mix="1.0" the response is flat to 0.01 dB everywhere - the dry path is gone and
        // an all-pass chain has unit magnitude.
        PhaserEffect effect = new PhaserEffect
        {
            Mix = 1.0, ModRate = 0.0, ModDepth = 0.0, Feedback = 0.0, CenterFrequency = 400.0,
        };

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Impulse(Spectrum.BlockLength), out left, out right);

        double[] magnitudes = Spectrum.PreciseMagnitudes(left);

        //Assert
        for (int bin = 4; bin < magnitudes.Length / 2; bin++)
        {
            (20.0 * Math.Log10(magnitudes[bin])).Should().BeApproximately(0.0, 0.05);
        }
    }

    [Fact]
    public void A_negative_feedback_is_the_same_as_none()
    {
        //Arrange
        // MEASURED: feedback="-0.7" was identical to feedback="0.0" to 0.05 dB everywhere.
        float[] source = EffectSignals.Noise(4096, 31u, 0.5);

        //Act
        float[] none = Render(source, feedback: 0.0);
        float[] negative = Render(source, feedback: -0.7);

        //Assert
        for (int i = 0; i < none.Length; i++)
        {
            ((double)negative[i]).Should().BeApproximately(none[i], 1.0e-6);
        }
    }

    [Fact]
    public void A_positive_feedback_turns_the_notches_into_shallow_peaks()
    {
        //Arrange
        // MEASURED: feedback="0.7" gave peaks of +1.2/+1.4/+1.6 dB where the notches had been, and
        // about -2 dB elsewhere.
        PhaserEffect effect = new PhaserEffect
        {
            Mix = 0.5, ModRate = 0.0, ModDepth = 0.0, Feedback = 0.7, CenterFrequency = 400.0,
        };

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Impulse(Spectrum.BlockLength), out left, out right);

        double[] magnitudes = Spectrum.PreciseMagnitudes(left);
        int bin = (int)Math.Round(400.0 / Spectrum.BinFrequency(1));

        //Assert - a peak, not a notch, and a small one.
        (20.0 * Math.Log10(magnitudes[bin])).Should().BeInRange(0.5, 3.0);
    }

    [Fact]
    public void A_zero_rate_freezes_the_chain_on_the_center_frequency()
    {
        //Arrange
        // MEASURED: modRate="0" is identical to modDepth="0", to 0.05 dB, so the oscillator is
        // zero-centred on centerFrequency rather than starting at one end of its travel.
        PhaserEffect frozen = new PhaserEffect
        {
            Mix = 0.5, ModRate = 0.0, ModDepth = 1.0, Feedback = 0.0, CenterFrequency = 400.0,
        };
        frozen.Prepare(EffectSignals.SampleRate);

        //Act
        double[] corners = SweepCorners(frozen, EffectSignals.SampleRate / 4);

        //Assert
        Lowest(corners).Should().BeApproximately(400.0, 0.001);
        Highest(corners).Should().BeApproximately(400.0, 0.001);
    }

    private static float[] Render(float[] source, double feedback)
    {
        PhaserEffect effect = new PhaserEffect
        {
            Mix = 0.5, ModRate = 0.0, ModDepth = 0.0, Feedback = feedback, CenterFrequency = 400.0,
        };

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        return left;
    }

    private static double[] SweepCorners(PhaserEffect effect, int frames)
    {
        double[] corners = new double[frames];
        float[] left = new float[1];
        float[] right = new float[1];

        for (int i = 0; i < frames; i++)
        {
            left[0] = 0.0f;
            right[0] = 0.0f;
            effect.Process(left, right, 1);
            corners[i] = effect.CurrentCornerFrequency;
        }

        return corners;
    }

    private static double Lowest(double[] values)
    {
        double lowest = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < lowest) { lowest = values[i]; }
        }

        return lowest;
    }

    private static double Highest(double[] values) => values[IndexOfHighest(values)];

    private static int IndexOfHighest(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best]) { best = i; }
        }

        return best;
    }

    private static IReadOnlyList<double> NotchFrequencies(double centerFrequency)
    {
        PhaserEffect effect = new PhaserEffect
        {
            Mix = 0.5,
            ModRate = 0.0,
            ModDepth = 0.0,
            Feedback = 0.0,
            CenterFrequency = centerFrequency,
        };

        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, EffectSignals.Impulse(Spectrum.BlockLength), out left, out right);

        double[] magnitudes = Spectrum.PreciseMagnitudes(left);
        List<double> notches = new List<double>();

        for (int i = 2; i < magnitudes.Length - 1; i++)
        {
            if (magnitudes[i] >= magnitudes[i - 1] || magnitudes[i] >= magnitudes[i + 1]) { continue; }
            if (20.0 * Math.Log10(magnitudes[i] + double.Epsilon) >= NotchThresholdDb) { continue; }

            notches.Add(Spectrum.BinFrequency(i));
        }

        return notches;
    }
}

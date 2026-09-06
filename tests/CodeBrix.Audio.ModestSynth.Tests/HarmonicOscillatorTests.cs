using System;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="HarmonicOscillator" />: the exact spectrum it produces, the three shaping
/// laws, that nothing above Nyquist ever sounds, and that a bare one is a sine.
/// </summary>
public class HarmonicOscillatorTests
{
    private const int FundamentalBin = 43;

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new HarmonicOscillator().Waveform.Should().Be("harmonic");

    [Fact]
    public void Defaults_match_the_formats_own()
    {
        //Arrange
        HarmonicOscillator oscillator = new HarmonicOscillator();

        //Act, Assert
        oscillator.NumPartials.Should().Be(8);
        oscillator.Tilt.Should().Be(0.0);
        oscillator.OddEvenBalance.Should().Be(0.5);
        oscillator.Normalization.Should().Be(0.0);
        oscillator.GetPartialLevel(1).Should().Be(0.0);
        oscillator.GetPartialLevel(64).Should().Be(0.0);
        oscillator.IsPureSineFallback.Should().BeTrue();
    }

    [Fact]
    public void With_no_partial_levels_it_is_a_pure_sine_at_a_sines_own_level()
    {
        //Arrange
        // Measured behaviour: the reference player renders a harmonic oscillator carrying no
        // harmonic* attributes as a pure sine at the same level as waveform="sine".
        HarmonicOscillator oscillator = Build();
        SineOscillator sine = new SineOscillator();
        sine.SetSampleRate(Spectrum.SampleRate);
        sine.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        float[] block = Spectrum.RenderBlock(oscillator);
        float[] reference = Spectrum.RenderBlock(sine);
        double[] magnitudes = Spectrum.PreciseMagnitudes(block);

        //Assert
        oscillator.ActivePartialCount.Should().Be(1);
        Spectrum.PeakBin(magnitudes).Should().Be(FundamentalBin);
        Spectrum.AliasFloorDb(magnitudes, FundamentalBin, 24000.0).Should().BeLessThan(-90.0);
        Rms(block).Should().BeApproximately(Rms(reference), 1e-4);
    }

    [Fact]
    public void Setting_any_partial_level_ends_the_pure_sine_fallback()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();

        //Act
        oscillator.SetPartialLevel(3, 1.0);

        //Assert
        oscillator.IsPureSineFallback.Should().BeFalse();
        oscillator.ActivePartialCount.Should().Be(1);
        oscillator.GetPartialGain(1).Should().Be(0.0);
        oscillator.GetPartialGain(3).Should().Be(1.0);
    }

    [Fact]
    public void ClearPartialLevels_puts_the_pure_sine_fallback_back()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        oscillator.SetPartialLevel(5, 0.5);

        //Act
        oscillator.ClearPartialLevels();

        //Assert
        oscillator.IsPureSineFallback.Should().BeTrue();
        oscillator.GetPartialGain(1).Should().Be(1.0);
    }

    [Fact]
    public void A_set_of_partials_produces_exactly_those_spectrum_lines_at_exactly_those_levels()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        for (int k = 1; k <= 8; k++) { oscillator.SetPartialLevel(k, 1.0 / k); }

        //Act
        double[] magnitudes = Spectrum.PreciseMagnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        for (int k = 1; k <= 8; k++)
        {
            RelativeDb(magnitudes, k).Should().BeApproximately(20.0 * Math.Log10(1.0 / k), 0.02);
        }

        RelativeDb(magnitudes, 9).Should().BeLessThan(-90.0);
        Spectrum.AliasFloorDb(magnitudes, FundamentalBin, 24000.0).Should().BeLessThan(-80.0);
    }

    [Fact]
    public void NumPartials_silences_everything_above_it()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        for (int k = 1; k <= 8; k++) { oscillator.SetPartialLevel(k, 1.0); }
        oscillator.NumPartials = 3;

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        oscillator.ActivePartialCount.Should().Be(3);
        RelativeDb(magnitudes, 3).Should().BeApproximately(0.0, 0.02);
        RelativeDb(magnitudes, 4).Should().BeLessThan(-90.0);
    }

    [Theory]
    [InlineData(-1.0, 4.0, 16.0, 64.0)]
    [InlineData(-0.5, 2.0, 4.0, 8.0)]
    [InlineData(0.0, 1.0, 1.0, 1.0)]
    [InlineData(0.5, 0.5, 0.25, 0.125)]
    [InlineData(1.0, 0.25, 0.0625, 0.015625)]
    public void Tilt_follows_the_measured_twelve_decibels_per_octave_law(double tilt, double second,
        double fourth, double eighth)
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        for (int k = 1; k <= 8; k++) { oscillator.SetPartialLevel(k, 1.0); }
        oscillator.Tilt = tilt;

        //Act, Assert
        oscillator.GetPartialGain(1).Should().BeApproximately(1.0, 1e-6);
        oscillator.GetPartialGain(2).Should().BeApproximately(second, 1e-6);
        oscillator.GetPartialGain(4).Should().BeApproximately(fourth, 1e-6);
        oscillator.GetPartialGain(8).Should().BeApproximately(eighth, 1e-6);
    }

    [Fact]
    public void A_positive_tilt_darkens_and_a_negative_one_brightens()
    {
        //Arrange
        HarmonicOscillator dark = Build();
        HarmonicOscillator bright = Build();
        for (int k = 1; k <= 8; k++)
        {
            dark.SetPartialLevel(k, 1.0);
            bright.SetPartialLevel(k, 1.0);
        }

        dark.Tilt = 0.6;
        bright.Tilt = -0.6;

        //Act
        double darkCentroid = Spectrum.PowerCentroid(RenderSeconds(dark, 1), Spectrum.BlockLength);
        double brightCentroid = Spectrum.PowerCentroid(RenderSeconds(bright, 1), Spectrum.BlockLength);

        //Assert
        darkCentroid.Should().BeLessThan(brightCentroid);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.25, 1.0, 0.5)]
    [InlineData(0.5, 1.0, 1.0)]
    [InlineData(0.75, 0.5, 1.0)]
    [InlineData(1.0, 0.0, 1.0)]
    public void Odd_even_balance_follows_the_documented_law(double balance, double odd, double even)
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        for (int k = 1; k <= 8; k++) { oscillator.SetPartialLevel(k, 1.0); }
        oscillator.OddEvenBalance = balance;

        //Act, Assert
        oscillator.GetPartialGain(1).Should().BeApproximately(odd, 1e-9);
        oscillator.GetPartialGain(3).Should().BeApproximately(odd, 1e-9);
        oscillator.GetPartialGain(2).Should().BeApproximately(even, 1e-9);
        oscillator.GetPartialGain(4).Should().BeApproximately(even, 1e-9);
    }

    [Fact]
    public void Odd_even_balance_at_the_extremes_removes_a_whole_family_from_the_spectrum()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        for (int k = 1; k <= 8; k++) { oscillator.SetPartialLevel(k, 1.0); }
        oscillator.OddEvenBalance = 0.0;

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        RelativeDb(magnitudes, 3).Should().BeApproximately(0.0, 0.02);
        RelativeDb(magnitudes, 2).Should().BeLessThan(-90.0);
        RelativeDb(magnitudes, 4).Should().BeLessThan(-90.0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Full_normalization_divides_by_the_sum_of_the_partial_gains(int partials)
    {
        //Arrange
        // MEASURED (round 2, item 26): harmonicNormalization is (1 - n) + n/sum(levels), a plain SUM
        // divide, so at full normalization every partial ends up at 1/partials and the summed PEAK -
        // not the RMS - is where a single full-level partial's would be.
        HarmonicOscillator oscillator = Build(Spectrum.BinFrequency(11));
        oscillator.NumPartials = partials;
        oscillator.Normalization = 1.0;
        for (int k = 1; k <= partials; k++) { oscillator.SetPartialLevel(k, 1.0); }

        //Act
        double rms = Rms(Spectrum.RenderBlock(oscillator));

        //Assert
        oscillator.ActivePartialCount.Should().Be(partials);
        oscillator.GetPartialGain(1).Should().BeApproximately(1.0 / partials, 1e-9);
        rms.Should().BeApproximately(Math.Sqrt(0.5 * partials) / partials, 1e-3);
    }

    [Fact]
    public void Without_normalization_the_level_climbs_as_partials_are_added()
    {
        //Arrange
        HarmonicOscillator few = Build(Spectrum.BinFrequency(11));
        HarmonicOscillator many = Build(Spectrum.BinFrequency(11));
        few.NumPartials = 4;
        many.NumPartials = 16;
        for (int k = 1; k <= 16; k++)
        {
            few.SetPartialLevel(k, 1.0);
            many.SetPartialLevel(k, 1.0);
        }

        //Act
        double quiet = Rms(Spectrum.RenderBlock(few));
        double loud = Rms(Spectrum.RenderBlock(many));

        //Assert
        loud.Should().BeGreaterThan(quiet * 1.5);
    }

    [Fact]
    public void Half_normalization_blends_half_way_toward_the_full_compensation()
    {
        //Arrange
        HarmonicOscillator none = Build(Spectrum.BinFrequency(11));
        HarmonicOscillator half = Build(Spectrum.BinFrequency(11));
        HarmonicOscillator full = Build(Spectrum.BinFrequency(11));
        foreach (HarmonicOscillator oscillator in new[] { none, half, full })
        {
            oscillator.NumPartials = 16;
            for (int k = 1; k <= 16; k++) { oscillator.SetPartialLevel(k, 1.0); }
        }

        half.Normalization = 0.5;
        full.Normalization = 1.0;

        //Act
        double noneDb = 20.0 * Math.Log10(none.GetPartialGain(1));
        double halfDb = 20.0 * Math.Log10(half.GetPartialGain(1));
        double fullDb = 20.0 * Math.Log10(full.GetPartialGain(1));

        //Assert
        // MEASURED: the blend is LINEAR IN GAIN - (1 - n) + n/sum - not linear in decibels.
        noneDb.Should().BeApproximately(0.0, 1e-9);
        half.GetPartialGain(1).Should().BeApproximately(
            (1.0 + full.GetPartialGain(1)) / 2.0, 1e-9);
        halfDb.Should().BeGreaterThan(fullDb);
    }

    [Fact]
    public void Normalization_leaves_a_single_full_level_partial_alone()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        oscillator.Normalization = 1.0;

        //Act, Assert
        oscillator.GetPartialGain(1).Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void Partials_that_would_reach_Nyquist_are_muted_rather_than_folded_back()
    {
        //Arrange
        HarmonicOscillator oscillator = Build(1000.0);
        oscillator.NumPartials = 64;
        for (int k = 1; k <= 64; k++) { oscillator.SetPartialLevel(k, 1.0); }

        //Act
        int active = oscillator.ActivePartialCount;

        //Assert
        active.Should().Be(23);                                  // 24 kHz over 1 kHz, exclusive
        oscillator.GetPartialGain(23).Should().BeGreaterThan(0.0);
        oscillator.GetPartialGain(24).Should().Be(0.0);
    }

    [Fact]
    public void A_high_note_with_every_partial_set_still_has_no_aliasing()
    {
        //Arrange
        HarmonicOscillator oscillator = Build(Spectrum.BinFrequency(1367));
        oscillator.NumPartials = 64;
        for (int k = 1; k <= 64; k++) { oscillator.SetPartialLevel(k, 1.0 / k); }

        //Act
        double[] magnitudes = Spectrum.PreciseMagnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        Spectrum.AliasFloorDb(magnitudes, 1367, 24000.0).Should().BeLessThan(-90.0);
    }

    [Fact]
    public void Raising_the_pitch_brings_partials_back_when_it_falls_again()
    {
        //Arrange
        HarmonicOscillator oscillator = Build(1000.0);
        oscillator.NumPartials = 16;
        for (int k = 1; k <= 16; k++) { oscillator.SetPartialLevel(k, 1.0); }

        //Act
        int atLowPitch = oscillator.ActivePartialCount;
        oscillator.SetFrequency(5000.0);
        int atHighPitch = oscillator.ActivePartialCount;
        oscillator.SetFrequency(1000.0);
        int backAgain = oscillator.ActivePartialCount;

        //Assert
        atLowPitch.Should().Be(16);
        atHighPitch.Should().Be(4);
        backAgain.Should().Be(16);
    }

    [Fact]
    public void Everything_muted_renders_silence_without_throwing()
    {
        //Arrange
        HarmonicOscillator oscillator = Build(30000.0);
        oscillator.SetPartialLevel(1, 1.0);
        float[] block = new float[512];

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(block);

        //Assert
        oscillator.ActivePartialCount.Should().Be(0);
        Rms(block).Should().Be(0.0);
    }

    [Fact]
    public void Render_is_repeatable_to_the_sample()
    {
        //Arrange
        HarmonicOscillator first = Build();
        HarmonicOscillator second = Build();
        for (int k = 1; k <= 12; k++)
        {
            first.SetPartialLevel(k, 1.0 / k);
            second.SetPartialLevel(k, 1.0 / k);
        }

        first.Tilt = 0.3;
        second.Tilt = 0.3;
        float[] a = new float[2048];
        float[] b = new float[2048];

        //Act
        first.Reset(0.25);
        first.Render(a);
        second.Reset(0.25);
        second.Render(b);

        //Assert
        a.Should().Equal(b);
    }

    [Fact]
    public void Render_allocates_nothing_even_when_a_parameter_changed()
    {
        //Arrange
        HarmonicOscillator oscillator = Build();
        oscillator.NumPartials = 32;
        for (int k = 1; k <= 32; k++) { oscillator.SetPartialLevel(k, 1.0 / k); }
        float[] block = new float[512];
        for (int i = 0; i < 4; i++) { oscillator.Render(block); }

        int pass = 0;

        // Measured through AllocationProbe, which keeps the LOWEST of several attempts: a single
        // GC.GetAllocatedBytesForCurrentThread difference is overstated whenever another thread's
        // collection retires this thread's allocation context mid-measurement, which under a parallel
        // test run happens often enough to be flaky.
        Action work = () =>
        {
            for (int i = 0; i < 64; i++)
            {
                int step = pass + i;
                oscillator.Tilt = (step % 21) * 0.05;
                oscillator.OddEvenBalance = (step % 11) * 0.1;
                oscillator.Normalization = (step % 5) * 0.25;
                oscillator.SetPartialLevel(1 + (step % 32), 0.5);
                oscillator.Render(block);
            }

            pass++;
        };

        //Act
        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Fact]
    public void Every_parameter_clamps_and_ignores_a_value_that_is_not_a_number()
    {
        //Arrange
        HarmonicOscillator oscillator = new HarmonicOscillator();

        //Act
        oscillator.NumPartials = 999;
        int high = oscillator.NumPartials;
        oscillator.NumPartials = -5;
        int low = oscillator.NumPartials;
        oscillator.Tilt = 9.0;
        oscillator.OddEvenBalance = -9.0;
        oscillator.Normalization = 9.0;
        oscillator.SetPartialLevel(1, 9.0);
        double kept = oscillator.GetPartialLevel(1);
        oscillator.SetPartialLevel(1, double.NaN);

        //Assert
        high.Should().Be(64);
        low.Should().Be(1);
        oscillator.Tilt.Should().Be(1.0);
        oscillator.OddEvenBalance.Should().Be(0.0);
        oscillator.Normalization.Should().Be(1.0);
        kept.Should().Be(1.0);
        oscillator.GetPartialLevel(1).Should().Be(1.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void A_partial_number_outside_one_to_sixty_four_is_a_programming_error(int partial)
    {
        //Arrange
        HarmonicOscillator oscillator = new HarmonicOscillator();

        //Act
        Action act = () => oscillator.SetPartialLevel(partial, 0.5);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_patch_hands_its_harmonic_settings_to_the_oscillator()
    {
        //Arrange
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Harmonic,
            NumPartials = 12,
            HarmonicTilt = 0.4,
            HarmonicOddEvenBalance = 0.25,
            HarmonicNormalization = 0.75,
        };

        patch.SetPartialLevel(1, 1.0);
        patch.SetPartialLevel(4, 0.5);

        //Act
        HarmonicOscillator oscillator = (HarmonicOscillator)patch.CreateOscillator(Spectrum.SampleRate);

        //Assert
        oscillator.NumPartials.Should().Be(12);
        oscillator.Tilt.Should().Be(0.4);
        oscillator.OddEvenBalance.Should().Be(0.25);
        oscillator.Normalization.Should().Be(0.75);
        oscillator.GetPartialLevel(1).Should().Be(1.0);
        oscillator.GetPartialLevel(4).Should().Be(0.5);
        oscillator.IsPureSineFallback.Should().BeFalse();
    }

    [Fact]
    public void The_patch_and_the_oscillator_agree_on_how_many_partials_there_are() =>
        ModestPatch.MaximumPartials.Should().Be(HarmonicOscillator.MaximumPartials);

    [Fact]
    public void The_patch_and_the_oscillator_agree_on_the_default_partial_count() =>
        ModestPatch.DefaultNumPartials.Should().Be(HarmonicOscillator.DefaultNumPartials);

    private static HarmonicOscillator Build() => Build(Spectrum.BinFrequency(FundamentalBin));

    private static HarmonicOscillator Build(double frequency)
    {
        HarmonicOscillator oscillator = new HarmonicOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(frequency);
        oscillator.Reset(0.0);
        return oscillator;
    }

    private static float[] RenderSeconds(HarmonicOscillator oscillator, int blocks)
    {
        float[] samples = new float[blocks * Spectrum.BlockLength];
        oscillator.Reset(0.0);
        oscillator.Render(samples);
        return samples;
    }

    private static double RelativeDb(double[] magnitudes, int harmonic)
    {
        int bin = FundamentalBin * harmonic;
        double value = bin < magnitudes.Length ? magnitudes[bin] : 0.0;
        return 20.0 * Math.Log10(Math.Max(value, 1e-12) / magnitudes[FundamentalBin]);
    }

    private static double Rms(float[] samples) => Spectrum.Rms(samples, 0, samples.Length);
}

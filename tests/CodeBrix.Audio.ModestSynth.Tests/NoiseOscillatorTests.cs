using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="NoiseOscillator" />: white, at the level the reference player's noise
/// oscillator sits at, and the same samples every time from the same seed.
/// </summary>
public class NoiseOscillatorTests
{
    [Fact]
    public void Render_produces_the_same_samples_from_the_same_seed()
    {
        //Arrange
        NoiseOscillator first = Build(1234u);
        NoiseOscillator second = Build(1234u);
        float[] a = new float[4096];
        float[] b = new float[4096];

        //Act
        first.Render(a);
        second.Render(b);

        //Assert
        a.Should().Equal(b);
    }

    [Fact]
    public void Render_produces_different_samples_from_different_seeds()
    {
        //Arrange
        NoiseOscillator first = Build(1234u);
        NoiseOscillator second = Build(4321u);
        float[] a = new float[4096];
        float[] b = new float[4096];

        //Act
        first.Render(a);
        second.Render(b);

        //Assert
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Reset_restarts_the_sequence_from_the_seed()
    {
        //Arrange
        NoiseOscillator oscillator = Build(99u);
        float[] first = new float[512];
        float[] second = new float[512];

        //Act
        oscillator.Render(first);
        oscillator.Reset(0.0);
        oscillator.Render(second);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void Reset_ignores_the_phase_it_is_handed()
    {
        //Arrange
        NoiseOscillator atZero = Build(7u);
        NoiseOscillator atAQuarter = Build(7u);
        float[] first = new float[512];
        float[] second = new float[512];

        //Act
        atZero.Reset(0.0);
        atZero.Render(first);
        atAQuarter.Reset(0.25);
        atAQuarter.Render(second);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void Render_is_flat_across_every_octave_below_the_anti_imaging_rolloff()
    {
        //Arrange
        // MEASURED (round 2, item 29): the reference's noise is flat to within 0.4 dB from 63 Hz to
        // 8 kHz and only then falls away, so the flatness check stops at the top octave - which is
        // where the rolloff lives and is checked by its own test below.
        NoiseOscillator oscillator = Build(NoiseOscillator.DefaultSeed);
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));
        double average = BandLevel(magnitudes, 1, magnitudes.Length / 2);

        //Act
        double lowest = double.MaxValue;
        double highest = double.MinValue;
        for (int band = 0; band < 7; band++)
        {
            int start = Math.Max(1, (int)(magnitudes.Length * Math.Pow(2.0, -(8 - band))));
            int end = Math.Min(magnitudes.Length, (int)(magnitudes.Length * Math.Pow(2.0, -(7 - band))));
            double level = 20.0 * Math.Log10(BandLevel(magnitudes, start, end) / average);
            if (level < lowest) { lowest = level; }
            if (level > highest) { highest = level; }
        }

        //Assert
        lowest.Should().BeGreaterThan(-1.5);
        highest.Should().BeLessThan(1.5);
    }

    [Theory]
    [InlineData(1000.0, 0.0)]
    [InlineData(4000.0, 0.13)]
    [InlineData(8000.0, 0.14)]
    [InlineData(16000.0, -6.07)]
    [InlineData(20000.0, -26.99)]
    public void Render_carries_the_reference_players_anti_imaging_rolloff(
        double frequency, double expectedDb)
    {
        //Arrange
        // MEASURED (round 2, item 29): a Welch spectrum of the reference's noise, third-octave
        // smoothed and read against its own 1 kHz band, at 44,100 Hz. The rolloff is a fraction of
        // the sample rate, so the band centres are scaled to the rate these tests run at.
        NoiseOscillator oscillator = Build(NoiseOscillator.DefaultSeed);

        // A Hann window, not a rectangular one: the deepest band sits 27 dB down and a rectangular
        // window's own sidelobes fill a notch that deep before the filter's response is visible.
        double[] magnitudes = Spectrum.WindowedMagnitudes(Spectrum.RenderBlock(oscillator), 0);
        double scale = Spectrum.SampleRate / 44100.0;

        //Act
        double reference = ThirdOctaveLevel(magnitudes, 1000.0 * scale);
        double level = 20.0 * Math.Log10(ThirdOctaveLevel(magnitudes, frequency * scale) / reference);

        //Assert
        // The tolerance is the fit's: one biquad cannot hold the band table and the measured power
        // centroid at once, and 1.3 dB at 8 kHz is what it costs to hold both.
        level.Should().BeApproximately(expectedDb, 2.0);
    }

    // The average magnitude over a third-octave band around a centre frequency.
    private static double ThirdOctaveLevel(double[] magnitudes, double centre)
    {
        double bandwidth = Math.Pow(2.0, 1.0 / 6.0);
        int start = (int)(centre / bandwidth / Spectrum.BinFrequency(1));
        int end = (int)((centre * bandwidth / Spectrum.BinFrequency(1)) + 1);

        return BandLevel(magnitudes, Math.Max(1, start), Math.Min(magnitudes.Length, end));
    }

    [Fact]
    public void Render_sits_two_point_three_decibels_below_a_full_amplitude_sine()
    {
        //Arrange
        NoiseOscillator oscillator = Build(NoiseOscillator.DefaultSeed);
        float[] block = new float[Spectrum.SampleRate];

        //Act
        oscillator.Render(block);
        double relativeDb = 20.0 * Math.Log10(Spectrum.Rms(block, 0, block.Length) / 0.70710678);

        //Assert
        // The measured level of the reference player's noise oscillator against its sine.
        relativeDb.Should().BeApproximately(-2.68, 0.15);
    }

    [Fact]
    public void Amplitude_of_one_gives_a_full_scale_noise_source()
    {
        //Arrange
        NoiseOscillator oscillator = Build(NoiseOscillator.DefaultSeed);
        oscillator.Amplitude = 1.0;
        float[] block = new float[Spectrum.SampleRate];

        //Act
        oscillator.Render(block);

        //Assert
        Spectrum.Rms(block, 0, block.Length).Should().BeApproximately(0.57735, 0.005);
    }

    [Fact]
    public void Amplitude_clamps_a_value_outside_the_range()
    {
        //Arrange
        NoiseOscillator oscillator = new NoiseOscillator();

        //Act
        oscillator.Amplitude = 4.0;

        //Assert
        oscillator.Amplitude.Should().Be(1.0);
    }

    [Fact]
    public void SetFrequency_is_accepted_and_changes_nothing()
    {
        //Arrange
        NoiseOscillator quiet = Build(5u);
        NoiseOscillator loud = Build(5u);
        float[] first = new float[256];
        float[] second = new float[256];

        //Act
        quiet.SetFrequency(50.0);
        quiet.Render(first);
        loud.SetFrequency(5000.0);
        loud.Render(second);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new NoiseOscillator().Waveform.Should().Be("noise");

    private static NoiseOscillator Build(uint seed)
    {
        NoiseOscillator oscillator = new NoiseOscillator { Seed = seed };
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.Reset(0.0);
        return oscillator;
    }

    private static double BandLevel(double[] magnitudes, int start, int end)
    {
        double sum = 0.0;
        for (int i = start; i < end; i++)
        {
            sum += magnitudes[i] * magnitudes[i];
        }

        return Math.Sqrt(sum / (end - start));
    }
}

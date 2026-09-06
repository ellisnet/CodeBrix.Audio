using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="Pluck1Oscillator" />: it rings at the pitch it was tuned to, rings longer
/// the higher the damping, brightens with the pluck type, and renders the same string every time
/// from the same seed.
/// </summary>
public class Pluck1OscillatorTests
{
    private const double DecayMeasurementWindowSeconds = 0.01;

    [Theory]
    [InlineData(55.0)]
    [InlineData(110.0)]
    [InlineData(220.0)]
    [InlineData(440.0)]
    [InlineData(880.0)]
    [InlineData(1760.0)]
    public void Render_rings_at_the_frequency_it_was_tuned_to(double frequency)
    {
        //Arrange
        Pluck1Oscillator oscillator = Build(1.0, 1.0);
        oscillator.SetFrequency(frequency);
        oscillator.Reset(0.0);
        float[] block = new float[Spectrum.BlockLength * 2];

        //Act
        oscillator.Render(block);
        double measured = FundamentalOf(block, frequency);

        //Assert
        // Within a twentieth of a percent, which is less than a hundredth of a semitone.
        measured.Should().BeApproximately(frequency, frequency * 0.005);
    }

    [Fact]
    public void Render_holds_the_pitch_of_the_lowest_note_a_keyboard_can_send()
    {
        //Arrange
        // MIDI note 0. Too low for the FFT the other pitch tests use - one period is longer than
        // their whole analysis block - so this one measures the period directly.
        const double Frequency = 8.1758;
        Pluck1Oscillator oscillator = Build(1.0, 1.0);
        oscillator.SetFrequency(Frequency);
        oscillator.Reset(0.0);
        float[] block = new float[Spectrum.SampleRate];

        //Act
        oscillator.Render(block);
        double measured = PitchByAutocorrelation(block, Frequency);

        //Assert
        measured.Should().BeApproximately(Frequency, 0.05);
    }

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(0.2, 0.4)]
    [InlineData(0.4, 0.6)]
    [InlineData(0.6, 0.8)]
    [InlineData(0.8, 1.0)]
    public void Damping_rings_for_longer_the_higher_it_is(double lower, double higher)
    {
        //Arrange
        const double AtSeconds = 1.0;

        //Act
        double quieter = LevelAfter(lower, AtSeconds);
        double louder = LevelAfter(higher, AtSeconds);

        //Assert
        louder.Should().BeGreaterThan(quieter);
    }

    [Fact]
    public void Damping_decay_time_is_the_one_measured_from_the_reference_player()
    {
        //Arrange
        // Recorded from the reference at damping="0.1" on an 82.4 Hz note: 30 dB down in 1.56 s.
        Pluck1Oscillator oscillator = Build(0.1, 0.5);
        oscillator.SetFrequency(82.4);
        oscillator.Reset(0.0);
        float[] block = new float[Spectrum.SampleRate * 4];

        //Act
        oscillator.Render(block);
        double measured = TimeToFall(block, 30.0);

        //Assert
        measured.Should().BeApproximately(1.56, 0.35);
    }

    [Theory]
    [InlineData(0.1, 439.5, 0.62)]
    [InlineData(0.5, 929.1, 1.03)]
    [InlineData(0.9, 491.3, 4.3)]
    public void The_decay_time_matches_the_reference_at_all_three_damping_settings(
        double damping, double frequency, double expectedSeconds)
    {
        //Arrange
        // MEASURED (round 2, item 29): the reference's -60 dB times, each read at its own pitch,
        // which is why the pitch is part of the case. The law that fits all three is a loss per trip
        // proportional to the CUBE of (1 - damping).
        Pluck1Oscillator oscillator = Build(damping, 0.5);

        //Act
        oscillator.SetFrequency(frequency);

        //Assert
        oscillator.DecayTimeSeconds.Should().BeApproximately(expectedSeconds, expectedSeconds * 0.1);
    }

    [Fact]
    public void DecayTimeSeconds_falls_as_the_pitch_rises_at_one_damping_setting()
    {
        //Arrange
        Pluck1Oscillator low = Build(0.5, 0.5);
        Pluck1Oscillator high = Build(0.5, 0.5);

        //Act
        low.SetFrequency(110.0);
        high.SetFrequency(880.0);

        //Assert
        high.DecayTimeSeconds.Should().BeLessThan(low.DecayTimeSeconds);
    }

    [Fact]
    public void PluckType_of_one_is_brighter_than_pluck_type_of_zero()
    {
        //Arrange
        Pluck1Oscillator mellow = Build(0.5, 0.0);
        Pluck1Oscillator bright = Build(0.5, 1.0);
        mellow.SetFrequency(116.5);
        bright.SetFrequency(116.5);
        float[] mellowBlock = new float[Spectrum.SampleRate * 2];
        float[] brightBlock = new float[Spectrum.SampleRate * 2];
        mellow.Reset(0.0);
        bright.Reset(0.0);

        //Act
        mellow.Render(mellowBlock);
        bright.Render(brightBlock);

        //Assert
        Spectrum.PowerCentroid(brightBlock, brightBlock.Length)
            .Should().BeGreaterThan(Spectrum.PowerCentroid(mellowBlock, mellowBlock.Length) * 2.0);
    }

    [Theory]
    [InlineData(0.1, 0.5, 82.4, 324.6)]
    [InlineData(0.5, 0.5, 92.5, 257.0)]
    [InlineData(0.9, 0.5, 103.8, 237.3)]
    [InlineData(0.5, 0.0, 116.5, 184.6)]
    [InlineData(0.5, 1.0, 130.8, 730.3)]
    public void Render_matches_the_reference_players_brightness_within_two_decibels(
        double damping, double pluckType, double frequency, double referenceCentroidHz)
    {
        //Arrange
        // Every case here is one the reference player was recorded playing; see the measurements
        // document, item 12. Two dB is the acceptance target the plan sets for synth waveforms.
        Pluck1Oscillator oscillator = Build(damping, pluckType);
        oscillator.SetFrequency(frequency);
        oscillator.Reset(0.0);
        float[] block = new float[Spectrum.SampleRate * 2];

        //Act
        oscillator.Render(block);
        double centroid = Spectrum.PowerCentroid(block, block.Length);

        //Assert
        (20.0 * Math.Log10(centroid / referenceCentroidHz)).Should().BeInRange(-2.0, 2.0);
    }

    [Fact]
    public void Render_produces_the_same_string_from_the_same_seed()
    {
        //Arrange
        Pluck1Oscillator first = Build(0.5, 1.0);
        Pluck1Oscillator second = Build(0.5, 1.0);
        first.Seed = 42u;
        second.Seed = 42u;
        first.SetFrequency(220.0);
        second.SetFrequency(220.0);
        float[] a = new float[8192];
        float[] b = new float[8192];
        first.Reset(0.0);
        second.Reset(0.0);

        //Act
        first.Render(a);
        second.Render(b);

        //Assert
        a.Should().Equal(b);
    }

    [Fact]
    public void Render_produces_a_different_string_from_a_different_seed()
    {
        //Arrange
        Pluck1Oscillator first = Build(0.5, 1.0);
        Pluck1Oscillator second = Build(0.5, 1.0);
        first.Seed = 42u;
        second.Seed = 43u;
        first.SetFrequency(220.0);
        second.SetFrequency(220.0);
        float[] a = new float[8192];
        float[] b = new float[8192];
        first.Reset(0.0);
        second.Reset(0.0);

        //Act
        first.Render(a);
        second.Render(b);

        //Assert
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Reset_at_a_different_phase_decorrelates_two_strings()
    {
        //Arrange
        Pluck1Oscillator first = Build(0.5, 0.0);
        Pluck1Oscillator second = Build(0.5, 0.0);
        first.SetFrequency(220.0);
        second.SetFrequency(220.0);
        float[] a = new float[4096];
        float[] b = new float[4096];

        //Act
        first.Reset(0.0);
        first.Render(a);
        second.Reset(0.37);
        second.Render(b);

        //Assert
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Render_before_the_first_reset_is_silent()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(220.0);
        float[] block = new float[1024];

        //Act
        oscillator.Render(block);

        //Assert
        Spectrum.Rms(block, 0, block.Length).Should().Be(0.0);
    }

    [Fact]
    public void SetFrequency_rejects_a_pitch_below_the_lowest_string()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);

        //Act
        Action act = () => oscillator.SetFrequency(4.0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFrequency_rejects_a_pitch_above_a_third_of_the_sample_rate()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);

        //Act
        Action act = () => oscillator.SetFrequency(20000.0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFrequency_accepts_the_highest_note_a_keyboard_can_send()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();
        oscillator.SetSampleRate(44100);

        //Act
        Action act = () => oscillator.SetFrequency(12543.85);   // MIDI note 127

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Damping_clamps_a_value_outside_the_range()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();

        //Act
        oscillator.Damping = 3.0;

        //Assert
        oscillator.Damping.Should().Be(1.0);
    }

    [Fact]
    public void PluckType_clamps_a_value_outside_the_range()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();

        //Act
        oscillator.PluckType = -3.0;

        //Assert
        oscillator.PluckType.Should().Be(0.0);
    }

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new Pluck1Oscillator().Waveform.Should().Be("pluck1");

    private static Pluck1Oscillator Build(double damping, double pluckType)
    {
        Pluck1Oscillator oscillator = new Pluck1Oscillator { Damping = damping, PluckType = pluckType };
        oscillator.SetSampleRate(Spectrum.SampleRate);
        return oscillator;
    }

    private static double LevelAfter(double damping, double seconds)
    {
        Pluck1Oscillator oscillator = Build(damping, 0.5);
        oscillator.SetFrequency(220.0);
        oscillator.Reset(0.0);

        int window = (int)(Spectrum.SampleRate * DecayMeasurementWindowSeconds);
        float[] block = new float[(int)(Spectrum.SampleRate * seconds) + window];
        oscillator.Render(block);

        return Spectrum.Rms(block, block.Length - window, window);
    }

    private static double TimeToFall(float[] block, double decibels)
    {
        int window = (int)(Spectrum.SampleRate * DecayMeasurementWindowSeconds);
        double start = Spectrum.Rms(block, 0, window);
        double target = start / Math.Pow(10.0, decibels / 20.0);

        for (int offset = 0; offset + window <= block.Length; offset += window)
        {
            if (Spectrum.Rms(block, offset, window) < target)
            {
                return offset / (double)Spectrum.SampleRate;
            }
        }

        return double.PositiveInfinity;
    }

    private static double PitchByAutocorrelation(float[] block, double expected)
    {
        int expectedLag = (int)Math.Round(Spectrum.SampleRate / expected);
        int lowest = (int)(expectedLag * 0.9);
        int highest = (int)(expectedLag * 1.1);
        int window = block.Length - highest;

        int best = lowest;
        double bestScore = double.MinValue;
        for (int lag = lowest; lag <= highest; lag++)
        {
            double score = 0.0;
            for (int i = 0; i < window; i++)
            {
                score += (double)block[i] * block[i + lag];
            }

            if (score > bestScore) { bestScore = score; best = lag; }
        }

        return Spectrum.SampleRate / (double)best;
    }

    private static double FundamentalOf(float[] block, double expected)
    {
        double[] magnitudes = Spectrum.WindowedMagnitudes(block, 0);
        int expectedBin = (int)Math.Round(expected * Spectrum.BlockLength / Spectrum.SampleRate);
        int low = Math.Max(2, (int)(expectedBin * 0.8));
        int high = Math.Min(magnitudes.Length - 2, (int)(expectedBin * 1.2) + 2);

        int peak = low;
        for (int i = low; i <= high; i++)
        {
            if (magnitudes[i] > magnitudes[peak]) { peak = i; }
        }

        // Log-parabolic interpolation between the peak bin and its neighbours.
        double before = Math.Log(Math.Max(magnitudes[peak - 1], 1e-30));
        double at = Math.Log(Math.Max(magnitudes[peak], 1e-30));
        double after = Math.Log(Math.Max(magnitudes[peak + 1], 1e-30));
        double delta = 0.5 * (before - after) / (before - (2.0 * at) + after);

        return (peak + delta) * Spectrum.SampleRate / Spectrum.BlockLength;
    }
}

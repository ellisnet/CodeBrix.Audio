using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="SineOscillator" />: one partial and nothing else, at the pitch that was
/// asked for.
/// </summary>
public class SineOscillatorTests
{
    private const int FundamentalBin = 171;

    [Fact]
    public void Render_puts_the_fundamental_on_the_requested_frequency()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        Spectrum.PeakBin(magnitudes).Should().Be(FundamentalBin);
    }

    [Fact]
    public void Render_produces_a_spectrum_with_nothing_but_the_fundamental_in_it()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        // -65 dB is the floor of a single-precision FFT this long, not of the oscillator.
        Spectrum.AliasFloorDb(magnitudes, FundamentalBin, 10000.0).Should().BeLessThan(-65.0);
    }

    [Fact]
    public void Render_swings_the_full_range_and_no_further()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(440.0);
        float[] block = new float[Spectrum.SampleRate];
        oscillator.Reset(0.0);

        //Act
        oscillator.Render(block);

        //Assert
        Spectrum.Rms(block, 0, block.Length).Should().BeApproximately(0.70711, 1e-3);
    }

    [Fact]
    public void Reset_starts_the_waveform_at_the_phase_it_was_given()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(100.0);
        float[] block = new float[4];

        //Act
        oscillator.Reset(0.25);
        oscillator.Render(block);

        //Assert
        block[0].Should().BeApproximately(1.0f, 1e-6f);
    }

    [Fact]
    public void Reset_takes_only_the_fractional_part_of_the_phase()
    {
        //Arrange
        SineOscillator wholeCycles = new SineOscillator();
        SineOscillator fraction = new SineOscillator();
        wholeCycles.SetSampleRate(Spectrum.SampleRate);
        fraction.SetSampleRate(Spectrum.SampleRate);
        wholeCycles.SetFrequency(100.0);
        fraction.SetFrequency(100.0);
        float[] first = new float[64];
        float[] second = new float[64];

        //Act
        wholeCycles.Reset(3.25);
        wholeCycles.Render(first);
        fraction.Reset(0.25);
        fraction.Render(second);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void SetSampleRate_rejects_a_rate_that_is_not_positive()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();

        //Act
        Action act = () => oscillator.SetSampleRate(0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFrequency_rejects_a_negative_frequency()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();

        //Act
        Action act = () => oscillator.SetFrequency(-1.0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new SineOscillator().Waveform.Should().Be("sine");

    [Fact]
    public void Render_of_an_empty_block_does_nothing()
    {
        //Arrange
        SineOscillator oscillator = new SineOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(440.0);

        //Act
        Action act = () => oscillator.Render(Array.Empty<float>());

        //Assert
        act.Should().NotThrow();
    }
}

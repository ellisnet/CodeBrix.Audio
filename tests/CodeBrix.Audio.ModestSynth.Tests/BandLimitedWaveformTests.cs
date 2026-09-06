using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The band-limiting claim, measured: <see cref="SawOscillator" />, <see cref="SquareOscillator" />
/// and <see cref="TriangleOscillator" /> keep the aliases a naive oscillator folds into the audible
/// band far enough down to be inaudible, and they do it without moving the pitch.
/// </summary>
/// <remarks>
/// The thresholds are stated, not derived: an alias floor of -50 dB below the fundamental anywhere
/// under 10 kHz for the two shapes with a step discontinuity, and -65 dB for the triangle, which
/// only has a corner. Measured on this implementation the figures are -56, -58 and -73 dB, so the
/// thresholds carry several dB of headroom. The naive shapes rendered by
/// <see cref="NaiveWaveforms" /> sit at -32, -32 and -64 dB on the same measurement, which is what
/// makes the comparison tests meaningful rather than tautological.
/// </remarks>
public class BandLimitedWaveformTests
{
    private const int FundamentalBin = 171;      // about 1002 Hz at 48 kHz over an 8192-sample block
    private const double AudibleLimitHz = 10000.0;
    private const double StepShapeFloorDb = -50.0;
    private const double CornerShapeFloorDb = -65.0;

    [Theory]
    [InlineData("saw")]
    [InlineData("square")]
    [InlineData("triangle")]
    public void Render_puts_the_fundamental_on_the_requested_frequency(string waveform)
    {
        //Arrange
        IModestOscillator oscillator = Build(waveform);

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        Spectrum.PeakBin(magnitudes).Should().Be(FundamentalBin);
    }

    [Theory]
    [InlineData("saw", StepShapeFloorDb)]
    [InlineData("square", StepShapeFloorDb)]
    [InlineData("triangle", CornerShapeFloorDb)]
    public void Render_keeps_aliases_below_the_stated_floor(string waveform, double floorDb)
    {
        //Arrange
        IModestOscillator oscillator = Build(waveform);

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        Spectrum.AliasFloorDb(magnitudes, FundamentalBin, AudibleLimitHz).Should().BeLessThan(floorDb);
    }

    [Theory]
    [InlineData("saw")]
    [InlineData("square")]
    public void Render_aliases_at_least_ten_decibels_lower_than_the_naive_shape(string waveform)
    {
        // The triangle is excluded here on purpose: at a kilohertz even a naive triangle's aliases
        // are below what a single-precision FFT of this length can see, so there is nothing to
        // compare. Render_triangle_aliases_lower_than_the_naive_shape_where_it_can_be_seen makes
        // the same point at a pitch where the difference is measurable.
        //Arrange
        IModestOscillator oscillator = Build(waveform);
        float[] naive = NaiveWaveforms.Render(waveform, Spectrum.BinFrequency(FundamentalBin));

        //Act
        double bandLimited = Spectrum.AliasFloorDb(
            Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator)), FundamentalBin, AudibleLimitHz);
        double unlimited = Spectrum.AliasFloorDb(
            Spectrum.Magnitudes(naive), FundamentalBin, AudibleLimitHz);

        //Assert
        bandLimited.Should().BeLessThan(unlimited - 10.0);
    }

    [Fact]
    public void Render_triangle_aliases_lower_than_the_naive_shape_where_it_can_be_seen()
    {
        //Arrange
        const int Bin = 683;     // about 4 kHz, where a naive triangle folds audibly
        TriangleOscillator oscillator = new TriangleOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(Bin));
        float[] naive = NaiveWaveforms.Render("triangle", Spectrum.BinFrequency(Bin));

        //Act
        double bandLimited = Spectrum.AliasFloorDb(
            Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator)), Bin, AudibleLimitHz);
        double unlimited = Spectrum.AliasFloorDb(Spectrum.Magnitudes(naive), Bin, AudibleLimitHz);

        //Assert
        bandLimited.Should().BeLessThan(unlimited - 25.0);
    }

    [Theory]
    [InlineData(683)]     // about 4 kHz - the eighth harmonic is already past Nyquist
    [InlineData(1367)]    // about 8 kHz - only two harmonics fit below Nyquist
    public void Render_stays_band_limited_at_pitches_where_a_naive_saw_falls_apart(int bin)
    {
        //Arrange
        SawOscillator oscillator = new SawOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(bin));
        float[] naive = NaiveWaveforms.Render("saw", Spectrum.BinFrequency(bin));

        //Act
        double bandLimited = Spectrum.AliasFloorDb(
            Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator)), bin, AudibleLimitHz);
        double unlimited = Spectrum.AliasFloorDb(Spectrum.Magnitudes(naive), bin, AudibleLimitHz);

        //Assert
        bandLimited.Should().BeLessThan(unlimited - 20.0);
    }

    [Fact]
    public void Square_at_the_default_width_carries_odd_harmonics_only()
    {
        //Arrange
        SquareOscillator oscillator = new SquareOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        double even = magnitudes[FundamentalBin * 2] / magnitudes[FundamentalBin];
        even.Should().BeLessThan(0.001);
    }

    [Fact]
    public void Square_at_a_quarter_width_brings_the_even_harmonics_back()
    {
        //Arrange
        SquareOscillator oscillator = new SquareOscillator { PulseWidth = 0.25 };
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        double even = magnitudes[FundamentalBin * 2] / magnitudes[FundamentalBin];
        even.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Square_clamps_a_pulse_width_outside_the_range_it_can_band_limit()
    {
        //Arrange
        SquareOscillator oscillator = new SquareOscillator();

        //Act
        oscillator.PulseWidth = 2.0;

        //Assert
        oscillator.PulseWidth.Should().Be(0.99);
    }

    [Fact]
    public void Triangle_carries_odd_harmonics_only()
    {
        //Arrange
        TriangleOscillator oscillator = new TriangleOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        double even = magnitudes[FundamentalBin * 2] / magnitudes[FundamentalBin];
        even.Should().BeLessThan(0.001);
    }

    [Fact]
    public void Triangle_holds_its_level_across_five_octaves()
    {
        //Arrange
        TriangleOscillator low = new TriangleOscillator();
        TriangleOscillator high = new TriangleOscillator();
        low.SetSampleRate(Spectrum.SampleRate);
        high.SetSampleRate(Spectrum.SampleRate);
        low.SetFrequency(55.0);
        high.SetFrequency(1760.0);
        float[] lowBlock = new float[Spectrum.SampleRate];
        float[] highBlock = new float[Spectrum.SampleRate];
        low.Reset(0.0);
        high.Reset(0.0);

        //Act
        low.Render(lowBlock);
        high.Render(highBlock);

        //Assert
        // A triangle's RMS is 1/sqrt(3). The "integrate a square" construction droops badly at one
        // end of this range or the other; the polyBLAMP one does not.
        Spectrum.Rms(lowBlock, 0, lowBlock.Length).Should().BeApproximately(0.5774, 0.02);
        Spectrum.Rms(highBlock, 0, highBlock.Length).Should().BeApproximately(0.5774, 0.02);
    }

    [Theory]
    [InlineData("saw", 0.5774, 0.02)]
    [InlineData("square", 1.0, 0.02)]
    [InlineData("triangle", 0.5774, 0.02)]
    public void Render_produces_the_level_the_shape_implies(string waveform, double expectedRms, double tolerance)
    {
        //Arrange
        IModestOscillator oscillator = Build(waveform);
        oscillator.SetFrequency(220.0);
        float[] block = new float[Spectrum.SampleRate];
        oscillator.Reset(0.0);

        //Act
        oscillator.Render(block);

        //Assert
        Spectrum.Rms(block, 0, block.Length).Should().BeApproximately(expectedRms, tolerance);
    }

    private static IModestOscillator Build(string waveform)
    {
        IModestOscillator oscillator = ModestOscillatorFactory.Create(Parse(waveform));
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));
        return oscillator;
    }

    private static ModestWaveform Parse(string waveform)
    {
        ModestWaveforms.TryParse(waveform, out ModestWaveform parsed);
        return parsed;
    }
}

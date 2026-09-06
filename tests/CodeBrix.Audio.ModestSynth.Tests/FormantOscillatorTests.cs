using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The <c>formant</c> waveform, against the reference player's own twenty-partial table.
/// </summary>
/// <remarks>
/// MEASURED (round 2, item 26): the reference's formant oscillator has NO attributes - twenty-nine
/// cases varying twenty-one spellings of a formant control produced bit-identical audio - and its
/// spectrum is one formant region near 2.4 kHz over the note's own fundamental.
/// </remarks>
public class FormantOscillatorTests
{
    private static readonly double[] Measured =
    {
        0.0, -15.4, -22.8, -26.3, -28.1, -28.6, -27.3, -21.2, -10.6, -22.6,
        -21.9, -28.9, -41.1, -48.7, -54.8, -60.3, -65.4, -68.9, -72.0, -75.3,
    };

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new FormantOscillator().Waveform.Should().Be(ModestWaveforms.Formant);

    [Fact]
    public void The_spectrum_is_the_reference_players_own_partial_table()
    {
        //Arrange
        // The measurement was taken at 261.626 Hz, so the probe runs at the bin nearest to it.
        int bin = (int)Math.Round(261.626 / Spectrum.BinFrequency(1));
        double fundamental = Spectrum.BinFrequency(bin);

        FormantOscillator oscillator = new FormantOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(fundamental);

        //Act
        double[] magnitudes = Spectrum.PreciseMagnitudes(Spectrum.RenderBlock(oscillator));
        double reference = magnitudes[bin];

        //Assert
        for (int partial = 1; partial <= Measured.Length; partial++)
        {
            int index = bin * partial;
            if (index >= magnitudes.Length) { break; }

            (20.0 * Math.Log10(magnitudes[index] / reference))
                .Should().BeApproximately(Measured[partial - 1], 1.5);
        }
    }

    [Fact]
    public void The_formant_region_stays_near_two_point_four_kilohertz_at_any_pitch()
    {
        //Arrange
        // The measured table is read as a spectral envelope in HERTZ, which is what a formant is: the
        // resonance holds still while the harmonics move through it.
        double low = FormantOscillator.EnvelopeAt(2356.0);

        //Assert
        low.Should().BeGreaterThan(FormantOscillator.EnvelopeAt(1200.0));
        low.Should().BeGreaterThan(FormantOscillator.EnvelopeAt(4000.0));
    }

    [Fact]
    public void The_envelope_holds_at_the_fundamental_below_the_measured_range() =>
        FormantOscillator.EnvelopeAt(100.0).Should().Be(1.0);

    [Fact]
    public void The_envelope_falls_at_eighteen_decibels_an_octave_above_the_measured_range()
    {
        //Arrange
        double top = FormantOscillator.MeasuredFundamentalHz * FormantOscillator.MeasuredPartialCount;

        //Act
        double decibels = 20.0 * Math.Log10(FormantOscillator.EnvelopeAt(top * 2.0) /
            FormantOscillator.EnvelopeAt(top));

        //Assert
        decibels.Should().BeApproximately(FormantOscillator.RolloffDecibelsPerOctave, 1e-9);
    }

    [Fact]
    public void The_level_is_the_measured_five_point_eight_five_decibels_above_a_sine()
    {
        //Arrange
        FormantOscillator formant = new FormantOscillator();
        SineOscillator sine = new SineOscillator();

        foreach (IModestOscillator oscillator in new IModestOscillator[] { formant, sine })
        {
            oscillator.SetSampleRate(Spectrum.SampleRate);
            oscillator.SetFrequency(261.626);
        }

        //Act
        double formantRms = Spectrum.Rms(Spectrum.RenderBlock(formant), 0, Spectrum.BlockLength);
        double sineRms = Spectrum.Rms(Spectrum.RenderBlock(sine), 0, Spectrum.BlockLength);

        //Assert
        (20.0 * Math.Log10(formantRms / sineRms)).Should().BeApproximately(5.85, 0.2);
    }

    [Fact]
    public void No_partial_reaches_Nyquist_at_the_top_of_the_keyboard()
    {
        //Arrange
        FormantOscillator oscillator = new FormantOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(8000.0);

        //Act
        double[] magnitudes = Spectrum.PreciseMagnitudes(Spectrum.RenderBlock(oscillator));

        //Assert - only the fundamental and the second partial fit below Nyquist at 8 kHz.
        oscillator.ActivePartialCount.Should().Be(2);
        magnitudes.Length.Should().BeGreaterThan(0);
    }
}

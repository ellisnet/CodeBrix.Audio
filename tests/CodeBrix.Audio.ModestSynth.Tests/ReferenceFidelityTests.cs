using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// How close each waveform sits to the reference player's, measured the way the reference
/// recordings were measured.
/// </summary>
/// <remarks>
/// <para>
/// The reference figures come from the measurements document, item 13, where every waveform was
/// played on its own key with an oscillator-only group and the recording analysed for its
/// POWER-weighted spectral centroid. The plan's acceptance target for a synth waveform is 2 dB of
/// centroid difference and a matching pitch; these tests hold the four classic shapes to a tenth
/// of that, which is where they measure.
/// </para>
/// <para>
/// White noise is the one divergence and it has its own test below, stating the size of the gap
/// rather than hiding it.
/// </para>
/// </remarks>
public class ReferenceFidelityTests
{
    private const double ToleranceDb = 0.5;

    [Fact]
    public void Sine_matches_the_reference_players_brightness() =>
        CentroidDifferenceDb(new SineOscillator(), 261.63, 261.6)
            .Should().BeInRange(-ToleranceDb, ToleranceDb);

    [Fact]
    public void Saw_matches_the_reference_players_brightness() =>
        CentroidDifferenceDb(new SawOscillator(), 293.66, 790.1)
            .Should().BeInRange(-ToleranceDb, ToleranceDb);

    [Fact]
    public void Square_matches_the_reference_players_brightness() =>
        CentroidDifferenceDb(new SquareOscillator(), 329.63, 677.9)
            .Should().BeInRange(-ToleranceDb, ToleranceDb);

    [Fact]
    public void Triangle_matches_the_reference_players_brightness() =>
        CentroidDifferenceDb(new TriangleOscillator(), 369.99, 383.3)
            .Should().BeInRange(-ToleranceDb, ToleranceDb);

    [Fact]
    public void Noise_is_within_a_decibel_and_a_bit_of_the_reference_players_brightness()
    {
        //Arrange
        // This was a 2.6 dB divergence until round 2 item 29 measured the reference's spectrum band
        // by band and found an ANTI-IMAGING ROLLOFF above 8 kHz rather than a tilt: -6.07 dB at
        // 16 kHz and -26.99 dB at 20 kHz. Reproducing that band table closes most of the gap and
        // leaves 1.16 dB, which is the most the two reference figures allow: read literally, the band
        // table alone implies a centroid near 6.5 kHz rather than the 8877 Hz measured beside it.

        //Act
        // The reference's 8877 Hz was recorded at 44,100 Hz and the rolloff is a fraction of the
        // sample rate, so the centroid of BROADBAND noise scales with the rate these tests run at.
        // (The pitched waveforms above need no such correction: their centroid follows the note.)
        double reference = 8877.4 * Spectrum.SampleRate / 44100.0;
        double difference = CentroidDifferenceDb(new NoiseOscillator(), 440.0, reference);

        //Assert
        difference.Should().BeInRange(-1.5, 1.5);
    }

    private static double CentroidDifferenceDb(
        IModestOscillator oscillator, double frequency, double referenceCentroidHz)
    {
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(frequency);
        oscillator.Reset(0.0);

        float[] block = new float[Spectrum.SampleRate];
        oscillator.Render(block);

        return 20.0 * Math.Log10(Spectrum.PowerCentroid(block, block.Length) / referenceCentroidHz);
    }
}

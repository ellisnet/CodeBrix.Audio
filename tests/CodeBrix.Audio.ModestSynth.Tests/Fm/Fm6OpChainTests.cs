using System;
using CodeBrix.Audio.ModestSynth.Fm;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The modulator-to-modulator chains of algorithm 1: op6 -&gt; op5 -&gt; op4 -&gt; op3, measured under
/// load rather than inherited from the published chart.
/// </summary>
/// <remarks>
/// MEASURED (round 4, item 56). Each link is carried by the intermediate operator's OWN OUTPUT scaled
/// by its level, so an operator whose downstream neighbour is silent contributes nothing at all: in the
/// reference, op5 at level 1.0 with op4 silent was bit-identical to the bare carrier, and op6 at 1.0
/// with op5 silent was bit-identical to op4 alone - to 0.000 dB in RMS and 0.00 dB on every significant
/// spectral line. The four "identical" cases are the load-bearing assertions here; an implementation
/// that added the modulators' phases together instead would fail every one of them.
/// </remarks>
public class Fm6OpChainTests
{
    private const int Rate = FmSignal.SampleRate;
    private const int Length = Spectrum.BlockLength;

    // A fundamental whose period fits the analysis block a whole number of times, so every harmonic
    // of it lands on a bin of its own and nothing leaks between the six operators' lines.
    private const int FundamentalBin = 19;

    // The item-56 ladder: algorithm 1 with op1 and op2 silent, so only the 3 <- 4 <- 5 <- 6 branch
    // sounds, and the carrier at ratio 1 with the three modulators at ratios 2, 3 and 5.
    private static float[] Ladder(double op4Level, double op5Level, double op6Level)
    {
        Fm6OpOscillator oscillator = new Fm6OpOscillator { Algorithm = 1 };
        oscillator.SetSampleRate(Rate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        oscillator.GetOperator(1).Level = 0.0;
        oscillator.GetOperator(2).Level = 0.0;

        oscillator.GetOperator(3).Ratio = 1.0;
        oscillator.GetOperator(3).Level = 1.0;
        oscillator.GetOperator(4).Ratio = 2.0;
        oscillator.GetOperator(4).Level = op4Level;
        oscillator.GetOperator(5).Ratio = 3.0;
        oscillator.GetOperator(5).Level = op5Level;
        oscillator.GetOperator(6).Ratio = 5.0;
        oscillator.GetOperator(6).Level = op6Level;

        oscillator.Reset(0.0);

        float[] block = new float[Length];
        oscillator.Render(block);
        return block;
    }

    [Fact]
    public void A_modulator_whose_downstream_operator_is_silent_is_inaudible()
    {
        //Arrange - the reference's cases A00, A13, A14 and A15.
        float[] carrierAlone = Ladder(0.0, 0.0, 0.0);

        //Act
        float[] withOp5 = Ladder(0.0, 1.0, 0.0);
        float[] withOp6 = Ladder(0.0, 0.0, 1.0);
        float[] withBoth = Ladder(0.0, 1.0, 1.0);

        //Assert - and the carrier is really sounding, so "identical" is not "identically silent".
        Rms(carrierAlone).Should().BeGreaterThan(0.1);
        MaximumDifference(carrierAlone, withOp5).Should().BeLessThan(1e-9);
        MaximumDifference(carrierAlone, withOp6).Should().BeLessThan(1e-9);
        MaximumDifference(carrierAlone, withBoth).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void A_gap_in_the_middle_of_the_chain_cuts_everything_above_it()
    {
        //Arrange - the reference's case A16 against A02: op6 at full level reaches nothing while op5
        //is silent, so the sound is exactly op4 modulating the carrier on its own.
        float[] op4Only = Ladder(0.30, 0.0, 0.0);

        //Act
        float[] op4AndOp6 = Ladder(0.30, 0.0, 1.00);

        //Assert - op4 really is modulating, so the two are not identically silent either.
        Spectrum.PowerCentroid(op4Only, op4Only.Length)
            .Should().BeGreaterThan(Spectrum.BinFrequency(FundamentalBin) * 1.5);
        MaximumDifference(op4Only, op4AndOp6).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void Raising_the_chain_opens_the_spectrum_without_moving_the_level()
    {
        //Arrange
        // MEASURED (round 4, item 56): the whole ladder moved the spectral centroid from 110 Hz to
        // 7261 Hz and the line count from 1 to 89 while the RMS stayed inside 1.9 dB. A chained
        // modulator changes the timbre, not the level.
        float[] carrierAlone = Ladder(0.0, 0.0, 0.0);

        //Act
        float[] whole = Ladder(0.30, 0.30, 1.00);

        //Assert
        double bare = Spectrum.PowerCentroid(carrierAlone, carrierAlone.Length);
        double laddered = Spectrum.PowerCentroid(whole, whole.Length);

        laddered.Should().BeGreaterThan(bare * 8.0);
        (20.0 * Math.Log10(Rms(whole) / Rms(carrierAlone))).Should().BeInRange(-3.0, 3.0);
    }

    [Fact]
    public void Every_operator_runs_its_own_envelope_at_the_same_time()
    {
        //Arrange
        // MEASURED (round 4, item 56): six carriers at ratios 1 to 6 with six different envelopes gave
        // six sustains landing on 20*log10(fmOpNSustain) exactly - 0.00, -6.00, -12.00, -20.00, 0.00
        // and -2.50 dB. Algorithm 32 is the one where all six operators are carriers.
        double[] sustains = [1.0, 0.5, 0.25, 0.1, 1.0, 0.75];

        Fm6OpOscillator oscillator = new Fm6OpOscillator { Algorithm = 32 };
        oscillator.SetSampleRate(Rate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        for (int number = 1; number <= 6; number++)
        {
            Patch.ModestFmOperator settings = oscillator.GetOperator(number);
            settings.Ratio = number;
            settings.Level = 1.0;
            settings.Attack = 0.001;
            settings.Decay = 0.01;
            settings.Sustain = sustains[number - 1];
            settings.Release = 0.02;
        }

        //Act - well past the 10 ms decay, so every operator is on its sustain.
        oscillator.Reset(0.0);
        oscillator.NoteOn(127);
        oscillator.Render(new float[Rate / 2]);

        float[] block = new float[Spectrum.BlockLength];
        oscillator.Render(block);

        //Assert - each operator owns one harmonic, and its line sits at its own sustain.
        double[] magnitudes = Spectrum.PreciseMagnitudes(block);
        double reference = magnitudes[FundamentalBin];

        for (int number = 1; number <= 6; number++)
        {
            (20.0 * Math.Log10(magnitudes[FundamentalBin * number] / reference))
                .Should().BeApproximately(20.0 * Math.Log10(sustains[number - 1]), 0.2);
        }
    }

    [Fact]
    public void The_voice_ends_on_the_longest_operator_release()
    {
        //Arrange
        // MEASURED (round 4, item 56): five operators at release 0.3 s were 90 dB down half a second
        // after the note-off while op5, at release 1.5 s, was only 22 dB down - the releases are per
        // operator and independent, and the voice's tail is the longest of them.
        Fm6OpOscillator oscillator = new Fm6OpOscillator { Algorithm = 32 };
        oscillator.SetSampleRate(Rate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        for (int number = 1; number <= 6; number++)
        {
            Patch.ModestFmOperator settings = oscillator.GetOperator(number);
            settings.Ratio = number;
            settings.Level = 1.0;
            settings.Attack = 0.001;
            settings.Decay = 0.0;
            settings.Sustain = 1.0;
            settings.Release = number == 5 ? 1.5 : 0.02;
        }

        //Act
        oscillator.Reset(0.0);
        oscillator.NoteOn(127);
        oscillator.Render(new float[Rate / 4]);
        oscillator.NoteOff();

        float[] shortReleases = new float[Rate / 2];
        oscillator.Render(shortReleases);

        //Assert - the five short operators are gone within the first tenth of a second and the one
        //long operator is still sounding half a second later; the voice has not finished.
        oscillator.IsFinished.Should().BeFalse();
        Rms(shortReleases, shortReleases.Length - 2048, 2048).Should().BeGreaterThan(1e-4);

        //Act - a second and a half after the key came up, everything is over.
        oscillator.Render(new float[Rate * 2]);

        //Assert
        oscillator.IsFinished.Should().BeTrue();
    }

    private static double Rms(float[] samples) => Rms(samples, 0, samples.Length);

    private static double Rms(float[] samples, int offset, int length)
    {
        double sum = 0.0;

        for (int i = offset; i < offset + length; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / length);
    }

    private static double MaximumDifference(float[] first, float[] second)
    {
        double worst = 0.0;

        for (int i = 0; i < first.Length; i++)
        {
            double difference = Math.Abs(first[i] - second[i]);
            if (difference > worst) { worst = difference; }
        }

        return worst;
    }
}

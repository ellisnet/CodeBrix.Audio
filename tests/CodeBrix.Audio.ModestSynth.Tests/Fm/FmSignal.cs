using System;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The measurements and the closed-form answers the six-operator tests are written against.
/// </summary>
/// <remarks>
/// <para>
/// Two of these matter more than the rest. <see cref="SumOfSines" /> accumulates phase exactly the
/// way the oscillator does - add the increment, subtract the whole cycles - so a test can demand a
/// sample-for-sample match rather than a statistical one. <see cref="BesselJ" /> is the textbook
/// series for the Bessel functions of the first kind, which is what says how much of a frequency
/// modulated tone is left in the carrier and how much has moved into the sidebands.
/// </para>
/// </remarks>
public static class FmSignal
{
    /// <summary>The sample rate the six-operator tests render at.</summary>
    public const int SampleRate = 48000;

    /// <summary>
    /// The sum of a set of sines, phase-accumulated exactly as the oscillator accumulates it.
    /// </summary>
    /// <param name="frequencies">One frequency per sine, in Hz.</param>
    /// <param name="amplitudes">One amplitude per sine, matching <paramref name="frequencies" />.</param>
    /// <param name="gain">What the sum is multiplied by.</param>
    /// <param name="startPhase">The phase every sine starts at, in cycles.</param>
    /// <param name="length">How many samples to produce.</param>
    /// <returns>The samples.</returns>
    public static float[] SumOfSines(double[] frequencies, double[] amplitudes, double gain,
        double startPhase, int length)
    {
        double[] phases = new double[frequencies.Length];
        for (int i = 0; i < phases.Length; i++) { phases[i] = startPhase; }

        float[] samples = new float[length];
        for (int n = 0; n < length; n++)
        {
            double mix = 0.0;
            for (int i = 0; i < frequencies.Length; i++)
            {
                mix += amplitudes[i] * Math.Sin(2.0 * Math.PI * phases[i]);

                double next = phases[i] + (frequencies[i] / SampleRate);
                if (next >= 1.0) { next -= Math.Floor(next); }
                phases[i] = next;
            }

            samples[n] = (float)(gain * mix);
        }

        return samples;
    }

    /// <summary>The largest absolute sample in a window centred on one point.</summary>
    /// <param name="samples">The rendered signal.</param>
    /// <param name="centre">The sample the window is centred on.</param>
    /// <param name="halfWidth">Half the window, in samples.</param>
    /// <returns>The peak magnitude in the window.</returns>
    public static double PeakAround(float[] samples, int centre, int halfWidth)
    {
        int from = centre - halfWidth < 0 ? 0 : centre - halfWidth;
        int to = centre + halfWidth > samples.Length ? samples.Length : centre + halfWidth;

        double peak = 0.0;
        for (int i = from; i < to; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            if (magnitude > peak) { peak = magnitude; }
        }

        return peak;
    }

    /// <summary>
    /// When a decaying signal first falls below a level, measured a window at a time so that the
    /// zero crossings of the tone underneath do not count as silence.
    /// </summary>
    /// <param name="samples">The rendered signal.</param>
    /// <param name="threshold">The peak level to fall below.</param>
    /// <param name="window">The window length in samples.</param>
    /// <returns>The time in seconds, or the length of the signal when it never falls that far.</returns>
    public static double TimeToFallBelow(float[] samples, double threshold, int window)
    {
        for (int start = 0; start + window <= samples.Length; start += window)
        {
            double peak = 0.0;
            for (int i = start; i < start + window; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                if (magnitude > peak) { peak = magnitude; }
            }

            if (peak < threshold) { return start / (double)SampleRate; }
        }

        return samples.Length / (double)SampleRate;
    }

    /// <summary>
    /// When a rising signal first reaches a level, measured a window at a time.
    /// </summary>
    /// <param name="samples">The rendered signal.</param>
    /// <param name="threshold">The peak level to reach.</param>
    /// <param name="window">The window length in samples.</param>
    /// <returns>The time in seconds, or the length of the signal when it never gets there.</returns>
    public static double TimeToRiseAbove(float[] samples, double threshold, int window)
    {
        for (int start = 0; start + window <= samples.Length; start += window)
        {
            double peak = 0.0;
            for (int i = start; i < start + window; i++)
            {
                double magnitude = Math.Abs(samples[i]);
                if (magnitude > peak) { peak = magnitude; }
            }

            if (peak >= threshold) { return (start + window) / (double)SampleRate; }
        }

        return samples.Length / (double)SampleRate;
    }

    /// <summary>
    /// The Bessel function of the first kind, which gives the amplitude of each sideband of a
    /// frequency modulated tone.
    /// </summary>
    /// <param name="order">The sideband number: 0 is the carrier, 1 the first pair, and so on.</param>
    /// <param name="argument">The modulation index in radians.</param>
    /// <returns>The relative amplitude of that sideband.</returns>
    /// <remarks>
    /// The ascending series, which converges quickly for the small indices these tests use.
    /// </remarks>
    public static double BesselJ(int order, double argument)
    {
        double half = argument / 2.0;
        double term = Math.Pow(half, order) / Factorial(order);
        double sum = term;

        for (int k = 1; k < 40; k++)
        {
            term *= -(half * half) / (k * (double)(k + order));
            sum += term;
            if (Math.Abs(term) < 1e-18) { break; }
        }

        return sum;
    }

    private static double Factorial(int n)
    {
        double result = 1.0;
        for (int i = 2; i <= n; i++) { result *= i; }
        return result;
    }
}

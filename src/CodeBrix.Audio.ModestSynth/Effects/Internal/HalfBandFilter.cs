using System;

namespace CodeBrix.Audio.ModestSynth.Effects.Internal;

/// <summary>
/// A symmetric half-band FIR low-pass: the filter that goes between the two rates of a doubling or
/// halving stage.
/// </summary>
/// <remarks>
/// <para>
/// A half-band filter cuts at a quarter of the rate it runs at, which is exactly the old Nyquist
/// frequency when a signal is being doubled or halved. Half of its taps are zero - the impulse
/// response is a sinc sampled at every other zero crossing - so only the centre tap and the
/// odd-offset taps do any work, and this implementation skips the rest.
/// </para>
/// <para>
/// The coefficients are a Blackman-windowed sinc, computed once for the type and shared; an
/// instance owns nothing but its delay history, so constructing one is cheap enough to do per
/// channel.
/// </para>
/// </remarks>
internal sealed class HalfBandFilter
{
    /// <summary>How many taps the filter uses. Must satisfy length = 4k + 3 for the zeros to fall right.</summary>
    internal const int TapCount = 31;

    private static readonly double[] Coefficients = BuildCoefficients();
    private static readonly int[] ActiveTaps = BuildActiveTaps(Coefficients);

    private readonly double[] history = new double[TapCount];

    private int position;

    /// <summary>The filter's group delay in samples at the rate it runs at.</summary>
    internal const int GroupDelaySamples = (TapCount - 1) / 2;

    /// <summary>Clears the delay history.</summary>
    internal void Reset()
    {
        Array.Clear(history, 0, history.Length);
        position = 0;
    }

    /// <summary>
    /// Filters one sample.
    /// </summary>
    /// <param name="value">The input sample.</param>
    /// <returns>The filtered sample.</returns>
    internal double Process(double value)
    {
        history[position] = value;

        double sum = 0.0;
        int length = history.Length;

        for (int i = 0; i < ActiveTaps.Length; i++)
        {
            int tap = ActiveTaps[i];
            int index = position - tap;
            if (index < 0) { index += length; }
            sum += Coefficients[tap] * history[index];
        }

        position++;
        if (position >= length) { position = 0; }

        return sum;
    }

    private static double[] BuildCoefficients()
    {
        double[] coefficients = new double[TapCount];
        int centre = (TapCount - 1) / 2;
        double sum = 0.0;

        for (int n = 0; n < TapCount; n++)
        {
            int m = n - centre;

            // A half-band impulse response is sinc(m/2): one half at the centre and a zero at every
            // even offset, which is where half the arithmetic goes away.
            double sinc = m == 0 ? 0.5 : Math.Sin(Math.PI * m * 0.5) / (Math.PI * m);

            double window =
                0.42 -
                (0.5 * Math.Cos(2.0 * Math.PI * n / (TapCount - 1))) +
                (0.08 * Math.Cos(4.0 * Math.PI * n / (TapCount - 1)));

            coefficients[n] = sinc * window;
            sum += coefficients[n];
        }

        // Unity at direct current, so an oversampled path neither lifts nor drops the level.
        for (int n = 0; n < TapCount; n++) { coefficients[n] /= sum; }

        return coefficients;
    }

    private static int[] BuildActiveTaps(double[] coefficients)
    {
        int count = 0;
        for (int n = 0; n < coefficients.Length; n++)
        {
            if (coefficients[n] != 0.0) { count++; }
        }

        int[] taps = new int[count];
        int next = 0;
        for (int n = 0; n < coefficients.Length; n++)
        {
            if (coefficients[n] != 0.0) { taps[next++] = n; }
        }

        return taps;
    }
}

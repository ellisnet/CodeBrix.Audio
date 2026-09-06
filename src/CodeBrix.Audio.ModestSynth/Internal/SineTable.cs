using System;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// One cycle of a sine, sampled once at start-up and read with linear interpolation.
/// </summary>
/// <remarks>
/// <para>
/// The additive oscillator sums up to 64 partials per sample. Calling <see cref="Math.Sin" /> that
/// many times per sample is what makes naive additive synthesis unusable; a table lookup is a
/// multiply, a truncation and a lerp, and it is what makes 64 partials affordable.
/// </para>
/// <para>
/// The table holds <see cref="Length" /> points plus one repeated point at the end, so the
/// interpolation never has to wrap and never has to branch. At that resolution the worst-case
/// interpolation error is about 3e-7 of full scale, roughly 130 dB down - far below anything a
/// float output can carry, and far below the single-precision FFT the tests measure with.
/// </para>
/// <para>
/// It is a static readonly array of doubles, built once, read-only afterwards and therefore safe
/// to read from any number of render threads at once. Reading it allocates nothing.
/// </para>
/// </remarks>
internal static class SineTable
{
    /// <summary>How many points one cycle is sampled at.</summary>
    internal const int Length = 4096;

    private static readonly double[] Values = Build();

    /// <summary>
    /// The sine of a phase given in CYCLES.
    /// </summary>
    /// <param name="phase">The phase in cycles, in [0, 1). Values outside that are wrapped.</param>
    /// <returns>The sine, in [-1, 1].</returns>
    internal static double Lookup(double phase)
    {
        double wrapped = phase - Math.Floor(phase);
        double x = wrapped * Length;
        int index = (int)x;
        if (index >= Length) { index = Length - 1; }

        double fraction = x - index;
        double low = Values[index];
        return low + ((Values[index + 1] - low) * fraction);
    }

    /// <summary>
    /// The sine of a phase already known to be in [0, 1) - the render-loop entry point.
    /// </summary>
    /// <param name="phase">The phase in cycles, in [0, 1).</param>
    /// <returns>The sine, in [-1, 1].</returns>
    internal static double LookupWrapped(double phase)
    {
        double x = phase * Length;
        int index = (int)x;
        if (index >= Length) { index = Length - 1; }
        else if (index < 0) { index = 0; }

        double fraction = x - index;
        double low = Values[index];
        return low + ((Values[index + 1] - low) * fraction);
    }

    private static double[] Build()
    {
        double[] values = new double[Length + 1];
        for (int i = 0; i < Length; i++)
        {
            values[i] = Math.Sin(2.0 * Math.PI * i / Length);
        }

        values[Length] = values[0];
        return values;
    }
}

using System;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// Measures the pitch of a rendered tone precisely enough to check bend arithmetic to within a cent.
/// </summary>
/// <remarks>
/// <para>
/// Counting zero crossings is good to a fraction of a percent, which is tens of cents - not enough.
/// This refines a crossing-count seed with the phase of a single Fourier bin measured twice, one hop
/// apart: the phase advances by exactly two pi times the frequency times the hop, so the phase
/// difference gives the frequency to a resolution set by the hop rather than by the block length.
/// Two passes are enough to land well inside a cent on a clean tone.
/// </para>
/// <para>
/// The hop bounds how far off the seed may be: a phase difference is only unambiguous while it stays
/// inside plus or minus pi, which at a hop of 1024 frames and 44.1 kHz is about twenty hertz.
/// </para>
/// </remarks>
internal static class PitchProbe
{
    private const int Hop = 1024;
    private const int Window = 8192;

    /// <summary>The frequency of a tone, in Hz.</summary>
    /// <param name="samples">The rendered channel.</param>
    /// <param name="offset">The first frame to analyse.</param>
    /// <param name="sampleRate">The sample rate.</param>
    /// <returns>The frequency in Hz, or 0 when the window holds no tone.</returns>
    public static double Hertz(float[] samples, int offset, int sampleRate)
    {
        if (offset < 0 || offset + Window + Hop > samples.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), "The analysis window falls outside the render.");
        }

        var seed = CrossingSeed(samples, offset, Window + Hop, sampleRate);

        if (seed <= 0.0)
        {
            return 0.0;
        }

        var estimate = seed;

        for (var pass = 0; pass < 2; pass++)
        {
            estimate = Refine(samples, offset, sampleRate, estimate);
        }

        return estimate;
    }

    /// <summary>The distance between two frequencies, in cents.</summary>
    /// <param name="measured">The measured frequency in Hz.</param>
    /// <param name="expected">The expected frequency in Hz.</param>
    /// <returns>The difference in cents, negative when the measurement is flat.</returns>
    public static double Cents(double measured, double expected) =>
        1200.0 * Math.Log2(measured / expected);

    private static double Refine(float[] samples, int offset, int sampleRate, double estimate)
    {
        var (firstReal, firstImaginary) = Bin(samples, offset, sampleRate, estimate);
        var (secondReal, secondImaginary) = Bin(samples, offset + Hop, sampleRate, estimate);

        // The phase the second window has gained on the first, wrapped into plus or minus pi.
        var real = (secondReal * firstReal) + (secondImaginary * firstImaginary);
        var imaginary = (secondImaginary * firstReal) - (secondReal * firstImaginary);
        var phase = Math.Atan2(imaginary, real);

        return estimate + (phase * sampleRate / (2.0 * Math.PI * Hop));
    }

    // One Fourier bin at an arbitrary frequency, windowed so that the two hops are comparable.
    private static (double Real, double Imaginary) Bin(
        float[] samples, int offset, int sampleRate, double frequency)
    {
        var real = 0.0;
        var imaginary = 0.0;
        var step = 2.0 * Math.PI * frequency / sampleRate;

        for (var index = 0; index < Window; index++)
        {
            var window = 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * index / Window));
            var value = samples[offset + index] * window;
            // The angle counts from the START OF THE RENDER, not from the start of this window, so
            // the two hops share one phase reference and their difference is the frequency error.
            var angle = step * (offset + index);

            real += value * Math.Cos(angle);
            imaginary -= value * Math.Sin(angle);
        }

        return (real, imaginary);
    }

    // A first guess from interpolated upward zero crossings, good to a fraction of a percent.
    private static double CrossingSeed(float[] samples, int offset, int length, int sampleRate)
    {
        var first = -1.0;
        var last = -1.0;
        var crossings = 0;

        for (var index = offset + 1; index < offset + length; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];

            if (!(previous <= 0f && current > 0f))
            {
                continue;
            }

            var fraction = previous == current ? 0.0 : -previous / (double)(current - previous);
            var position = index - 1 + fraction;

            if (crossings == 0)
            {
                first = position;
            }

            last = position;
            crossings++;
        }

        if (crossings < 2)
        {
            return 0.0;
        }

        var period = (last - first) / (crossings - 1);
        return period <= 0.0 ? 0.0 : sampleRate / period;
    }
}

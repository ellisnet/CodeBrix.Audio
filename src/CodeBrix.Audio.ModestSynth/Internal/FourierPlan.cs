using System;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// A radix-2 complex FFT whose twiddle factors are worked out once, in double precision, and then
/// reused for every transform of that size or smaller.
/// </summary>
/// <remarks>
/// <para>
/// This exists for building wavetable mip maps at LOAD time, not for anything on the audio thread.
/// It is separate from CodeBrix.Audio's own <c>FastFourierTransform</c> for two reasons: that one
/// is single precision, and a mip map is built by transforming forwards once and then inverting a
/// dozen times at a dozen different lengths, which is exactly the case one plan can serve.
/// </para>
/// <para>
/// A plan of size N can transform any power-of-two length up to and including N, because the
/// twiddle a stage needs is always <c>table[k * (N / stageLength)]</c> whatever the overall length
/// is.
/// </para>
/// <para>
/// Both directions are UNNORMALISED: a forward followed by an inverse multiplies by the transform
/// length. The caller divides, which is what lets a resampling inverse divide by the FORWARD
/// length instead of its own.
/// </para>
/// </remarks>
internal sealed class FourierPlan
{
    private readonly int size;
    private readonly double[] cosine;
    private readonly double[] sine;

    /// <summary>Builds a plan for transforms of up to <paramref name="size" /> points.</summary>
    /// <param name="size">The largest transform length, a power of two of at least 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is not a power of two of at least 2.</exception>
    internal FourierPlan(int size)
    {
        if (size < 2 || !IsPowerOfTwo(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size,
                "A Fourier plan's size must be a power of two of at least 2.");
        }

        this.size = size;
        int half = size / 2;
        cosine = new double[half];
        sine = new double[half];

        for (int i = 0; i < half; i++)
        {
            double angle = -2.0 * Math.PI * i / size;
            cosine[i] = Math.Cos(angle);
            sine[i] = Math.Sin(angle);
        }
    }

    /// <summary>The largest transform length this plan can serve.</summary>
    internal int Size => size;

    /// <summary>Whether a value is a power of two of at least 1.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true" /> when it is.</returns>
    internal static bool IsPowerOfTwo(int value) => value >= 1 && (value & (value - 1)) == 0;

    /// <summary>
    /// The smallest power of two that is at least <paramref name="value" />.
    /// </summary>
    /// <param name="value">The value to round up.</param>
    /// <returns>The rounded-up value; 1 for anything below 1.</returns>
    internal static int NextPowerOfTwo(int value)
    {
        if (value <= 1) { return 1; }

        int result = 1;
        while (result < value) { result <<= 1; }
        return result;
    }

    /// <summary>
    /// Transforms the first <paramref name="length" /> points of a complex signal in place.
    /// </summary>
    /// <param name="real">The real parts; at least <paramref name="length" /> long.</param>
    /// <param name="imaginary">The imaginary parts; at least <paramref name="length" /> long.</param>
    /// <param name="length">The transform length: a power of two, at least 2, at most <see cref="Size" />.</param>
    /// <param name="inverse"><see langword="true" /> for the inverse transform.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not a usable transform length.</exception>
    internal void Transform(double[] real, double[] imaginary, int length, bool inverse)
    {
        if (length < 2 || length > size || !IsPowerOfTwo(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length,
                "The transform length must be a power of two between 2 and the plan's size.");
        }

        for (int i = 1, j = 0; i < length; i++)
        {
            int bit = length >> 1;
            for (; (j & bit) != 0; bit >>= 1) { j ^= bit; }
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int stage = 2; stage <= length; stage <<= 1)
        {
            int half = stage >> 1;
            int step = size / stage;

            for (int start = 0; start < length; start += stage)
            {
                for (int k = 0; k < half; k++)
                {
                    int twiddle = k * step;
                    double wr = cosine[twiddle];
                    double wi = inverse ? -sine[twiddle] : sine[twiddle];

                    int a = start + k;
                    int b = a + half;

                    double br = real[b];
                    double bi = imaginary[b];
                    double tr = (br * wr) - (bi * wi);
                    double ti = (br * wi) + (bi * wr);

                    real[b] = real[a] - tr;
                    imaginary[b] = imaginary[a] - ti;
                    real[a] += tr;
                    imaginary[a] += ti;
                }
            }
        }
    }
}

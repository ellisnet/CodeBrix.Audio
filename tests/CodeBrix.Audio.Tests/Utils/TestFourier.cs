using System;
using System.Numerics;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>
/// Minimal double-precision FFT for tests, built on <see cref="Complex"/> from
/// System.Numerics. This replaces MathNet.Numerics' <c>Fourier.Forward</c>: the BCL ships
/// <see cref="Complex"/> but no transform, and the one FFT the test suite needed did not
/// justify an external dependency.
///
/// The transform is unscaled, which matches MathNet's <c>FourierOptions.AsymmetricScaling</c>
/// (forward unscaled, inverse scaled by 1/n). Callers here only read magnitudes, so the sign
/// of the exponent is irrelevant; the negative convention is used for familiarity.
/// </summary>
public static class TestFourier
{
    /// <summary>
    /// Computes an in-place, unscaled forward FFT. The length must be a power of two.
    /// </summary>
    public static void Forward(Complex[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int n = data.Length;
        if (n <= 1) { return; }
        if ((n & (n - 1)) != 0)
        {
            throw new ArgumentException($"Length must be a power of two, but was {n}.", nameof(data));
        }

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;

            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        // Iterative radix-2 Cooley-Tukey butterflies.
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2.0 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (int start = 0; start < n; start += length)
            {
                var w = Complex.One;
                for (int k = 0; k < length / 2; k++)
                {
                    var even = data[start + k];
                    var odd = w * data[start + k + (length / 2)];
                    data[start + k] = even + odd;
                    data[start + k + (length / 2)] = even - odd;
                    w *= step;
                }
            }
        }
    }
}

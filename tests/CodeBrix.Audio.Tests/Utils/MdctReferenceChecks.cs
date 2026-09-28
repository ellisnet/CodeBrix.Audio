using System;
using CodeBrix.Audio.Vorbis;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>Checks the optimized inverse transform against the direct cosine definition.</summary>
internal static class MdctReferenceChecks
{
    internal static void Check(int size)
    {
        Mdct transform = new Mdct();
        float[] coefficients = new float[size / 2];
        float[] samples = new float[size + 8];
        for (int k = 0; k < coefficients.Length; k++)
        {
            coefficients[k] = ((k * 17 % 23) - 11) / 16f;
            samples[k] = coefficients[k];
        }
        for (int i = size; i < samples.Length; i++) samples[i] = 12345f;

        transform.Reverse(samples, size);

        // Sample across the entire output, including both ends. The O(N^2)
        // definition is intentionally independent of the optimized FFT stages.
        for (int point = 0; point < 33; point++)
        {
            int i = point * (size - 1) / 32;
            double expected = 0;
            for (int k = 0; k < coefficients.Length; k++)
                expected += coefficients[k] * Math.Cos(2 * Math.PI / size * (i + 0.5 + size / 4.0) * (k + 0.5));
            double tolerance = 0.00002 * Math.Sqrt(size);
            if (!float.IsFinite(samples[i]) || Math.Abs(samples[i] - expected) > tolerance)
                throw new InvalidOperationException($"MDCT {size}, sample {i}: expected {expected}, got {samples[i]} (tolerance {tolerance}).");
        }
        for (int i = size; i < samples.Length; i++)
            if (samples[i] != 12345f) throw new InvalidOperationException("MDCT wrote beyond the requested block.");

        // Reuse the transform/scratch buffer with a different spectrum. An impulse
        // gives an independent expected value for EVERY output sample.
        Array.Clear(samples, 0, size);
        samples[coefficients.Length - 1] = 1;
        transform.Reverse(samples, size);
        for (int i = 0; i < size; i++)
        {
            double expected = Math.Cos(2 * Math.PI / size * (i + 0.5 + size / 4.0) * (coefficients.Length - 0.5));
            if (!float.IsFinite(samples[i]) || Math.Abs(samples[i] - expected) > 0.00002)
                throw new InvalidOperationException($"MDCT {size} impulse, sample {i}: expected {expected}, got {samples[i]}.");
        }
    }
}

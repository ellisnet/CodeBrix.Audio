using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// The test signals and the measurements the creative-effect tests are written against.
/// </summary>
/// <remarks>
/// <para>
/// Every source here is deterministic - the noise generator is the same xorshift the package's own
/// noise oscillator uses - so a failure is always reproducible and never a bad draw.
/// </para>
/// <para>
/// Effects are stereo and in-place, so the helpers work on a pair of buffers and hand them back;
/// <see cref="Render" /> pushes them through an effect in blocks, which is how the engine will call
/// it, and catches anything that only goes wrong at a block boundary.
/// </para>
/// </remarks>
public static class EffectSignals
{
    /// <summary>The sample rate every effect test prepares at.</summary>
    public const int SampleRate = 48000;

    /// <summary>The block size the render helpers use, matching the engine's own default.</summary>
    public const int BlockSize = 64;

    /// <summary>
    /// A sine wave.
    /// </summary>
    /// <param name="count">How many samples.</param>
    /// <param name="frequencyHz">The frequency in Hz.</param>
    /// <param name="amplitude">The peak amplitude.</param>
    /// <returns>The samples.</returns>
    public static float[] Sine(int count, double frequencyHz, double amplitude)
    {
        float[] samples = new float[count];
        double step = 2.0 * Math.PI * frequencyHz / SampleRate;

        for (int i = 0; i < count; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(step * i));
        }

        return samples;
    }

    /// <summary>
    /// White noise, uniformly distributed and reproducible.
    /// </summary>
    /// <param name="count">How many samples.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="peak">The peak amplitude.</param>
    /// <returns>The samples.</returns>
    public static float[] Noise(int count, uint seed, double peak)
    {
        float[] samples = new float[count];
        uint state = seed == 0u ? 0x9E3779B9u : seed;

        for (int i = 0; i < count; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            samples[i] = (float)(peak * ((((state >> 8) * (1.0 / 8388608.0)) - 1.0)));
        }

        return samples;
    }

    /// <summary>
    /// A single sample of one followed by silence: the input whose output IS the transfer function.
    /// </summary>
    /// <param name="count">How many samples.</param>
    /// <returns>The samples.</returns>
    public static float[] Impulse(int count)
    {
        float[] samples = new float[count];
        samples[0] = 1.0f;
        return samples;
    }

    /// <summary>Copies a buffer.</summary>
    /// <param name="samples">The buffer.</param>
    /// <returns>A copy.</returns>
    public static float[] Copy(float[] samples) => (float[])samples.Clone();

    /// <summary>
    /// Pushes a stereo pair through an effect in blocks, in place.
    /// </summary>
    /// <param name="effect">The effect, already prepared.</param>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    public static void Render(IInstrumentEffect effect, float[] left, float[] right)
    {
        float[] blockLeft = new float[BlockSize];
        float[] blockRight = new float[BlockSize];

        int position = 0;
        while (position < left.Length)
        {
            int frames = Math.Min(BlockSize, left.Length - position);

            Array.Copy(left, position, blockLeft, 0, frames);
            Array.Copy(right, position, blockRight, 0, frames);

            effect.Process(blockLeft, blockRight, frames);

            Array.Copy(blockLeft, 0, left, position, frames);
            Array.Copy(blockRight, 0, right, position, frames);

            position += frames;
        }
    }

    /// <summary>
    /// Prepares an effect and pushes one mono signal through both of its channels.
    /// </summary>
    /// <param name="effect">The effect.</param>
    /// <param name="mono">The signal to feed both channels.</param>
    /// <param name="left">Receives the left output.</param>
    /// <param name="right">Receives the right output.</param>
    public static void RenderMono(IInstrumentEffect effect, float[] mono, out float[] left, out float[] right)
    {
        effect.Prepare(SampleRate);

        left = Copy(mono);
        right = Copy(mono);

        Render(effect, left, right);
    }

    /// <summary>
    /// The root-mean-square level of part of a buffer, in decibels relative to full scale.
    /// </summary>
    /// <param name="samples">The buffer.</param>
    /// <param name="offset">Where to start.</param>
    /// <param name="count">How many samples to measure.</param>
    /// <returns>The level in dB, or -400 for silence.</returns>
    public static double RmsDb(float[] samples, int offset, int count)
    {
        double sum = 0.0;

        for (int i = 0; i < count; i++)
        {
            double value = samples[offset + i];
            sum += value * value;
        }

        double rms = Math.Sqrt(sum / count);
        return rms <= 0.0 ? -400.0 : 20.0 * Math.Log10(rms);
    }

    /// <summary>
    /// The mono sum of a stereo pair.
    /// </summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns>The average of the two, sample by sample.</returns>
    public static float[] MonoSum(float[] left, float[] right)
    {
        float[] sum = new float[left.Length];

        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] = 0.5f * (left[i] + right[i]);
        }

        return sum;
    }

    /// <summary>
    /// The zero-lag correlation of two channels, with their means removed: 1 is identical, 0 is
    /// uncorrelated, -1 is opposite.
    /// </summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <param name="offset">Where to start.</param>
    /// <param name="count">How many samples to measure.</param>
    /// <returns>The correlation.</returns>
    public static double Correlation(float[] left, float[] right, int offset, int count)
    {
        double meanLeft = 0.0;
        double meanRight = 0.0;

        for (int i = 0; i < count; i++)
        {
            meanLeft += left[offset + i];
            meanRight += right[offset + i];
        }

        meanLeft /= count;
        meanRight /= count;

        double product = 0.0;
        double leftPower = 0.0;
        double rightPower = 0.0;

        for (int i = 0; i < count; i++)
        {
            double a = left[offset + i] - meanLeft;
            double b = right[offset + i] - meanRight;
            product += a * b;
            leftPower += a * a;
            rightPower += b * b;
        }

        double denominator = Math.Sqrt(leftPower * rightPower);
        return denominator <= 0.0 ? 0.0 : product / denominator;
    }

    /// <summary>
    /// Estimates a periodic signal's frequency from its autocorrelation, which is immune to the
    /// amplitude ripple an overlap-add pitch shifter leaves behind.
    /// </summary>
    /// <remarks>
    /// The search range must hold exactly ONE period. A pure tone's raw autocorrelation peaks just
    /// as strongly at every multiple of its period, so a range wide enough to contain two of them
    /// can lock silently onto a sub-harmonic.
    /// </remarks>
    /// <param name="samples">The signal.</param>
    /// <param name="offset">Where to start.</param>
    /// <param name="count">How many samples to use.</param>
    /// <param name="minimumHz">The lowest frequency to consider.</param>
    /// <param name="maximumHz">The highest frequency to consider.</param>
    /// <returns>The frequency in Hz.</returns>
    public static double EstimateFrequency(
        float[] samples, int offset, int count, double minimumHz, double maximumHz)
    {
        int minimumLag = Math.Max(2, (int)(SampleRate / maximumHz));
        int maximumLag = (int)(SampleRate / minimumHz) + 1;

        double[] correlation = new double[maximumLag + 2];
        int usable = count - maximumLag - 2;

        for (int lag = minimumLag; lag <= maximumLag + 1; lag++)
        {
            double sum = 0.0;
            for (int i = 0; i < usable; i++)
            {
                sum += samples[offset + i] * (double)samples[offset + i + lag];
            }

            correlation[lag] = sum;
        }

        int best = minimumLag;
        for (int lag = minimumLag + 1; lag <= maximumLag; lag++)
        {
            if (correlation[lag] > correlation[best]) { best = lag; }
        }

        // A parabola through the peak and its neighbours turns an integer lag into a fractional one,
        // which is what makes a tenth-of-a-percent tuning assertion possible at all.
        double previous = correlation[best - 1];
        double peak = correlation[best];
        double next = correlation[best + 1];
        double divisor = previous - (2.0 * peak) + next;
        double shift = divisor == 0.0 ? 0.0 : 0.5 * (previous - next) / divisor;

        return SampleRate / (best + shift);
    }
}

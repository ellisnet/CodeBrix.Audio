using System;
using CodeBrix.Audio.Synth.DecentSampler;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Renders a Decent Sampler synthesizer offline and measures the result. Every render is
/// block-aligned, because a partial block leaks earlier audio into the next call.
/// </summary>
internal static class DecentSamplerRenderProbe
{
    /// <summary>Builds a synthesizer with the engine's defaults and a unity master volume.</summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="configure">An optional hook to change the settings before construction.</param>
    /// <returns>The synthesizer.</returns>
    public static DecentSamplerSynthesizer Synthesizer(
        DecentSamplerInstrument instrument, Action<DecentSamplerSynthesizerSettings> configure = null)
    {
        var settings = new DecentSamplerSynthesizerSettings(DecentSamplerEngineFixtures.SampleRate)
        {
            // Unity, so a measured level is the instrument's own arithmetic rather than half of it.
            MasterVolume = 1f,
        };

        configure?.Invoke(settings);

        return new DecentSamplerSynthesizer(instrument, settings);
    }

    /// <summary>Renders whole blocks and returns the stereo result.</summary>
    /// <param name="synthesizer">The synthesizer to render.</param>
    /// <param name="blocks">How many 64-frame blocks to render.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) RenderBlocks(
        DecentSamplerSynthesizer synthesizer, int blocks)
    {
        var frames = blocks * synthesizer.BlockSize;
        var left = new float[frames];
        var right = new float[frames];

        synthesizer.Render(left, right);

        return (left, right);
    }

    /// <summary>Renders the whole blocks that cover a duration in seconds.</summary>
    /// <param name="synthesizer">The synthesizer to render.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) RenderSeconds(
        DecentSamplerSynthesizer synthesizer, double seconds) =>
        RenderBlocks(synthesizer, BlocksFor(synthesizer, seconds));

    /// <summary>How many whole blocks cover a duration.</summary>
    /// <param name="synthesizer">The synthesizer, for its block size and rate.</param>
    /// <param name="seconds">The duration.</param>
    /// <returns>The block count, at least one.</returns>
    public static int BlocksFor(DecentSamplerSynthesizer synthesizer, double seconds) =>
        Math.Max(1, (int)Math.Ceiling(seconds * synthesizer.SampleRate / synthesizer.BlockSize));

    /// <summary>The root-mean-square level of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame of the window.</param>
    /// <param name="length">How many frames the window covers.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > samples.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The window falls outside the buffer.");
        }

        var sum = 0.0;
        for (var i = offset; i < offset + length; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / length);
    }

    /// <summary>The root-mean-square level of a whole channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples) => Rms(samples, 0, samples.Length);

    /// <summary>The largest absolute sample in a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame of the window.</param>
    /// <param name="length">How many frames the window covers.</param>
    /// <returns>The peak level.</returns>
    public static double Peak(float[] samples, int offset, int length)
    {
        var peak = 0.0;
        for (var i = offset; i < offset + length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }

    /// <summary>The largest absolute sample of a whole channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The peak level.</returns>
    public static double Peak(float[] samples) => Peak(samples, 0, samples.Length);

    /// <summary>
    /// The dominant frequency of a window, found by counting rising zero crossings. Good to a fraction
    /// of a percent on a clean tone, which is all the pitch tests need.
    /// </summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame of the window.</param>
    /// <param name="length">How many frames the window covers.</param>
    /// <param name="sampleRate">The sample rate.</param>
    /// <returns>The frequency in Hz, or 0 when the window holds fewer than two crossings.</returns>
    public static double Frequency(float[] samples, int offset, int length, int sampleRate)
    {
        var firstCrossing = -1.0;
        var lastCrossing = -1.0;
        var crossings = 0;

        for (var i = offset + 1; i < offset + length; i++)
        {
            var previous = samples[i - 1];
            var current = samples[i];

            if (!(previous <= 0f && current > 0f))
            {
                continue;
            }

            // Linear interpolation across the crossing keeps the estimate well inside a percent.
            var fraction = previous == current ? 0.0 : -previous / (double)(current - previous);
            var position = i - 1 + fraction;

            if (crossings == 0)
            {
                firstCrossing = position;
            }

            lastCrossing = position;
            crossings++;
        }

        if (crossings < 2)
        {
            return 0.0;
        }

        var period = (lastCrossing - firstCrossing) / (crossings - 1);
        return period <= 0.0 ? 0.0 : sampleRate / period;
    }
}

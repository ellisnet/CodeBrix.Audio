using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// Renders a synthesizer offline and measures what came out. Every render is block-aligned, because a
/// partial block leaks the previous call's audio into the next one.
/// </summary>
public static class RenderProbe
{
    /// <summary>Builds a Decent Sampler synthesizer at unity master volume.</summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="extensions">The registry to resolve waveforms and effects through, or null for the shared one.</param>
    /// <returns>The synthesizer.</returns>
    public static DecentSamplerSynthesizer Synthesizer(
        DecentSamplerInstrument instrument, DecentSamplerExtensionRegistry extensions = null)
    {
        DecentSamplerSynthesizerSettings settings =
            new DecentSamplerSynthesizerSettings(DecentSamplerFixtures.SampleRate)
            {
                // Unity, so a measured level is the instrument's own arithmetic rather than half of it.
                MasterVolume = 1f,
                Extensions = extensions,
            };

        return new DecentSamplerSynthesizer(instrument, settings);
    }

    /// <summary>Renders whole blocks from any synthesizer.</summary>
    /// <param name="synthesizer">The synthesizer.</param>
    /// <param name="blocks">How many blocks to render.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) RenderBlocks(IMidiSynthesizer synthesizer, int blocks)
    {
        int frames = blocks * synthesizer.BlockSize;
        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.Render(left, right);

        return (left, right);
    }

    /// <summary>Renders the whole blocks that cover a duration.</summary>
    /// <param name="synthesizer">The synthesizer.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) RenderSeconds(IMidiSynthesizer synthesizer, double seconds) =>
        RenderBlocks(synthesizer, BlocksFor(synthesizer, seconds));

    /// <summary>How many whole blocks cover a duration.</summary>
    /// <param name="synthesizer">The synthesizer.</param>
    /// <param name="seconds">The duration.</param>
    /// <returns>The block count, at least one.</returns>
    public static int BlocksFor(IMidiSynthesizer synthesizer, double seconds) =>
        Math.Max(1, (int)Math.Ceiling(seconds * synthesizer.SampleRate / synthesizer.BlockSize));

    /// <summary>The root-mean-square level of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame.</param>
    /// <param name="length">How many frames.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples, int offset, int length)
    {
        double sum = 0.0;

        for (int i = offset; i < offset + length; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / length);
    }

    /// <summary>The root-mean-square level of a whole channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples) => Rms(samples, 0, samples.Length);

    /// <summary>The largest absolute sample of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame.</param>
    /// <param name="length">How many frames.</param>
    /// <returns>The peak level.</returns>
    public static double Peak(float[] samples, int offset, int length)
    {
        double peak = 0.0;

        for (int i = offset; i < offset + length; i++)
        {
            double value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }

    /// <summary>The largest absolute sample of a whole channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The peak level.</returns>
    public static double Peak(float[] samples) => Peak(samples, 0, samples.Length);

    /// <summary>
    /// The dominant frequency of a window, from the loudest spectral bin with a parabolic
    /// interpolation across its neighbours. Accurate to a small fraction of a bin on a steady tone.
    /// </summary>
    /// <param name="samples">The channel; at least <see cref="Spectrum.BlockLength" /> samples from the offset.</param>
    /// <param name="offset">Where the analysis window starts.</param>
    /// <returns>The frequency in Hz.</returns>
    public static double Frequency(float[] samples, int offset = 0)
    {
        double[] magnitudes = Spectrum.WindowedMagnitudes(samples, offset);
        int peak = Spectrum.PeakBin(magnitudes);

        double left = peak > 0 ? magnitudes[peak - 1] : 0.0;
        double right = peak + 1 < magnitudes.Length ? magnitudes[peak + 1] : 0.0;
        double denominator = left - (2.0 * magnitudes[peak]) + right;
        double shift = denominator == 0.0 ? 0.0 : 0.5 * (left - right) / denominator;

        return (peak + shift) * Spectrum.SampleRate / Spectrum.BlockLength;
    }

    /// <summary>How much louder one level is than another, in decibels.</summary>
    /// <param name="value">The level.</param>
    /// <param name="reference">What to compare it with.</param>
    /// <returns>The difference in dB, or negative infinity when the level is zero.</returns>
    public static double Decibels(double value, double reference) =>
        value <= 0.0 ? double.NegativeInfinity : 20.0 * Math.Log10(value / reference);
}

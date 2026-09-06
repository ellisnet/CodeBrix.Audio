using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// Builds one effect straight from an <c>&lt;effect&gt;</c> element and measures it offline, without a
/// synthesizer in the way, so every number a test asserts is the effect's own arithmetic.
/// </summary>
internal static class DecentSamplerEffectHarness
{
    /// <summary>The rate every effect test runs at, matching the measurements' own 44.1 kHz.</summary>
    public const int SampleRate = 44100;

    /// <summary>The block size the engine renders in, which is what a chain is driven with.</summary>
    public const int BlockSize = 64;

    /// <summary>Parses one effect element out of a preset's instrument chain.</summary>
    /// <param name="xml">The <c>&lt;effect&gt;</c> element text.</param>
    /// <returns>The parsed element.</returns>
    public static DecentSamplerEffect Element(string xml)
    {
        var preset = DecentSamplerParser.ParseText(
            $"<DecentSampler><effects>{xml}</effects></DecentSampler>");

        return preset.Effects.Effects[0];
    }

    /// <summary>Builds a running effect from an element, through the shared registry.</summary>
    /// <param name="xml">The <c>&lt;effect&gt;</c> element text.</param>
    /// <param name="tempo">The musical clock a tempo-synced effect follows, or null for 120 BPM.</param>
    /// <returns>The effect and the element it reads its live parameters from.</returns>
    public static (IInstrumentEffect Effect, DecentSamplerEffect Element) Build(
        string xml, TempoSource tempo = null)
    {
        var element = Element(xml);

        DecentSamplerExtensions.Shared.TryGetEffectFactory(element.TypeName, out var factory);

        var effect = factory(new EffectContext(
            null, element, SampleRate, DecentSamplerEffectPlacement.Instrument, -1, BlockSize, tempo));

        effect.Prepare(SampleRate);

        return (effect, element);
    }

    /// <summary>Runs a mono signal through an effect and returns the stereo result.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="input">The signal, fed to both channels.</param>
    /// <returns>The processed left and right channels.</returns>
    public static (float[] Left, float[] Right) Run(IInstrumentEffect effect, float[] input)
    {
        var left = new float[input.Length];
        var right = new float[input.Length];
        Array.Copy(input, left, input.Length);
        Array.Copy(input, right, input.Length);

        var blockLeft = new float[BlockSize];
        var blockRight = new float[BlockSize];

        for (var offset = 0; offset < input.Length; offset += BlockSize)
        {
            var frames = Math.Min(BlockSize, input.Length - offset);

            Array.Clear(blockLeft, 0, BlockSize);
            Array.Clear(blockRight, 0, BlockSize);
            Array.Copy(left, offset, blockLeft, 0, frames);
            Array.Copy(right, offset, blockRight, 0, frames);

            effect.Process(blockLeft, blockRight, BlockSize);

            Array.Copy(blockLeft, 0, left, offset, frames);
            Array.Copy(blockRight, 0, right, offset, frames);
        }

        return (left, right);
    }

    /// <summary>A sine at a frequency, one second long by default.</summary>
    /// <param name="frequency">The frequency in Hz.</param>
    /// <param name="seconds">How long the tone runs.</param>
    /// <param name="amplitude">The peak amplitude.</param>
    /// <returns>The samples.</returns>
    public static float[] Sine(double frequency, double seconds = 1.0, double amplitude = 0.5)
    {
        var samples = new float[(int)(seconds * SampleRate)];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate));
        }

        return samples;
    }

    /// <summary>A single full-scale sample followed by silence.</summary>
    /// <param name="length">How many samples in total.</param>
    /// <returns>The samples.</returns>
    public static float[] Impulse(int length)
    {
        var samples = new float[length];
        samples[0] = 1f;
        return samples;
    }

    /// <summary>A step from zero to one, held for the rest of the buffer.</summary>
    /// <param name="length">How many samples in total.</param>
    /// <param name="level">The level the step rises to.</param>
    /// <returns>The samples.</returns>
    public static float[] Step(int length, float level = 1f)
    {
        var samples = new float[length];
        Array.Fill(samples, level);
        return samples;
    }

    /// <summary>The root-mean-square level of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame.</param>
    /// <param name="length">How many frames.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples, int offset, int length)
    {
        var sum = 0.0;

        for (var i = offset; i < offset + length; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / length);
    }

    /// <summary>
    /// The gain an effect applies to a steady sine, in decibels, measured after the filter has settled.
    /// </summary>
    /// <param name="xml">The <c>&lt;effect&gt;</c> element text.</param>
    /// <param name="frequency">The probe frequency in Hz.</param>
    /// <returns>The gain in decibels.</returns>
    public static double MagnitudeDecibels(string xml, double frequency)
    {
        var input = Sine(frequency, 1.0);
        var (effect, _) = Build(xml);
        var (left, _) = Run(effect, input);

        // Half a second in is far past any transient at these frequencies.
        var window = SampleRate / 2;
        var measured = Rms(left, window, window - 1);
        var reference = Rms(input, window, window - 1);

        return 20.0 * Math.Log10(measured / reference);
    }

    /// <summary>The peak absolute sample of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame.</param>
    /// <param name="length">How many frames.</param>
    /// <returns>The peak.</returns>
    public static double Peak(float[] samples, int offset, int length)
    {
        var peak = 0.0;

        for (var i = offset; i < offset + length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }

    /// <summary>The index of the largest absolute sample after a starting point.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="from">Where to start looking.</param>
    /// <param name="threshold">How large a sample has to be to count.</param>
    /// <returns>The index, or -1 when nothing reaches the threshold.</returns>
    public static int FirstPeakAfter(float[] samples, int from, double threshold)
    {
        for (var i = from; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) >= threshold)
            {
                return i;
            }
        }

        return -1;
    }
}

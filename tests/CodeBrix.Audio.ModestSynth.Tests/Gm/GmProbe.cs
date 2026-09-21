using System;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Playing the General MIDI synthesizer offline and measuring what came out.
/// </summary>
/// <remarks>
/// Everything here renders at <see cref="SampleRate" />, which is the rate
/// <see cref="Spectrum" /> and <see cref="Integration.RenderProbe" /> analyse at, so a frequency or
/// a centroid measured from these renders means what those helpers say it means.
/// </remarks>
public static class GmProbe
{
    /// <summary>The rate every test in this folder renders at.</summary>
    public const int SampleRate = Spectrum.SampleRate;

    /// <summary>The wire channel General MIDI puts percussion on - one less than the channel number.</summary>
    public const int PercussionWireChannel = GeneralMidi.PercussionChannel - 1;

    /// <summary>Builds a multi-timbral synthesizer at unity master volume, so a measured level is its own.</summary>
    /// <param name="configure">Anything else to set before it is built.</param>
    /// <returns>The synthesizer.</returns>
    public static GeneralMidiSynthesizer Build(Action<GeneralMidiSynthesizerSettings> configure = null) =>
        new GeneralMidiSynthesizer(Settings(configure));

    /// <summary>Builds a synthesizer pinned to one program, at unity master volume.</summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <param name="configure">Anything else to set before it is built.</param>
    /// <returns>The synthesizer.</returns>
    public static GeneralMidiSynthesizer BuildForProgram(
        int program, Action<GeneralMidiSynthesizerSettings> configure = null) =>
        GeneralMidiSynthesizer.CreateForProgram(program, Settings(configure));

    /// <summary>Builds a synthesizer pinned to the percussion kit, at unity master volume.</summary>
    /// <param name="configure">Anything else to set before it is built.</param>
    /// <returns>The synthesizer.</returns>
    public static GeneralMidiSynthesizer BuildForPercussion(
        Action<GeneralMidiSynthesizerSettings> configure = null) =>
        GeneralMidiSynthesizer.CreateForPercussion(Settings(configure));

    /// <summary>Settings at the test rate and unity volume.</summary>
    /// <param name="configure">Anything else to set.</param>
    /// <returns>The settings.</returns>
    public static GeneralMidiSynthesizerSettings Settings(
        Action<GeneralMidiSynthesizerSettings> configure = null)
    {
        GeneralMidiSynthesizerSettings settings = new GeneralMidiSynthesizerSettings(SampleRate)
        {
            MasterVolume = 1f,
        };

        configure?.Invoke(settings);

        return settings;
    }

    /// <summary>Renders whole blocks covering a duration.</summary>
    /// <param name="synthesizer">The synthesizer.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) Render(GeneralMidiSynthesizer synthesizer, double seconds)
    {
        int blocks = Math.Max(1, (int)Math.Ceiling(seconds * SampleRate / synthesizer.BlockSize));
        int frames = blocks * synthesizer.BlockSize;

        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.Render(left, right);

        return (left, right);
    }

    /// <summary>
    /// Plays one note and renders it: the hold, then the tail after the key comes up.
    /// </summary>
    /// <param name="synthesizer">The synthesizer.</param>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="key">The note number.</param>
    /// <param name="velocity">The velocity.</param>
    /// <param name="hold">How long the key is held, in seconds.</param>
    /// <param name="tail">How long to keep rendering after it comes up, in seconds.</param>
    /// <returns>The whole render, hold and tail together.</returns>
    public static (float[] Left, float[] Right) PlayNote(
        GeneralMidiSynthesizer synthesizer,
        int channel,
        int key,
        int velocity = 100,
        double hold = 0.6,
        double tail = 0.5)
    {
        synthesizer.NoteOn(channel, key, velocity);
        var (heldLeft, heldRight) = Render(synthesizer, hold);

        synthesizer.NoteOff(channel, key);
        var (tailLeft, tailRight) = Render(synthesizer, tail);

        return (Join(heldLeft, tailLeft), Join(heldRight, tailRight));
    }

    /// <summary>The root-mean-square level of a whole channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples) => Rms(samples, 0, samples.Length);

    /// <summary>The root-mean-square level of a window.</summary>
    /// <param name="samples">The channel.</param>
    /// <param name="offset">The first frame.</param>
    /// <param name="length">How many frames.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples, int offset, int length)
    {
        double sum = 0.0;
        int end = Math.Min(samples.Length, offset + length);

        for (int i = offset; i < end; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / Math.Max(1, end - offset));
    }

    /// <summary>The largest absolute sample of a channel.</summary>
    /// <param name="samples">The channel.</param>
    /// <returns>The peak level.</returns>
    public static double Peak(float[] samples)
    {
        double peak = 0.0;

        for (int i = 0; i < samples.Length; i++)
        {
            double value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }

    /// <summary>The loudest sample of either channel.</summary>
    /// <param name="render">A stereo render.</param>
    /// <returns>The peak level.</returns>
    public static double Peak((float[] Left, float[] Right) render) =>
        Math.Max(Peak(render.Left), Peak(render.Right));

    /// <summary>The root-mean-square of the mono sum of a stereo render.</summary>
    /// <param name="render">A stereo render.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms((float[] Left, float[] Right) render)
    {
        double sum = 0.0;

        for (int i = 0; i < render.Left.Length; i++)
        {
            double value = 0.5 * (render.Left[i] + render.Right[i]);
            sum += value * value;
        }

        return Math.Sqrt(sum / Math.Max(1, render.Left.Length));
    }

    /// <summary>
    /// The level of the LOUDEST hundred milliseconds of a render, as the root-mean-square of the
    /// mono sum.
    /// </summary>
    /// <param name="render">A stereo render.</param>
    /// <returns>The loudest window's RMS level.</returns>
    /// <remarks>
    /// This is the fair way to compare a pad whose attack takes a second with a woodblock that is
    /// over in a tenth of one: an average over the whole note measures how LONG a program lasts as
    /// much as how loud it is.
    /// </remarks>
    public static double LoudestWindow((float[] Left, float[] Right) render)
    {
        int window = SampleRate / 10;
        int count = render.Left.Length;

        if (count <= window) { return Rms(render); }

        double best = 0.0;

        // Hopping a tenth of the window is fine: a hundred-millisecond measurement does not move
        // meaningfully inside ten milliseconds.
        for (int offset = 0; offset + window <= count; offset += window / 10)
        {
            double sum = 0.0;

            for (int i = offset; i < offset + window; i++)
            {
                double value = 0.5 * (render.Left[i] + render.Right[i]);
                sum += value * value;
            }

            double level = Math.Sqrt(sum / window);
            if (level > best) { best = level; }
        }

        return best;
    }

    /// <summary>
    /// The power-weighted spectral centroid of a render, in Hz - how bright it is, as one number.
    /// </summary>
    /// <param name="samples">The channel to analyse.</param>
    /// <param name="count">How many frames to use; whole analysis blocks only.</param>
    /// <returns>The centroid in Hz.</returns>
    public static double Centroid(float[] samples, int count = 0)
    {
        int frames = count <= 0 ? samples.Length : Math.Min(count, samples.Length);
        return Spectrum.PowerCentroid(samples, frames);
    }

    /// <summary>Whether two renders are the same sample for sample.</summary>
    /// <param name="first">One render.</param>
    /// <param name="second">The other.</param>
    /// <returns>The largest absolute difference between them.</returns>
    public static double LargestDifference(float[] first, float[] second)
    {
        double worst = 0.0;
        int count = Math.Min(first.Length, second.Length);

        for (int i = 0; i < count; i++)
        {
            double difference = Math.Abs((double)first[i] - second[i]);
            if (difference > worst) { worst = difference; }
        }

        return worst;
    }

    private static float[] Join(float[] first, float[] second)
    {
        float[] joined = new float[first.Length + second.Length];
        Array.Copy(first, joined, first.Length);
        Array.Copy(second, 0, joined, first.Length, second.Length);
        return joined;
    }
}

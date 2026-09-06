using System;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The same waveforms generated the naive way - straight from the phase, with no band limiting -
/// so a test can show what the corrections in the real oscillators are worth.
/// </summary>
public static class NaiveWaveforms
{
    /// <summary>
    /// Renders one analysis block of a naive waveform.
    /// </summary>
    /// <param name="waveform">One of <c>saw</c>, <c>square</c> or <c>triangle</c>.</param>
    /// <param name="frequencyHz">The pitch in Hz.</param>
    /// <returns>A block of <see cref="Spectrum.BlockLength" /> samples.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="waveform" /> is not one of the three.</exception>
    public static float[] Render(string waveform, double frequencyHz)
    {
        float[] block = new float[Spectrum.BlockLength];
        double increment = frequencyHz / Spectrum.SampleRate;
        double phase = 0.0;

        for (int i = 0; i < block.Length; i++)
        {
            block[i] = (float)Shape(waveform, phase);

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        return block;
    }

    private static double Shape(string waveform, double phase)
    {
        switch (waveform)
        {
            case "saw": return (2.0 * phase) - 1.0;
            case "square": return phase < 0.5 ? 1.0 : -1.0;
            case "triangle": return phase < 0.5 ? (4.0 * phase) - 1.0 : 3.0 - (4.0 * phase);
            default:
                throw new ArgumentOutOfRangeException(nameof(waveform), waveform,
                    "Only saw, square and triangle have a naive form worth comparing against.");
        }
    }
}

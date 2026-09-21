using System;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// A sine low-frequency oscillator with a delayed onset and a fade-in, advanced once per BLOCK.
//
// Block rate is enough: at the default block of 64 frames a 5 Hz sweep moves by less than half a
// degree between updates, and keeping it off the per-sample path is what makes vibrato free on the
// programs that use it.
internal sealed class GmLfo
{
    private readonly double sampleRate;

    private double phase;
    private double increment;
    private double delaySamples;
    private double fadeSamples;
    private double elapsed;
    private double value;
    private double depth;

    internal GmLfo(int sampleRate)
    {
        this.sampleRate = sampleRate < 1 ? 1 : sampleRate;
    }

    // -1 to 1, already scaled by the delay and the fade-in.
    internal double Value => value * depth;

    internal void Start(GmLfoSpec spec, double startPhase)
    {
        increment = spec.Rate / sampleRate;
        delaySamples = spec.Delay * sampleRate;
        fadeSamples = spec.FadeIn * sampleRate;
        phase = startPhase;
        elapsed = 0.0;
        depth = 0.0;
        value = 0.0;
    }

    internal void Advance(int frames)
    {
        elapsed += frames;

        if (elapsed <= delaySamples)
        {
            depth = 0.0;
        }
        else if (fadeSamples <= 0.0)
        {
            depth = 1.0;
        }
        else
        {
            double faded = (elapsed - delaySamples) / fadeSamples;
            depth = faded >= 1.0 ? 1.0 : faded;
        }

        phase += increment * frames;
        if (phase >= 1.0) { phase -= Math.Floor(phase); }

        value = Math.Sin(2.0 * Math.PI * phase);
    }
}

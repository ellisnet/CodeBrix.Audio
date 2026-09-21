using System;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The per-voice resonant filter ModestSynth never had: a topology-preserving state-variable filter,
// which gives the low-pass, the high-pass and the band-pass from one set of coefficients and stays
// stable when the cutoff is swept every block.
//
// Why a state-variable rather than a ladder or a biquad: the three outputs come out of the same two
// integrators, so a row picks its mode without paying for a second filter; it is well behaved at
// high resonance; and its coefficients follow the cutoff continuously, which an envelope sweeping
// five octaves in forty milliseconds needs.
//
// Coefficients are recomputed once per BLOCK and the filter runs per SAMPLE. At the synthesizer's
// default block of 64 frames that is a parameter update every 1.45 ms, which is faster than any
// envelope stage a listener can pick out, and it keeps the trigonometry off the per-sample path.
internal sealed class GmFilter
{
    // Never let the cutoff reach Nyquist: tan() runs away there.
    private const double MaximumNyquistFraction = 0.45;
    private const double MinimumCutoffHz = 20.0;

    private readonly double sampleRate;

    private double a1;
    private double a2;
    private double a3;
    private double damping = 2.0;
    private double ic1;
    private double ic2;

    internal GmFilter(int sampleRate)
    {
        this.sampleRate = sampleRate < 1 ? 1 : sampleRate;
        SetCutoff(20000.0, 0.0);
    }

    internal void Reset()
    {
        ic1 = 0.0;
        ic2 = 0.0;
    }

    // Resonance 0 gives a damping of 2 (no peak at all); resonance 1 gives 0.1, a Q of ten.
    internal void SetCutoff(double cutoffHz, double resonance)
    {
        double highest = sampleRate * MaximumNyquistFraction;

        double cutoff = double.IsNaN(cutoffHz) ? highest : cutoffHz;
        if (cutoff < MinimumCutoffHz) { cutoff = MinimumCutoffHz; }
        if (cutoff > highest) { cutoff = highest; }

        double clamped = resonance < 0.0 ? 0.0 : resonance > 1.0 ? 1.0 : resonance;
        damping = 2.0 - (1.9 * clamped);

        double g = Math.Tan(Math.PI * cutoff / sampleRate);
        a1 = 1.0 / (1.0 + (g * (g + damping)));
        a2 = g * a1;
        a3 = g * a2;
    }

    internal float LowPass(float input)
    {
        double v3 = input - ic2;
        double v1 = (a1 * ic1) + (a2 * v3);
        double v2 = ic2 + (a2 * ic1) + (a3 * v3);
        ic1 = (2.0 * v1) - ic1;
        ic2 = (2.0 * v2) - ic2;
        return (float)v2;
    }

    internal float HighPass(float input)
    {
        double v3 = input - ic2;
        double v1 = (a1 * ic1) + (a2 * v3);
        double v2 = ic2 + (a2 * ic1) + (a3 * v3);
        ic1 = (2.0 * v1) - ic1;
        ic2 = (2.0 * v2) - ic2;
        return (float)(input - (damping * v1) - v2);
    }

    internal float BandPass(float input)
    {
        double v3 = input - ic2;
        double v1 = (a1 * ic1) + (a2 * v3);
        double v2 = ic2 + (a2 * ic1) + (a3 * v3);
        ic1 = (2.0 * v1) - ic1;
        ic2 = (2.0 * v2) - ic2;
        return (float)v1;
    }

    // One block through whichever output the mode names, in place.
    internal void Process(GmFilterMode mode, float[] buffer, int frames)
    {
        switch (mode)
        {
            case GmFilterMode.LowPass:
                for (int i = 0; i < frames; i++) { buffer[i] = LowPass(buffer[i]); }
                break;

            case GmFilterMode.HighPass:
                for (int i = 0; i < frames; i++) { buffer[i] = HighPass(buffer[i]); }
                break;

            case GmFilterMode.BandPass:
                for (int i = 0; i < frames; i++) { buffer[i] = BandPass(buffer[i]); }
                break;
        }
    }
}

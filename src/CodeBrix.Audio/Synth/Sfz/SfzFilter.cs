using System;

namespace CodeBrix.Audio.Synth.Sfz;

// The per-voice SFZ filter. One-pole shapes for lpf_1p/hpf_1p (6 dB/octave, no resonance), RBJ biquads
// for the two-pole shapes. Coefficients are recomputed only when the cutoff moves meaningfully, since
// CC modulation can retune the filter every block.
internal sealed class SfzFilter
{
    private readonly int sampleRate;

    private SfzFilterType type;
    private bool active;
    private bool onePole;

    private float lastCutoff;
    private float lastResonance;

    // Biquad state (also reused as the one-pole state in y1).
    private float b0;
    private float b1;
    private float b2;
    private float a1;
    private float a2;

    private float x1;
    private float x2;
    private float y1;
    private float y2;

    // One-pole coefficient.
    private float g;

    internal SfzFilter(int sampleRate)
    {
        this.sampleRate = sampleRate;
    }

    public void Start(SfzFilterType filterType)
    {
        type = filterType;
        active = false;
        lastCutoff = -1;
        lastResonance = float.MinValue;
        ClearBuffer();
    }

    public void ClearBuffer()
    {
        x1 = 0;
        x2 = 0;
        y1 = 0;
        y2 = 0;
    }

    public void SetCutoff(float cutoffFrequency, float resonanceDb)
    {
        // Below ~10 Hz a low-pass silences everything and a high-pass passes everything; clamp into a
        // sane band instead. Above the usable band the filter switches itself off.
        if (cutoffFrequency >= 0.45f * sampleRate)
        {
            if (type == SfzFilterType.LowPass1P || type == SfzFilterType.LowPass2P)
            {
                active = false;
                return;
            }

            cutoffFrequency = 0.45f * sampleRate;
        }

        cutoffFrequency = Math.Clamp(cutoffFrequency, 10f, 0.45f * sampleRate);

        active = true;

        // Recomputing coefficients for sub-cent cutoff wiggle is wasted work; a 0.1% change is far
        // below audibility.
        if (lastCutoff > 0 &&
            MathF.Abs(cutoffFrequency - lastCutoff) < 0.001f * lastCutoff &&
            resonanceDb == lastResonance)
        {
            return;
        }

        lastCutoff = cutoffFrequency;
        lastResonance = resonanceDb;

        switch (type)
        {
            case SfzFilterType.LowPass1P:
            case SfzFilterType.HighPass1P:
                onePole = true;
                g = 1f - MathF.Exp(-2f * MathF.PI * cutoffFrequency / sampleRate);
                return;

            default:
                onePole = false;
                SetBiquad(cutoffFrequency, resonanceDb);
                return;
        }
    }

    public void Process(float[] block)
    {
        if (!active)
        {
            return;
        }

        if (onePole)
        {
            if (type == SfzFilterType.LowPass1P)
            {
                for (var t = 0; t < block.Length; t++)
                {
                    y1 += g * (block[t] - y1);
                    block[t] = y1;
                }
            }
            else
            {
                for (var t = 0; t < block.Length; t++)
                {
                    y1 += g * (block[t] - y1);
                    block[t] -= y1;
                }
            }

            return;
        }

        for (var t = 0; t < block.Length; t++)
        {
            var input = block[t];
            var output = b0 * input + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;

            block[t] = output;
        }
    }

    // RBJ audio-EQ-cookbook coefficients. Resonance is decibels above the Butterworth baseline, the
    // convention SFZ players share.
    private void SetBiquad(float cutoffFrequency, float resonanceDb)
    {
        var q = (1f / MathF.Sqrt(2f)) * MathF.Pow(10f, resonanceDb / 20f);

        var w = 2f * MathF.PI * cutoffFrequency / sampleRate;
        var cosw = MathF.Cos(w);
        var sinw = MathF.Sin(w);
        var alpha = sinw / (2f * q);

        float rb0;
        float rb1;
        float rb2;

        switch (type)
        {
            case SfzFilterType.HighPass2P:
                rb0 = (1f + cosw) / 2f;
                rb1 = -(1f + cosw);
                rb2 = (1f + cosw) / 2f;
                break;

            case SfzFilterType.BandPass2P:
                rb0 = alpha;
                rb1 = 0f;
                rb2 = -alpha;
                break;

            case SfzFilterType.BandReject2P:
                rb0 = 1f;
                rb1 = -2f * cosw;
                rb2 = 1f;
                break;

            default: // LowPass2P
                rb0 = (1f - cosw) / 2f;
                rb1 = 1f - cosw;
                rb2 = (1f - cosw) / 2f;
                break;
        }

        var ra0 = 1f + alpha;
        var ra1 = -2f * cosw;
        var ra2 = 1f - alpha;

        b0 = rb0 / ra0;
        b1 = rb1 / ra0;
        b2 = rb2 / ra0;
        a1 = ra1 / ra0;
        a2 = ra2 / ra0;
    }
}

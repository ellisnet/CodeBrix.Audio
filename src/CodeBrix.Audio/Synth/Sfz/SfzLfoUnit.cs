using System;

namespace CodeBrix.Audio.Synth.Sfz;

// The runtime of one region LFO, advanced once per block. The caller supplies the current frequency
// (base plus CC and cross-LFO modulation); this keeps the phase, the delay/fade-in state, and the
// sample-and-hold random draws (seeded per voice, so identical renders stay byte-identical).
internal sealed class SfzLfoUnit
{
    private SfzLfo lfo;
    private float delaySeconds;
    private float fadeSeconds;

    private float elapsed;
    private double phase;
    private uint rngState;
    private int heldHalfIndex;
    private float heldRandom;
    private float value;

    public void Start(SfzLfo lfoModel, float delay, float fade, uint randomSeed)
    {
        lfo = lfoModel;
        delaySeconds = Math.Max(0f, delay);
        fadeSeconds = Math.Max(0f, fade);

        elapsed = 0f;
        phase = lfoModel.Phase;
        rngState = randomSeed | 1;
        heldHalfIndex = -1;
        heldRandom = NextRandom();
        value = 0f;
    }

    // Advances by one block at the given frequency (Hz) and returns the LFO output, nominally -1..1.
    public float Advance(float blockSeconds, float frequency)
    {
        elapsed += blockSeconds;

        if (elapsed < delaySeconds)
        {
            value = 0f;
            return 0f;
        }

        phase += Math.Max(0f, frequency) * blockSeconds;

        var total = Shape(lfo.Wave, (float)(phase - Math.Floor(phase)));

        var subs = lfo.Subs;
        for (var i = 0; i < subs.Count; i++)
        {
            var sub = subs[i];
            var subPhase = phase * sub.Ratio;
            total += sub.Scale * Shape(sub.Wave, (float)(subPhase - Math.Floor(subPhase))) + sub.Offset;
        }

        if (fadeSeconds > 0f)
        {
            total *= Math.Clamp((elapsed - delaySeconds) / fadeSeconds, 0f, 1f);
        }

        value = total;
        return total;
    }

    public float Value => value;

    private float Shape(SfzLfoWave wave, float p)
    {
        switch (wave)
        {
            case SfzLfoWave.Sine:
                return MathF.Sin(2f * MathF.PI * p);

            case SfzLfoWave.Pulse75:
                return p < 0.75f ? 1f : -1f;

            case SfzLfoWave.Square:
                return p < 0.5f ? 1f : -1f;

            case SfzLfoWave.Pulse25:
                return p < 0.25f ? 1f : -1f;

            case SfzLfoWave.Pulse12:
                return p < 0.125f ? 1f : -1f;

            case SfzLfoWave.SawUp:
                return 2f * p - 1f;

            case SfzLfoWave.SawDown:
                return 1f - 2f * p;

            case SfzLfoWave.RandomSampleHold:
            {
                // A new random level twice per period, held between draws.
                var halfIndex = (int)(phase * 2.0);
                if (halfIndex != heldHalfIndex)
                {
                    heldHalfIndex = halfIndex;
                    heldRandom = NextRandom();
                }

                return heldRandom;
            }

            default: // Triangle: rises from 0, the phase convention shared with the sine.
                if (p < 0.25f)
                {
                    return 4f * p;
                }

                return p < 0.75f ? 2f - 4f * p : 4f * p - 4f;
        }
    }

    // xorshift32: tiny, deterministic, and plenty random for a sample-and-hold vibrato.
    private float NextRandom()
    {
        var x = rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        rngState = x;
        return (x >> 8) * (2f / 0xFFFFFF) - 1f;
    }
}

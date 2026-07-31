using System;

namespace CodeBrix.Audio.Synth.Sfz;

// The runtime of one modulation envelope (fileg/pitcheg): a linear DAHDSR contour advanced once per
// block, output 0..1 before depth scaling. Stage times and depth latch at note start, like the
// amplifier envelope's CC modulation.
internal sealed class SfzModEnvelopeUnit
{
    private float delayEnd;
    private float attackEnd;
    private float holdEnd;
    private float decayEnd;
    private float attackSeconds;
    private float decaySeconds;
    private float releaseSeconds;
    private float sustain;

    private float elapsed;
    private float releaseStart;
    private float releaseLevel;
    private bool released;
    private float value;

    public void Start(float delay, float attack, float hold, float decay, float sustainLevel, float release)
    {
        delayEnd = Math.Max(0f, delay);
        attackSeconds = Math.Max(0f, attack);
        attackEnd = delayEnd + attackSeconds;
        holdEnd = attackEnd + Math.Max(0f, hold);
        decaySeconds = Math.Max(0f, decay);
        decayEnd = holdEnd + decaySeconds;
        releaseSeconds = Math.Max(0f, release);
        sustain = Math.Clamp(sustainLevel, 0f, 1f);

        elapsed = 0f;
        released = false;
        value = 0f;
    }

    public void Release()
    {
        if (released)
        {
            return;
        }

        released = true;
        releaseStart = elapsed;
        releaseLevel = value;
    }

    // Advances by one block and returns the envelope level, 0 to 1.
    public float Advance(float blockSeconds)
    {
        elapsed += blockSeconds;

        if (released)
        {
            if (releaseSeconds <= 0f)
            {
                value = 0f;
            }
            else
            {
                var progress = (elapsed - releaseStart) / releaseSeconds;
                value = progress >= 1f ? 0f : releaseLevel * (1f - progress);
            }

            return value;
        }

        if (elapsed <= delayEnd)
        {
            value = 0f;
        }
        else if (elapsed <= attackEnd)
        {
            value = attackSeconds <= 0f ? 1f : (elapsed - delayEnd) / attackSeconds;
        }
        else if (elapsed <= holdEnd)
        {
            value = 1f;
        }
        else if (elapsed <= decayEnd)
        {
            var progress = decaySeconds <= 0f ? 1f : (elapsed - holdEnd) / decaySeconds;
            value = 1f - (1f - sustain) * progress;
        }
        else
        {
            value = sustain;
        }

        return value;
    }

    public float Value => value;
}

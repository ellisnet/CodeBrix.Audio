using System;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The General MIDI voice's envelope: delay, attack, hold, decay, sustain, release, with EXPONENTIAL
// decay and release.
//
// Why exponential. A struck string, a bell and a plucked note all lose a fixed FRACTION of their
// energy per unit time, so their amplitude falls as a decaying exponential and their level in
// decibels falls in a straight line. A linear decay - what ModestEnvelope does, and what the old
// synthesizer keeps - holds the sound up too long and then cuts it off, which is exactly the
// "synthetic" character the bank is trying to avoid on 60 of its 128 programs.
//
// The stage times mean what they say: a decay of 0.4 seconds has fallen to a ten-thousandth of the
// distance it had to travel after 0.4 seconds, which is -80 dB and inaudible, so the stage ENDS
// there rather than crawling towards its target forever.
internal sealed class GmEnvelope
{
    // ln(10000): the number of time constants that take a stage from its start to -80 dB of its span.
    private const double StageDecades = 9.2103403719761836;

    // The attack curve overshoots its target so that the curve reaches exactly 1.0 at the attack time
    // instead of approaching it asymptotically. 1.3 with the matching rate constant below.
    private const double AttackTarget = 1.3;
    private const double AttackRate = 1.4663370687934272;

    private const double FinishedLevel = 1.0E-4;
    private const double KillSeconds = 0.005;

    private readonly int sampleRate;

    private double level;
    private double sustainLevel;
    private double attackStep;
    private double attackCoefficient;
    private double decayCoefficient;
    private double releaseCoefficient;
    private int delaySamples;
    private int holdSamples;
    private int stageSamples;
    private bool linearAttack;
    private Stage stage = Stage.Idle;

    internal GmEnvelope(int sampleRate)
    {
        this.sampleRate = sampleRate < 1 ? 1 : sampleRate;
    }

    private enum Stage
    {
        Idle,
        Delay,
        Attack,
        Hold,
        Decay,
        Sustain,
        Release,
        Finished,
    }

    internal bool IsFinished => stage == Stage.Finished || stage == Stage.Idle;

    internal bool IsReleased => stage == Stage.Release || stage == Stage.Finished;

    internal double Level => level;

    // timeScale stretches or shrinks every stage together - that is how key scaling makes a high note
    // decay faster than a low one. attackScale and releaseScale do the same to one stage each, which
    // is how a hard velocity opens a sound faster and how the public per-program adjustments lengthen
    // a release without touching anything else.
    internal void Start(GmEnvelopeSpec spec, double timeScale, double attackScale, double releaseScale)
    {
        double attack = Math.Max(GmEnvelopeSpec.MinimumAttackSeconds, spec.Attack * timeScale * attackScale);
        double decay = Math.Max(0.0, spec.Decay * timeScale);
        double release = Math.Max(GmEnvelopeSpec.MinimumReleaseSeconds, spec.Release * timeScale * releaseScale);

        sustainLevel = spec.Sustain < 0.0 ? 0.0 : spec.Sustain > 1.0 ? 1.0 : spec.Sustain;
        linearAttack = spec.LinearAttack;

        attackStep = 1.0 / (attack * sampleRate);
        attackCoefficient = 1.0 - Math.Exp(-AttackRate / (attack * sampleRate));
        decayCoefficient = decay > 0.0 ? Math.Exp(-StageDecades / (decay * sampleRate)) : 0.0;
        releaseCoefficient = Math.Exp(-StageDecades / (release * sampleRate));

        delaySamples = spec.Delay > 0.0 ? (int)(spec.Delay * sampleRate) : 0;
        holdSamples = spec.Hold > 0.0 ? (int)(spec.Hold * sampleRate) : 0;

        level = 0.0;
        stageSamples = delaySamples;
        stage = delaySamples > 0 ? Stage.Delay : Stage.Attack;
    }

    internal void Release()
    {
        if (stage == Stage.Idle || stage == Stage.Finished || stage == Stage.Release) { return; }

        stage = Stage.Release;
    }

    // The choke a stolen voice gets: five milliseconds, the conventional click-free figure.
    internal void Kill()
    {
        releaseCoefficient = Math.Exp(-StageDecades / (KillSeconds * sampleRate));
        stage = Stage.Release;
    }

    internal void Reset()
    {
        level = 0.0;
        stage = Stage.Idle;
    }

    // The gain for one sample, advancing the envelope by one sample.
    internal double Next()
    {
        switch (stage)
        {
            case Stage.Delay:
                if (--stageSamples <= 0) { stage = Stage.Attack; }
                return 0.0;

            case Stage.Attack:
                if (linearAttack)
                {
                    level += attackStep;
                }
                else
                {
                    level += (AttackTarget - level) * attackCoefficient;
                }

                if (level >= 1.0)
                {
                    level = 1.0;
                    stageSamples = holdSamples;
                    stage = holdSamples > 0 ? Stage.Hold : Stage.Decay;
                }

                break;

            case Stage.Hold:
                if (--stageSamples <= 0) { stage = Stage.Decay; }
                break;

            case Stage.Decay:
                level = sustainLevel + ((level - sustainLevel) * decayCoefficient);

                if (level <= sustainLevel + FinishedLevel)
                {
                    level = sustainLevel;
                    stage = sustainLevel <= FinishedLevel ? Stage.Finished : Stage.Sustain;
                }

                break;

            case Stage.Sustain:
                break;

            case Stage.Release:
                level *= releaseCoefficient;

                if (level <= FinishedLevel)
                {
                    level = 0.0;
                    stage = Stage.Finished;
                }

                break;

            default:
                level = 0.0;
                break;
        }

        return level;
    }
}

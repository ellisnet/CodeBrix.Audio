namespace CodeBrix.Audio.ModestSynth.Internal;

// The standalone synthesizer's amplitude envelope: straight lines in amplitude, one sample at a time.
//
// Straight lines rather than the Decent Sampler engine's curve law, because this envelope belongs to
// this package's own synthesizer rather than to the format: it is meant to be obvious and exact. A
// preset's envelope is the core engine's job and is a different piece of code entirely.
internal sealed class ModestEnvelope
{
    private const double KillSeconds = 0.005;

    private readonly int sampleRate;

    private double level;
    private double sustainLevel = 1.0;
    private double attackStep;
    private double decayStep;
    private double releaseStep;
    private double releaseFrom = 1.0;
    private Stage stage = Stage.Idle;

    internal ModestEnvelope(int sampleRate)
    {
        this.sampleRate = sampleRate < 1 ? 1 : sampleRate;
    }

    private enum Stage
    {
        Idle,
        Attack,
        Decay,
        Sustain,
        Release,
        Finished,
    }

    internal bool IsFinished => stage == Stage.Finished || stage == Stage.Idle;

    internal bool IsReleased => stage == Stage.Release || stage == Stage.Finished;

    internal double Level => level;

    internal void Start(double attack, double decay, double sustain, double release)
    {
        sustainLevel = sustain;
        attackStep = attack > 0.0 ? 1.0 / (attack * sampleRate) : 1.0;
        decayStep = decay > 0.0 ? (1.0 - sustain) / (decay * sampleRate) : 1.0;
        releaseStep = release > 0.0 ? 1.0 / (release * sampleRate) : 1.0;

        level = 0.0;
        stage = Stage.Attack;
    }

    internal void Release()
    {
        if (stage == Stage.Idle || stage == Stage.Finished || stage == Stage.Release) { return; }

        releaseFrom = level;
        stage = Stage.Release;
    }

    // The choke a stolen voice gets: five milliseconds, the conventional click-free figure.
    internal void Kill()
    {
        releaseFrom = level;
        releaseStep = 1.0 / (KillSeconds * sampleRate);
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
            case Stage.Attack:
                level += attackStep;
                if (level >= 1.0)
                {
                    level = 1.0;
                    stage = Stage.Decay;
                }

                break;

            case Stage.Decay:
                level -= decayStep;
                if (level <= sustainLevel)
                {
                    level = sustainLevel;
                    stage = Stage.Sustain;
                }

                break;

            case Stage.Sustain:
                level = sustainLevel;
                break;

            case Stage.Release:
                level -= releaseStep * releaseFrom;
                if (level <= 0.0)
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

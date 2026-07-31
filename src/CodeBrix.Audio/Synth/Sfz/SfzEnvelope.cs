using System;

namespace CodeBrix.Audio.Synth.Sfz;

// The SFZ amplifier envelope: delay, attack, hold, decay, sustain, release (seconds and a sustain
// percentage). Attack is a linear amplitude ramp per the SFZ specification; decay and release are the
// exponential-to-silence shape, matching the SoundFont envelope in this package so the two synths sit
// well side by side. Stage times are floored at one millisecond for slope computation only, so a
// zero-second release still declicks instead of truncating.
//
// The ampeg_*_shape opcodes replace a stage's curve with a power curve: shape 0 is linear, positive
// values put most of the movement late in the stage, negative values put it early. The spec gives the
// behaviour but no formula; progress^(2^(shape/2)) is this engine's documented approximation, which
// saturates around |shape| = 10 the way the spec describes. An unset shape keeps the defaults above.
internal sealed class SfzEnvelope
{
    // The exponential slope constant the SoundFont envelope uses: e^-9.226 is about -80 dB, which is
    // where the decay and release shapes are considered finished.
    private const double slopeConstant = -9.226;
    private const double minimumStageSeconds = 0.001;

    private readonly int sampleRate;
    private readonly int blockSize;

    private double attackStartTime;
    private double holdStartTime;
    private double decayStartTime;
    private double releaseStartTime;

    private double attackSlope;
    private double decaySlope;
    private double releaseSlope;

    private double attackSeconds;
    private double decaySeconds;
    private double releaseSeconds;

    private float attackExponent;
    private float decayExponent;
    private float releaseExponent;
    private bool decayShaped;
    private bool releaseShaped;

    private float sustainLevel;
    private float releaseLevel;

    private int processedSampleCount;
    private Stage stage;
    private float value;
    private float priority;

    internal SfzEnvelope(int sampleRate, int blockSize)
    {
        this.sampleRate = sampleRate;
        this.blockSize = blockSize;
    }

    public void Start(float delay, float attack, float hold, float decay, float sustain, float release)
    {
        Start(delay, attack, hold, decay, sustain, release, null, null, null);
    }

    public void Start(
        float delay, float attack, float hold, float decay, float sustain, float release,
        float? attackShape, float? decayShape, float? releaseShape)
    {
        attackExponent = ShapeExponent(attackShape);
        decayShaped = decayShape.HasValue;
        decayExponent = ShapeExponent(decayShape);
        releaseShaped = releaseShape.HasValue;
        releaseExponent = ShapeExponent(releaseShape);

        SetTimes(delay, attack, hold, decay, sustain, release);
        releaseStartTime = 0;
        releaseLevel = 0;

        processedSampleCount = 0;
        stage = Stage.Delay;
        value = 0;

        Process(0);
    }

    // ampeg_dynamic: the stage timing follows its CC modulation while the note plays. The elapsed
    // time and current stage stand; only the boundaries ahead, the slopes and the sustain move.
    public void Retime(float delay, float attack, float hold, float decay, float sustain, float release)
    {
        SetTimes(delay, attack, hold, decay, sustain, release);

        if (stage == Stage.Release)
        {
            // The new release time applies to the remainder, from the level the release started at.
            releaseSlope = slopeConstant / Math.Max(release, minimumStageSeconds);
            releaseSeconds = Math.Max(release, minimumStageSeconds);
        }
    }

    public void Release()
    {
        stage = Stage.Release;
        releaseStartTime = (double)processedSampleCount / sampleRate;
        releaseLevel = value;
    }

    // The fast choke for off_mode=fast: a few milliseconds to silence, whatever the region's release
    // says. Five milliseconds is the conventional choke time.
    public void ReleaseFast()
    {
        ReleaseTimed(0.005f, 0f);
        releaseShaped = false; // The 5 ms choke keeps the exponential shape it always had.
    }

    // The off_mode=time choke: fade over off_time seconds with the off_shape curvature.
    public void ReleaseTimed(float seconds, float shape)
    {
        stage = Stage.Release;
        releaseStartTime = (double)processedSampleCount / sampleRate;
        releaseLevel = value;
        releaseSeconds = Math.Max(seconds, minimumStageSeconds);
        releaseSlope = slopeConstant / releaseSeconds;
        releaseShaped = true;
        releaseExponent = ShapeExponent(shape);
    }

    public bool Process()
    {
        return Process(blockSize);
    }

    private void SetTimes(float delay, float attack, float hold, float decay, float sustain, float release)
    {
        attackSeconds = Math.Max(attack, minimumStageSeconds);
        decaySeconds = Math.Max(decay, minimumStageSeconds);
        releaseSeconds = Math.Max(release, minimumStageSeconds);

        attackSlope = 1 / attackSeconds;
        decaySlope = slopeConstant / decaySeconds;
        releaseSlope = slopeConstant / releaseSeconds;

        attackStartTime = Math.Max(0, delay);
        holdStartTime = attackStartTime + Math.Max(0, attack);
        decayStartTime = holdStartTime + Math.Max(0, hold);

        sustainLevel = Math.Clamp(sustain, 0f, 1f);
    }

    private static float ShapeExponent(float? shape)
    {
        if (!shape.HasValue || shape.Value == 0f)
        {
            return 1f;
        }

        return MathF.Pow(2f, Math.Clamp(shape.Value, -10f, 10f) / 2f);
    }

    private bool Process(int sampleCount)
    {
        processedSampleCount += sampleCount;

        var currentTime = (double)processedSampleCount / sampleRate;

        while (stage <= Stage.Hold)
        {
            double endTime;
            switch (stage)
            {
                case Stage.Delay:
                    endTime = attackStartTime;
                    break;

                case Stage.Attack:
                    endTime = holdStartTime;
                    break;

                case Stage.Hold:
                    endTime = decayStartTime;
                    break;

                default:
                    throw new InvalidOperationException("Invalid envelope stage.");
            }

            if (currentTime < endTime)
            {
                break;
            }

            stage++;
        }

        switch (stage)
        {
            case Stage.Delay:
                value = 0;
                priority = 4f + value;
                return true;

            case Stage.Attack:
            {
                var progress = (float)(attackSlope * (currentTime - attackStartTime));
                value = attackExponent == 1f
                    ? progress
                    : MathF.Pow(Math.Clamp(progress, 0f, 1f), attackExponent);
                priority = 3f + value;
                return true;
            }

            case Stage.Hold:
                value = 1;
                priority = 2f + value;
                return true;

            case Stage.Decay:
                if (decayShaped)
                {
                    var progress = Math.Clamp((currentTime - decayStartTime) / decaySeconds, 0, 1);
                    var descent = MathF.Pow((float)progress, decayExponent);
                    value = 1f - (1f - sustainLevel) * descent;
                }
                else
                {
                    value = Math.Max((float)SoundFontMath.ExpCutoff(decaySlope * (currentTime - decayStartTime)), sustainLevel);
                }
                priority = 1f + value;
                return value > SoundFontMath.NonAudible;

            case Stage.Release:
                if (releaseShaped)
                {
                    var progress = Math.Clamp((currentTime - releaseStartTime) / releaseSeconds, 0, 1);
                    var descent = MathF.Pow((float)progress, releaseExponent);
                    value = releaseLevel * (1f - descent);
                }
                else
                {
                    value = (float)(releaseLevel * SoundFontMath.ExpCutoff(releaseSlope * (currentTime - releaseStartTime)));
                }
                priority = value;
                return value > SoundFontMath.NonAudible;

            default:
                throw new InvalidOperationException("Invalid envelope stage.");
        }
    }

    public float Value => value;
    public float Priority => priority;

    private enum Stage
    {
        Delay,
        Attack,
        Hold,
        Decay,
        Release
    }
}

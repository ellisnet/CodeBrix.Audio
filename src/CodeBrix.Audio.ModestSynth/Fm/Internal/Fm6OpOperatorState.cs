using System;
using CodeBrix.Audio.ModestSynth.Patch;

namespace CodeBrix.Audio.ModestSynth.Fm.Internal;

/// <summary>
/// One operator's per-voice runtime: its phase, the amplitude its level and the note's velocity
/// give it, and its envelope - whichever of the two kinds of envelope the operator asked for.
/// </summary>
/// <remarks>
/// <para>
/// The settings themselves live in a <see cref="ModestFmOperator" />, which a preset or a caller
/// can change at any time. <see cref="Configure" /> copies them into the form the render loop
/// needs, once per block, and never disturbs where the envelope has got to.
/// </para>
/// <para>
/// Both envelopes run on the same state machine. The ADSR one measures its stages in seconds and
/// moves the amplitude in a straight line; the four-stage one measures them with the hardware's
/// 0-99 rates and moves along the 0-99 LEVEL scale, so its amplitude moves exponentially, which is
/// what makes a four-stage decay sound like the hardware's rather than like a ramp.
/// </para>
/// </remarks>
internal sealed class Fm6OpOperatorState
{
    private const int StageIdle = 0;
    private const int StageAttack = 1;
    private const int StageDecay = 2;
    private const int StageSustain = 3;
    private const int StageRelease = 4;
    private const int StageHeld = 5;
    private const int StageFinished = 6;

    private readonly double[] stageTargets = new double[4];
    private readonly double[] stageSteps = new double[4];

    private ModestFmEnvelopeType envelopeType;
    private bool usesOuterEnvelope;
    private bool usesOuterRelease;
    private double attackStep;
    private double decayStep;
    private double sustainLevel = 1.0;
    private double releaseSeconds;
    private double releaseStep;
    private int sampleRate = 48000;

    private int stage = StageFinished;
    private double value;
    private double amplitude;
    private double signedStep;
    private double amplitudeFactor = 1.0;

    /// <summary>The operator's phase, in cycles, always in [0, 1).</summary>
    internal double Phase;

    /// <summary>How far one sample advances <see cref="Phase" />, in cycles.</summary>
    internal double Increment;

    /// <summary>The operator's level scaled by velocity - the block-constant part of its output.</summary>
    internal double LevelScale = 1.0;

    /// <summary>What the operator produced for the sample just rendered.</summary>
    internal double Output;

    /// <summary>
    /// Whether the envelope has run out. An operator that handed its envelope or its release to
    /// the group's envelope never says yes, because deciding when the note ends is what it gave
    /// away.
    /// </summary>
    internal bool IsFinished => stage == StageFinished;

    /// <summary>
    /// Copies an operator's settings into render-ready form, leaving the envelope where it is.
    /// </summary>
    /// <param name="settings">The operator's parameters.</param>
    /// <param name="rate">The sample rate in Hz.</param>
    /// <param name="rateScalingUnits">Extra four-stage rate units from key scaling; 0 turns it off.</param>
    internal void Configure(ModestFmOperator settings, int rate, double rateScalingUnits)
    {
        sampleRate = rate;
        envelopeType = settings.EnvelopeType;
        usesOuterEnvelope = settings.Attack <= ModestFmOperator.OuterEnvelopeSentinel
            && envelopeType == ModestFmEnvelopeType.Adsr;

        if (envelopeType == ModestFmEnvelopeType.Adsr)
        {
            usesOuterRelease = settings.Release <= ModestFmOperator.OuterEnvelopeSentinel;
            double attackSeconds = settings.Attack < 0.0 ? 0.0 : settings.Attack;
            sustainLevel = settings.Sustain;
            releaseSeconds = usesOuterRelease ? 0.0 : settings.Release;
            attackStep = attackSeconds > 0.0 ? 1.0 / (attackSeconds * rate) : double.PositiveInfinity;
            decayStep = settings.Decay > 0.0
                ? (1.0 - sustainLevel) / (settings.Decay * rate)
                : double.PositiveInfinity;
        }
        else
        {
            usesOuterRelease = false;
            ConfigureStage(0, settings.EgRate1, settings.EgLevel1, rate, rateScalingUnits);
            ConfigureStage(1, settings.EgRate2, settings.EgLevel2, rate, rateScalingUnits);
            ConfigureStage(2, settings.EgRate3, settings.EgLevel3, rate, rateScalingUnits);
            ConfigureStage(3, settings.EgRate4, settings.EgLevel4, rate, rateScalingUnits);
            RefreshStageDirection();
        }
    }

    /// <summary>Starts the envelope from silence.</summary>
    internal void NoteOn()
    {
        if (envelopeType == ModestFmEnvelopeType.Adsr)
        {
            if (usesOuterEnvelope)
            {
                value = 1.0;
                amplitude = 1.0;
                stage = StageHeld;
                return;
            }

            value = 0.0;
            amplitude = 0.0;
            stage = StageAttack;
            if (double.IsPositiveInfinity(attackStep))
            {
                value = 1.0;
                stage = StageDecay;
                if (double.IsPositiveInfinity(decayStep))
                {
                    value = sustainLevel;
                    stage = StageSustain;
                }
            }

            amplitude = value;
            return;
        }

        value = 0.0;
        amplitude = 0.0;
        stage = StageAttack;
        RefreshStageDirection();
    }

    /// <summary>Releases the envelope, as at note-off.</summary>
    internal void NoteOff()
    {
        if (stage == StageFinished || stage == StageIdle) { return; }

        if (envelopeType == ModestFmEnvelopeType.Adsr)
        {
            if (usesOuterEnvelope || usesOuterRelease)
            {
                stage = StageHeld;
                return;
            }

            if (releaseSeconds <= 0.0)
            {
                value = 0.0;
                amplitude = 0.0;
                stage = StageFinished;
                return;
            }

            releaseStep = value / (releaseSeconds * sampleRate);
            stage = StageRelease;
            return;
        }

        stage = StageRelease;
        RefreshStageDirection();
    }

    /// <summary>Returns the envelope amplitude for this sample and moves it on one sample.</summary>
    /// <returns>The amplitude to scale the operator's output by, 0 to 1.</returns>
    internal double Advance()
    {
        double current = amplitude;

        if (stage == StageIdle || stage == StageFinished) { return 0.0; }
        if (stage == StageHeld) { return current; }

        if (envelopeType == ModestFmEnvelopeType.Adsr)
        {
            // The ADSR's third stage is a hold; the four-stage envelope's third stage still moves,
            // towards L3, and only holds once it gets there.
            if (stage == StageSustain) { return current; }

            AdvanceAdsr();
        }
        else
        {
            AdvanceFourStage();
        }

        return current;
    }

    private void AdvanceAdsr()
    {
        switch (stage)
        {
            case StageAttack:
                value += attackStep;
                if (value >= 1.0)
                {
                    value = 1.0;
                    stage = StageDecay;
                    if (double.IsPositiveInfinity(decayStep))
                    {
                        value = sustainLevel;
                        stage = StageSustain;
                    }
                }

                break;

            case StageDecay:
                value -= decayStep;
                if (value <= sustainLevel)
                {
                    value = sustainLevel;
                    stage = StageSustain;
                }

                break;

            case StageRelease:
                value -= releaseStep;
                if (value <= 0.0)
                {
                    value = 0.0;
                    stage = StageFinished;
                }

                break;
        }

        amplitude = value;
    }

    private void AdvanceFourStage()
    {
        int index = stage - 1;
        if (index < 0 || index > 3) { return; }

        double target = stageTargets[index];
        double step = stageSteps[index];

        if (step <= 0.0)
        {
            // Rate 0 does not move. The stage holds where it is for as long as the note lasts,
            // which is what "rate 0 holds indefinitely" means.
            if (Math.Abs(value - target) > 0.0) { return; }
        }

        if (Math.Abs(value - target) <= Math.Abs(signedStep))
        {
            value = target;
            amplitude = Dx7Tables.LevelUnitsToAmplitude(value);

            if (stage < StageSustain)
            {
                stage++;
                RefreshStageDirection();
            }
            else if (stage == StageSustain)
            {
                // L3 reached: hold there for as long as the key is down.
                stage = StageHeld;
            }
            else if (stage == StageRelease)
            {
                // L4 reached. An operator whose L4 is above silence never goes quiet on its own -
                // that is the hardware's behaviour, and the group envelope is what stops it.
                stage = value <= 0.0 ? StageFinished : StageHeld;
            }

            return;
        }

        value += signedStep;
        if (amplitude <= 0.0) { amplitude = Dx7Tables.LevelUnitsToAmplitude(value); }
        else { amplitude *= amplitudeFactor; }
    }

    private void ConfigureStage(int index, int rate, int level, int sampleRateHz, double rateScalingUnits)
    {
        double scaledRate = rate + rateScalingUnits;
        int rounded = scaledRate <= 0.0 ? 0
            : scaledRate >= Dx7Tables.MaximumValue ? Dx7Tables.MaximumValue
            : (int)Math.Round(scaledRate, MidpointRounding.AwayFromZero);

        // Rate 0 must stay rate 0: key scaling speeds an envelope up, it does not start one that
        // was told not to move.
        if (rate <= 0) { rounded = 0; }

        stageTargets[index] = level;
        stageSteps[index] = Dx7Tables.LevelUnitsPerSecond(rounded) / sampleRateHz;
    }

    private void RefreshStageDirection()
    {
        if (envelopeType != ModestFmEnvelopeType.Dx7) { return; }

        int index = stage - 1;
        if (index < 0 || index > 3)
        {
            signedStep = 0.0;
            amplitudeFactor = 1.0;
            return;
        }

        double step = stageSteps[index];
        signedStep = stageTargets[index] >= value ? step : -step;
        amplitudeFactor = Math.Pow(2.0, signedStep / Dx7Tables.LevelUnitsPerHalving);
    }
}

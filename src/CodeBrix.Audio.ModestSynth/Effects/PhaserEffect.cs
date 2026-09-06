using System;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>phaser</c> effect: a cascade of all-pass filters whose corner frequency is swept by a
/// low-frequency oscillator, mixed back against the dry signal so the moving phase shift becomes a
/// set of moving notches.
/// </summary>
/// <remarks>
/// <para>
/// Parameters and defaults follow the developer guide: <c>mix</c> 0.5, <c>modDepth</c> 0.2,
/// <c>modRate</c> 0.2 Hz, <c>centerFrequency</c> 400 Hz, <c>feedback</c> 0.7.
/// </para>
/// <para>
/// MEASURED against the reference player. The structure came off a noise transfer function with the
/// oscillator frozen (round 2, item 22): SIX first-order all-pass sections all tuned to
/// <c>centerFrequency</c>, summed with the dry through the same crossfade. That puts three notches at
/// <c>centerFrequency/3.732</c>, <c>centerFrequency</c> and <c>centerFrequency*3.732</c> - the middle
/// one exactly on the setting, at four different centres - with total cancellation at
/// <c>mix="0.5"</c>, a flat response at <c>mix="1.0"</c> and nothing at all at <c>mix="0"</c>. Four
/// sections would space the notches 2.414 apart and eight would space them 5.03; the measured ratio is
/// 3.71.
/// </para>
/// <para>
/// THE SWEEP is exponential and reset at every note-on (round 3, item 43):
/// <c>fc(t) = centerFrequency * 2 ^ (5 * modDepth * -sin(2*pi*modRate*t))</c>, i.e. five octaves each
/// way at full depth and DOWNWARD first, measured as 4.96, 4.92 and 4.80 octaves per unit of depth at
/// three depths. <c>modRate="0"</c> therefore freezes the chain at <c>centerFrequency</c> exactly,
/// whatever the depth says.
/// </para>
/// <para>
/// <c>feedback="0.7"</c> turns the notches into shallow PEAKS of about 1.5 dB and costs about 2 dB
/// elsewhere. That is the signature of a loop that SUBTRACTS its feedback: with an all-pass chain
/// <c>A</c> the loop is <c>A/(1 + k*A)</c>, which at the cancelling frequencies (<c>A = -1</c>) gives
/// <c>-1/(1-k)</c> and an output of <c>+1.34 dB</c> at <c>k = 0.7</c>, and at <c>A = +1</c> gives
/// <c>1/(1+k)</c> and <c>-2.0 dB</c> - both measured. Adding the feedback instead deepens the notches,
/// which is not what the reference does. A NEGATIVE feedback measured identical to zero, so negative
/// values are treated as none.
/// </para>
/// </remarks>
public sealed class PhaserEffect : ModestMixEffectBase
{
    /// <summary>How many first-order all-pass sections the cascade holds.</summary>
    /// <remarks>
    /// MEASURED. Six sections put notches at 0.268, 1.0 and 3.732 times the all-pass corner, and the
    /// reference's three notches are spaced by a measured 3.71.
    /// </remarks>
    public const int AllPassStageCount = 6;

    /// <summary>
    /// How far the corner frequency travels EACH WAY at <c>modDepth="1"</c>, in octaves.
    /// </summary>
    /// <remarks>
    /// MEASURED as 4.96, 4.92 and 4.80 octaves per unit of depth at depths 1.0, 0.5 and 0.25, fitted
    /// per 93 ms frame with a residual of 0.013 to 0.043 octaves.
    /// </remarks>
    public const double SweepOctavesAtFullDepth = 5.0;

    /// <summary>The lowest corner frequency the sweep is allowed to reach, in Hz.</summary>
    public const double MinimumCenterFrequency = 20.0;

    // Five octaves below the lowest usable centre is below hearing; the sweep is allowed down there so
    // that a full-depth sweep from a low centre is not silently flattened against the property's own
    // floor. The reference sweeps past Nyquist at the top, where nothing is left to measure.
    private const double MinimumSweptFrequency = 1.0;

    /// <summary>The highest corner frequency the sweep is allowed to reach, in Hz.</summary>
    public const double MaximumCenterFrequency = 22000.0;

    // The all-pass corner only has to follow a sweep of at most 10 Hz, so recomputing its
    // coefficient every 32 samples is inaudible and keeps a transcendental out of the sample loop.
    private const int CoefficientInterval = 32;

    private const double DenormalFloor = 1.0e-25;

    private readonly double[] leftState = new double[AllPassStageCount];
    private readonly double[] rightState = new double[AllPassStageCount];

    private double modDepth = 0.2;
    private double modRate = 0.2;
    private double centerFrequency = 400.0;
    private double feedback = 0.7;

    private double lfoPhase;
    private double lfoIncrement;
    private double coefficient;
    private double cornerFrequency = 400.0;
    private double leftFeedback;
    private double rightFeedback;
    private int untilCoefficientUpdate;

    /// <summary>Creates a phaser carrying the guide's documented defaults.</summary>
    public PhaserEffect()
        : base(0.5)
    {
    }

    /// <summary>How far the corner frequency sweeps, 0 (still) to 1 (the full range). Default 0.2.</summary>
    public double ModDepth
    {
        get { return modDepth; }
        set { modDepth = Clamp(value, 0.0, 1.0, modDepth); }
    }

    /// <summary>The sweep frequency in Hz, 0 to 10. Default 0.2.</summary>
    public double ModRate
    {
        get { return modRate; }
        set
        {
            modRate = Clamp(value, 0.0, 10.0, modRate);
            UpdateLfoIncrement();
        }
    }

    /// <summary>
    /// The frequency the sweep starts from, in Hz. Default 400. Clamped to
    /// <see cref="MinimumCenterFrequency" />..<see cref="MaximumCenterFrequency" />; the guide's
    /// range starts at 0, which is not a usable all-pass corner.
    /// </summary>
    public double CenterFrequency
    {
        get { return centerFrequency; }
        set
        {
            centerFrequency =
                Clamp(value, MinimumCenterFrequency, MaximumCenterFrequency, centerFrequency);
        }
    }

    /// <summary>
    /// How much of the cascade's output is fed back into it, -1 to 1. Default 0.7. Values above 0.99
    /// are held at 0.99 while processing - an all-pass loop at a gain of exactly one never decays -
    /// and NEGATIVE values act as zero, which is what the reference measured.
    /// </summary>
    public double Feedback
    {
        get { return feedback; }
        set { feedback = Clamp(value, -1.0, 1.0, feedback); }
    }

    /// <summary>Where the sweep currently sits, in Hz. Read by the tests that measure the sweep.</summary>
    internal double CurrentCornerFrequency
    {
        get { return cornerFrequency; }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate) => UpdateLfoIncrement();

    /// <inheritdoc />
    protected override void OnReset()
    {
        Array.Clear(leftState, 0, leftState.Length);
        Array.Clear(rightState, 0, rightState.Length);
        leftFeedback = 0.0;
        rightFeedback = 0.0;
        lfoPhase = 0.0;
        untilCoefficientUpdate = 0;
        UpdateCoefficient();
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;
        // MEASURED: a negative feedback is indistinguishable from none, to 0.05 dB everywhere.
        double loop = feedback;
        if (loop > 0.99) { loop = 0.99; }
        if (loop < 0.0) { loop = 0.0; }

        for (int i = 0; i < frames; i++)
        {
            if (untilCoefficientUpdate <= 0)
            {
                UpdateCoefficient();
                untilCoefficientUpdate = CoefficientInterval;
            }

            untilCoefficientUpdate--;

            double dryLeft = left[i];
            double dryRight = right[i];

            double wetLeft = RunCascade(dryLeft - (loop * leftFeedback), leftState);
            double wetRight = RunCascade(dryRight - (loop * rightFeedback), rightState);

            leftFeedback = Math.Abs(wetLeft) < DenormalFloor ? 0.0 : wetLeft;
            rightFeedback = Math.Abs(wetRight) < DenormalFloor ? 0.0 : wetRight;

            left[i] = (float)((dryGain * dryLeft) + (mix * wetLeft));
            right[i] = (float)((dryGain * dryRight) + (mix * wetRight));

            lfoPhase += lfoIncrement;
            if (lfoPhase >= 1.0) { lfoPhase -= 1.0; }
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        switch (foldedName)
        {
            case "moddepth": ModDepth = value; return true;
            case "modrate": ModRate = value; return true;
            case "centerfrequency": CenterFrequency = value; return true;
            case "feedback": Feedback = value; return true;
            default: return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "moddepth": value = modDepth; return true;
            case "modrate": value = modRate; return true;
            case "centerfrequency": value = centerFrequency; return true;
            case "feedback": value = feedback; return true;
            default: return base.OnTryGetParameter(foldedName, out value);
        }
    }

    private double RunCascade(double input, double[] state)
    {
        double signal = input;

        for (int stage = 0; stage < AllPassStageCount; stage++)
        {
            double output = (coefficient * signal) + state[stage];
            double next = signal - (coefficient * output);
            state[stage] = Math.Abs(next) < DenormalFloor ? 0.0 : next;
            signal = output;
        }

        return signal;
    }

    private void UpdateLfoIncrement() => lfoIncrement = modRate / SampleRate;

    private void UpdateCoefficient()
    {
        // MEASURED: fc = centre * 2^(5 * modDepth * -sin(2*pi*rate*t)). The negative sine starts the
        // sweep at the centre and takes it DOWNWARD first, and it is zero-centred on the centre
        // frequency, so modRate="0" freezes the chain there whatever the depth says.
        double sweep = -Math.Sin(2.0 * Math.PI * lfoPhase);
        double corner = centerFrequency * Math.Pow(2.0, SweepOctavesAtFullDepth * modDepth * sweep);

        double ceiling = SampleRate * 0.45;
        if (corner > ceiling) { corner = ceiling; }
        if (corner < MinimumSweptFrequency) { corner = MinimumSweptFrequency; }

        cornerFrequency = corner;

        double warped = Math.Tan(Math.PI * corner / SampleRate);
        coefficient = (warped - 1.0) / (warped + 1.0);
    }
}

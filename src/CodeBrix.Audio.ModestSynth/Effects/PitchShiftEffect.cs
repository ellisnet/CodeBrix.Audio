using System;
using CodeBrix.Audio.ModestSynth.Effects.Internal;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>pitch_shift</c> effect: the guide's "old-school pitch shifter", a delay line read by two
/// overlapping grains whose delay slides at the rate the shift asks for.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>pitchShift</c> is a number of semitones from -24 to
/// 24 (default 0, no shift) and <c>mix</c> is the dry/wet crossfade (default 0.5).
/// </para>
/// <para>
/// HOW IT WORKS. Two read pointers sit half a grain apart on a
/// <see cref="GrainMilliseconds" />-millisecond delay line. Each slides towards or away from the
/// write pointer at <c>2^(semitones/12) - 1</c> samples per sample, which resamples the signal by
/// exactly the ratio the shift asks for, and each is windowed by a raised cosine that reaches zero
/// where the pointer wraps. The two windows sum to one, so nothing steps and nothing drops out. The
/// classic cost of the classic method is a periodic warble at the wrap rate - about ten hertz at
/// seven semitones - which is what "old-school" means.
/// </para>
/// <para>
/// WHAT IS MEASURED. The reference player's shift is accurate semitones (+7 on 440 Hz gives 659.3
/// Hz, measured within a cent and a half) and its <c>mix</c> is a crossfade. Both are reproduced.
/// </para>
/// <para>
/// WHAT IS NOT. The reference's grain length, its window and whether it delays the DRY path to
/// match the wet one were not measured. This implementation does NOT delay the dry path, so at a
/// mix between 0 and 1 the two paths are a grain apart and comb against each other, exactly as an
/// analogue-era shifter does. The grain length is the one number to change if the warble rate ever
/// needs to match a recording.
/// </para>
/// </remarks>
public sealed class PitchShiftEffect : ModestMixEffectBase
{
    /// <summary>The grain length in milliseconds, which is also the wet path's latency.</summary>
    /// <remarks>Not measured against the reference. Fifty milliseconds is the classic choice.</remarks>
    public const double GrainMilliseconds = 50.0;

    /// <summary>The largest downward shift the format allows, in semitones.</summary>
    public const double MinimumPitchShift = -24.0;

    /// <summary>The largest upward shift the format allows, in semitones.</summary>
    public const double MaximumPitchShift = 24.0;

    private ModestDelayLine leftLine;
    private ModestDelayLine rightLine;

    private double pitchShift;
    private double grainSamples = 1.0;
    private double grainPhase;

    /// <summary>Creates a pitch shifter carrying the guide's documented defaults.</summary>
    public PitchShiftEffect()
        : base(0.5)
    {
    }

    /// <summary>
    /// How far to shift, in semitones, from -24 (two octaves down) to 24 (two octaves up). Default 0.
    /// </summary>
    public double PitchShift
    {
        get { return pitchShift; }
        set { pitchShift = Clamp(value, MinimumPitchShift, MaximumPitchShift, pitchShift); }
    }

    /// <summary>The grain length in samples at the prepared rate, which is also the wet latency.</summary>
    public int GrainLengthSamples
    {
        get { return (int)grainSamples; }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate)
    {
        int length = (int)Math.Round(GrainMilliseconds * 0.001 * sampleRate);
        if (length < 8) { length = 8; }

        grainSamples = length;
        leftLine = new ModestDelayLine(length + 4);
        rightLine = new ModestDelayLine(length + 4);
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
        leftLine.Reset();
        rightLine.Reset();
        grainPhase = 0.0;
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;

        double ratio = Math.Pow(2.0, pitchShift / 12.0);
        double slide = ratio - 1.0;
        double window = grainSamples;
        double half = window * 0.5;
        double twoPiOverWindow = 2.0 * Math.PI / window;

        for (int i = 0; i < frames; i++)
        {
            double dryLeft = left[i];
            double dryRight = right[i];

            leftLine.Write(dryLeft);
            rightLine.Write(dryRight);

            double firstPhase = grainPhase;
            double secondPhase = grainPhase + half;
            if (secondPhase >= window) { secondPhase -= window; }

            // Zero at both ends of the grain, so the wrap is silent; the pair sums to unity.
            double firstGain = 0.5 - (0.5 * Math.Cos(twoPiOverWindow * firstPhase));
            double secondGain = 0.5 - (0.5 * Math.Cos(twoPiOverWindow * secondPhase));

            double firstDelay = window - firstPhase;
            double secondDelay = window - secondPhase;

            double wetLeft =
                (firstGain * leftLine.Read(firstDelay)) + (secondGain * leftLine.Read(secondDelay));
            double wetRight =
                (firstGain * rightLine.Read(firstDelay)) + (secondGain * rightLine.Read(secondDelay));

            left[i] = (float)((dryGain * dryLeft) + (mix * wetLeft));
            right[i] = (float)((dryGain * dryRight) + (mix * wetRight));

            grainPhase += slide;
            if (grainPhase >= window) { grainPhase -= window; }
            else if (grainPhase < 0.0) { grainPhase += window; }
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        if (foldedName == "pitchshift")
        {
            PitchShift = value;
            return true;
        }

        return base.OnTrySetParameter(foldedName, value);
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        if (foldedName == "pitchshift")
        {
            value = pitchShift;
            return true;
        }

        return base.OnTryGetParameter(foldedName, out value);
    }
}

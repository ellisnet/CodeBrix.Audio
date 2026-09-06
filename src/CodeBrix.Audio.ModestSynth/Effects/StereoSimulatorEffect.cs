using System;
using CodeBrix.Audio.ModestSynth.Effects.Internal;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>stereo_simulator</c> effect: one signal becomes two by adding a decorrelated copy to one
/// side and subtracting it from the other, with <c>width</c> deciding how much of the original is
/// left in the middle.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>algorithm</c> (<c>lauridsen</c>, <c>schroeder</c> or
/// <c>adt</c>, default <c>adt</c>), <c>width</c> 0 to 1 (default 0.5), <c>delayTime</c> 0.001 to
/// 0.030 seconds (default 0.005), and <c>modRate</c> 0.1 to 10 Hz (default 0.5) and
/// <c>modDepth</c> 0 to 1 (default 0.3), which move the delay and matter only to <c>adt</c>.
/// </para>
/// <para>
/// THE WIDTH LAW IS MEASURED. All three algorithms at <c>width="0.5"</c> put the reference player's
/// mono sum 6.02 dB down, so width is a mid/side blend in which the middle is scaled by
/// <c>1 - width</c>: 0 leaves the signal alone, 0.5 halves the middle, 1 removes it entirely. The
/// side gains below are then chosen so that the three algorithms decorrelate in the measured order
/// - lauridsen fully (side equal to mid, left and right uncorrelated), adt in the middle (side 6 dB
/// below mid) and schroeder least (side about 11.6 dB below mid).
/// </para>
/// <para>
/// WHAT IS NOT MEASURED. The delay lengths inside each algorithm, schroeder's second delay, the
/// modulation shape, and what the effect does to a signal that is ALREADY stereo. This
/// implementation sums the input to mono first, as the guide's own description of the effect
/// implies; a stereo input therefore loses its own side signal.
/// </para>
/// </remarks>
public sealed class StereoSimulatorEffect : ModestEffectBase
{
    /// <summary>The longest delay any algorithm reads, in seconds, which sets the buffer size.</summary>
    public const double MaximumDelaySeconds = 0.070;

    /// <summary>Schroeder's second delay, as a multiple of <see cref="DelayTime" />.</summary>
    /// <remarks>Not measured. An irrational-looking ratio keeps the two combs from lining up.</remarks>
    public const double SchroederSecondDelayRatio = 1.7;

    // The side gains that reproduce the measured decorrelation. A delayed copy of noise has the
    // same level as the original, so lauridsen's raw side already equals the mid at width 0.5 and
    // needs no scaling; adt is asked for half of that; schroeder's raw side is the average of two
    // independent delays, which is 3 dB quieter to begin with, so its gain makes up the difference
    // to the measured -11.57 dB.
    private const double LauridsenSideGain = 1.0;
    private const double AdtSideGain = 0.5;
    private const double SchroederSideGain = 0.3728;

    private ModestDelayLine line;

    private ModestStereoAlgorithm algorithm = ModestStereoAlgorithm.Adt;
    private double width = 0.5;
    private double delayTime = 0.005;
    private double modRate = 0.5;
    private double modDepth = 0.3;

    private double lfoPhase;

    /// <summary>Creates a stereo simulator carrying the guide's documented defaults.</summary>
    public StereoSimulatorEffect()
    {
    }

    /// <summary>Which algorithm generates the side signal. Default <see cref="ModestStereoAlgorithm.Adt" />.</summary>
    public ModestStereoAlgorithm Algorithm
    {
        get { return algorithm; }
        set
        {
            if (value >= ModestStereoAlgorithm.Lauridsen && value <= ModestStereoAlgorithm.Adt)
            {
                algorithm = value;
            }
        }
    }

    /// <summary>
    /// The stereo spread, which doubles as the dry/wet amount: 0 is the untouched mono signal, 0.5
    /// halves the middle, 1 removes it. Default 0.5.
    /// </summary>
    public double Width
    {
        get { return width; }
        set { width = Clamp(value, 0.0, 1.0, width); }
    }

    /// <summary>The delay the side signal is built from, in seconds, 0.001 to 0.030. Default 0.005.</summary>
    public double DelayTime
    {
        get { return delayTime; }
        set { delayTime = Clamp(value, 0.001, 0.030, delayTime); }
    }

    /// <summary>The modulation rate in Hz, 0.1 to 10. Default 0.5. Used by <c>adt</c> only.</summary>
    public double ModRate
    {
        get { return modRate; }
        set { modRate = Clamp(value, 0.1, 10.0, modRate); }
    }

    /// <summary>The modulation depth, 0 to 1. Default 0.3. Used by <c>adt</c> only.</summary>
    public double ModDepth
    {
        get { return modDepth; }
        set { modDepth = Clamp(value, 0.0, 1.0, modDepth); }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate)
    {
        int capacity = (int)Math.Ceiling(MaximumDelaySeconds * SchroederSecondDelayRatio * sampleRate);
        if (capacity < 16) { capacity = 16; }

        line = new ModestDelayLine(capacity);
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
        line.Reset();
        lfoPhase = 0.0;
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double midGain = 1.0 - width;
        double baseDelay = delayTime * SampleRate;
        double secondDelay = baseDelay * SchroederSecondDelayRatio;
        double lfoIncrement = modRate / SampleRate;

        double sideGain = width * SideGainFor(algorithm);

        for (int i = 0; i < frames; i++)
        {
            double mono = 0.5 * (left[i] + right[i]);
            line.Write(mono);

            double sideRaw;

            switch (algorithm)
            {
                case ModestStereoAlgorithm.Lauridsen:
                    sideRaw = line.Read(baseDelay);
                    break;

                case ModestStereoAlgorithm.Schroeder:
                    sideRaw = 0.5 * (line.Read(baseDelay) + line.Read(secondDelay));
                    break;

                default:
                    double sweep = Math.Sin(2.0 * Math.PI * lfoPhase);
                    sideRaw = line.Read(baseDelay * (1.0 + (modDepth * sweep)));
                    break;
            }

            double mid = midGain * mono;
            double side = sideGain * sideRaw;

            left[i] = (float)(mid + side);
            right[i] = (float)(mid - side);

            lfoPhase += lfoIncrement;
            if (lfoPhase >= 1.0) { lfoPhase -= 1.0; }
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        switch (foldedName)
        {
            case "width": Width = value; return true;
            case "delaytime": DelayTime = value; return true;
            case "modrate": ModRate = value; return true;
            case "moddepth": ModDepth = value; return true;
            case "algorithm": Algorithm = (ModestStereoAlgorithm)(int)Math.Round(value); return true;
            default: return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "width": value = width; return true;
            case "delaytime": value = delayTime; return true;
            case "modrate": value = modRate; return true;
            case "moddepth": value = modDepth; return true;
            case "algorithm": value = (double)(int)algorithm; return true;
            default: return base.OnTryGetParameter(foldedName, out value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, string value)
    {
        if (foldedName != "algorithm") { return base.OnTrySetParameter(foldedName, value); }

        string name = value == null ? null : value.Trim().ToLowerInvariant();

        switch (name)
        {
            case "lauridsen": algorithm = ModestStereoAlgorithm.Lauridsen; return true;
            case "schroeder": algorithm = ModestStereoAlgorithm.Schroeder; return true;
            case "adt": algorithm = ModestStereoAlgorithm.Adt; return true;
            default: return true;
        }
    }

    private static double SideGainFor(ModestStereoAlgorithm value)
    {
        switch (value)
        {
            case ModestStereoAlgorithm.Lauridsen: return LauridsenSideGain;
            case ModestStereoAlgorithm.Schroeder: return SchroederSideGain;
            default: return AdtSideGain;
        }
    }
}

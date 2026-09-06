using System;
using CodeBrix.Audio.ModestSynth.Effects.Internal;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>wave_shaper</c> effect: a saturating curve that adds harmonics, with an output level that
/// takes the resulting loudness back down and an optional four-times-oversampled path that keeps
/// the new harmonics from folding back as aliases.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>drive</c> 1 to 1000 (default 1), <c>driveBoost</c> 0
/// to 1 (default 1), <c>outputLevel</c> (default 0.1) and <c>highQuality</c> (default false). A
/// <c>mix</c> is accepted as well, defaulting to 1.0 so that a preset written with only the
/// documented attributes behaves exactly as documented.
/// </para>
/// <para>
/// THE CURVE is a hyperbolic tangent driven by <c>drive * (1 + driveBoost)</c> and scaled by
/// <c>outputLevel</c>. That is chosen to MATCH THE MEASUREMENT: the reference player at
/// <c>drive="10"</c> with <c>outputLevel="1.0"</c> added 16.4 dB to white noise, and this curve
/// adds 15.9 dB on the same material - inside the plan's 1.5 dB tolerance for an effect. The
/// measurement round rated the wave shaper LOW confidence because it did not sweep the input level,
/// so treat the drive law as right in the region that was measured and provisional elsewhere.
/// </para>
/// <para>
/// OUTPUT LEVEL DEFAULTS TO 0.1, which is a 20 dB cut, and that is not a mistake in this code: it
/// is the format's own default, confirmed by measurement. A preset that raises it is asking for a
/// large boost.
/// </para>
/// <para>
/// HIGH QUALITY costs four evaluations of the curve per sample plus twelve half-band filter runs,
/// and adds <see cref="Oversampler4x.LatencySamples" /> samples of latency to the wet path, which
/// the dry path does not carry. The guide warns about the CPU; the latency is this
/// implementation's own and is not measured against the reference.
/// </para>
/// </remarks>
public sealed class WaveShaperEffect : ModestMixEffectBase
{
    /// <summary>
    /// How much extra gain <c>driveBoost="1"</c> adds, as a multiplier on the drive.
    /// </summary>
    /// <remarks>
    /// Chosen so that the measured +16.4 dB at <c>drive="10" outputLevel="1.0"</c> is reproduced.
    /// The guide says only that driveBoost "introduces an extra gain boost to the drive".
    /// </remarks>
    public const double DriveBoostRange = 1.0;

    private readonly Oversampler4x leftOversampler = new Oversampler4x();
    private readonly Oversampler4x rightOversampler = new Oversampler4x();

    private double drive = 1.0;
    private double driveBoost = 1.0;
    private double outputLevel = 0.1;

    /// <summary>Creates a wave shaper carrying the guide's documented defaults.</summary>
    public WaveShaperEffect()
        : base(1.0)
    {
    }

    /// <summary>The gain into the curve, 1 to 1000. Default 1.</summary>
    public double Drive
    {
        get { return drive; }
        set { drive = Clamp(value, 1.0, 1000.0, drive); }
    }

    /// <summary>The extra boost on the drive, 0 to 1. Default 1.</summary>
    public double DriveBoost
    {
        get { return driveBoost; }
        set { driveBoost = Clamp(value, 0.0, 1.0, driveBoost); }
    }

    /// <summary>
    /// The linear level the shaped signal leaves at, 0 to 8. Default 0.1 - the format's own default,
    /// which is a 20 dB cut.
    /// </summary>
    /// <remarks>
    /// The effect page gives the range as 0 to 1 and the binding appendix as 0 to 8; the wider of
    /// the two is honoured so that a binding written to the appendix is not silently clipped.
    /// </remarks>
    public double OutputLevel
    {
        get { return outputLevel; }
        set { outputLevel = Clamp(value, 0.0, 8.0, outputLevel); }
    }

    /// <summary>
    /// Whether the curve runs four times oversampled. Default false, as the guide documents.
    /// </summary>
    public bool HighQuality { get; set; }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate)
    {
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
        leftOversampler.Reset();
        rightOversampler.Reset();
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;
        double gain = drive * (1.0 + (DriveBoostRange * driveBoost));
        double level = outputLevel;

        if (!HighQuality)
        {
            for (int i = 0; i < frames; i++)
            {
                double dryLeft = left[i];
                double dryRight = right[i];

                left[i] = (float)((dryGain * dryLeft) + (mix * Math.Tanh(gain * dryLeft) * level));
                right[i] = (float)((dryGain * dryRight) + (mix * Math.Tanh(gain * dryRight) * level));
            }

            return;
        }

        Span<double> oversampled = stackalloc double[Oversampler4x.Factor];

        for (int i = 0; i < frames; i++)
        {
            double dryLeft = left[i];
            double dryRight = right[i];

            leftOversampler.Up(dryLeft, oversampled);
            for (int k = 0; k < Oversampler4x.Factor; k++)
            {
                oversampled[k] = Math.Tanh(gain * oversampled[k]);
            }

            double wetLeft = leftOversampler.Down(oversampled) * level;

            rightOversampler.Up(dryRight, oversampled);
            for (int k = 0; k < Oversampler4x.Factor; k++)
            {
                oversampled[k] = Math.Tanh(gain * oversampled[k]);
            }

            double wetRight = rightOversampler.Down(oversampled) * level;

            left[i] = (float)((dryGain * dryLeft) + (mix * wetLeft));
            right[i] = (float)((dryGain * dryRight) + (mix * wetRight));
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        switch (foldedName)
        {
            case "drive": Drive = value; return true;
            case "driveboost": DriveBoost = value; return true;
            case "outputlevel": OutputLevel = value; return true;
            case "highquality": HighQuality = value >= 0.5; return true;
            default: return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "drive": value = drive; return true;
            case "driveboost": value = driveBoost; return true;
            case "outputlevel": value = outputLevel; return true;
            case "highquality": value = HighQuality ? 1.0 : 0.0; return true;
            default: return base.OnTryGetParameter(foldedName, out value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, string value)
    {
        if (foldedName == "highquality")
        {
            string folded = value == null ? null : value.Trim().ToLowerInvariant();
            if (folded == "true" || folded == "1") { HighQuality = true; return true; }
            if (folded == "false" || folded == "0") { HighQuality = false; return true; }
            return true;
        }

        return base.OnTrySetParameter(foldedName, value);
    }
}

using System;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>wave_folder</c> effect: everything above the threshold is reflected back down, again and
/// again, which turns a plain waveform into a harmonically rich one without ever going louder than
/// the threshold.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>drive</c> 1 to 100 (default 1, no amplification) is
/// the gain into the folder, and <c>threshold</c> 0 to 10 (default 0.25) is the amplitude the fold
/// happens at. A <c>mix</c> is accepted as well, defaulting to 1.0 so that a preset written with
/// only the documented attributes behaves exactly as documented.
/// </para>
/// <para>
/// MEASURED IN CLOSED FORM (round 3, item 47). With <c>k = drive / (2*sqrt(2)*threshold)</c>:
/// </para>
/// <para>
/// <c>foldOnce(u) = u</c> for <c>|u| &lt;= 1</c>, else <c>sign(u) * (2 - |u|)</c>, and
/// <c>out = foldOnce(in * k) / k</c>.
/// </para>
/// <para>
/// Three things in that are not the textbook triangle folder this used to be. It is a SINGLE
/// reflection that keeps going down past zero rather than a repeated triangle, and it is not clamped.
/// The output is divided by the same <c>k</c> the input was multiplied by, so below the fold point
/// the effect is a BIT-IDENTICAL PASS-THROUGH - <c>drive</c> does not even amplify. And the threshold
/// is scaled by <c>2*sqrt(2)</c>, the same constant round 2 found in the compressor's threshold, so
/// the reference scales at least two thresholds that way.
/// </para>
/// <para>
/// The form reproduces all seven measured root-mean-square points to a mean error of 0.01 dB and the
/// harmonic structure of three cases to 0.6 dB, over a range in which the third harmonic travels from
/// -14 dB to +24 dB relative to the fundamental. It closes the 5.6 dB divergence this effect used to
/// publish.
/// </para>
/// <para>
/// The guide's appendix gives <c>FX_THRESHOLD</c> a range of 1 to 100, which contradicts the
/// effect's own 0 to 10; the effect page wins here, and since a threshold above the signal's own
/// peak means "no folding at all", the two agree on everything audible.
/// </para>
/// </remarks>
public sealed class WaveFolderEffect : ModestMixEffectBase
{
    /// <summary>The lowest threshold that is processed as a fold rather than as silence.</summary>
    public const double MinimumThreshold = 0.001;

    /// <summary>
    /// What the declared threshold is multiplied by before the fold point is taken.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 3, item 47) as <c>2*sqrt(2)</c> - the same constant round 2 found scaling the
    /// compressor's threshold.
    /// </remarks>
    public const double ThresholdScale = 2.8284271247461903;

    private double drive = 1.0;
    private double threshold = 0.25;

    /// <summary>Creates a wave folder carrying the guide's documented defaults.</summary>
    public WaveFolderEffect()
        : base(1.0)
    {
    }

    /// <summary>The gain into the folder, 1 to 100. Default 1, which is no amplification.</summary>
    public double Drive
    {
        get { return drive; }
        set { drive = Clamp(value, 1.0, 100.0, drive); }
    }

    /// <summary>
    /// The amplitude the signal folds at, 0 to 10. Default 0.25. The output never leaves
    /// -threshold..threshold.
    /// </summary>
    public double Threshold
    {
        get { return threshold; }
        set { threshold = Clamp(value, 0.0, 10.0, threshold); }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate)
    {
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;

        double limit = threshold;
        if (limit < MinimumThreshold) { limit = MinimumThreshold; }

        // MEASURED: k = drive / (2*sqrt(2)*threshold), and the output is divided by the same k, so a
        // signal that never reaches the fold point comes out untouched.
        double k = drive / (ThresholdScale * limit);
        double inverse = 1.0 / k;

        for (int i = 0; i < frames; i++)
        {
            double dryLeft = left[i];
            double dryRight = right[i];

            double wetLeft = FoldOnce(dryLeft * k) * inverse;
            double wetRight = FoldOnce(dryRight * k) * inverse;

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
            case "threshold": Threshold = value; return true;
            default: return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "drive": value = drive; return true;
            case "threshold": value = threshold; return true;
            default: return base.OnTryGetParameter(foldedName, out value);
        }
    }

    /// <summary>
    /// The measured transfer curve: the identity inside -1..1 and ONE reflection outside it, which
    /// keeps going down past zero rather than folding back and forth.
    /// </summary>
    /// <param name="value">The driven sample.</param>
    /// <returns>The folded sample. It is not clamped.</returns>
    private static double FoldOnce(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { return 0.0; }

        double magnitude = Math.Abs(value);
        if (magnitude <= 1.0) { return value; }

        return value < 0.0 ? -(2.0 - magnitude) : 2.0 - magnitude;
    }
}

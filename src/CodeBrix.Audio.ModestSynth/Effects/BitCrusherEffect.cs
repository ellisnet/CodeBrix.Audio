using System;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>bit_crusher</c> effect: fewer bits and fewer samples per second, which is the whole of
/// the lo-fi digital sound.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>bitDepth</c> 1 to 24 (default 24, which is clean),
/// <c>sampleRateReduction</c> 1 to 32 (default 1, which is no reduction) and <c>mix</c> 0 to 1
/// (default 1.0).
/// </para>
/// <para>
/// BIT DEPTH is a MID-TREAD quantiser with NO DITHER whose step is
/// <c>2*sqrt(2) / 2^(bitDepth-1)</c>: the signal is rounded to the nearest multiple of the step, and
/// zero is always one of the levels. Non-integer depths are honoured, because a knob bound to
/// <c>FX_BIT_DEPTH</c> sweeps through them.
/// </para>
/// <para>
/// THE FULL SCALE IS <c>2*sqrt(2)</c>, NOT 1 - the same constant the compressor threshold and the
/// wave folder use. That is measured, not chosen, and it has one consequence worth knowing: a signal
/// whose PEAK is below <c>STEP/2 = 2^(1.5-bitDepth)</c> is crushed to DIGITAL SILENCE. At
/// <c>bitDepth="4"</c> that threshold is 0.1768, so a sine at -20 dBFS RMS vanishes completely.
/// </para>
/// <para>
/// SAMPLE-RATE REDUCTION is a sample-and-hold: a reduction of four holds each sample for four
/// output samples. Non-integer factors are honoured the same way, by a phase accumulator, so the
/// hold length alternates rather than jumping.
/// </para>
/// <para>
/// WHAT IS MEASURED. The reference's <c>sampleRateReduction="8"</c> pulled the spectral centroid of
/// white noise from 7.5 kHz to 2.1 kHz, which is the zero-order hold's own roll-off and is what a
/// sample-and-hold gives.
/// </para>
/// <para>
/// The apparent "gain" of a crusher is a consequence of the step rather than a parameter of its own:
/// the reference read +1.21 dB where the signal spans three levels, +0.44 dB where it spans five, and
/// 0.00 dB once the step is small against the signal. This quantiser reproduces all three.
/// </para>
/// </remarks>
public sealed class BitCrusherEffect : ModestMixEffectBase
{
    /// <summary>The cleanest bit depth the format allows.</summary>
    public const double MaximumBitDepth = 24.0;

    /// <summary>The coarsest bit depth the format allows.</summary>
    public const double MinimumBitDepth = 1.0;

    /// <summary>
    /// The notional full scale the quantiser's step is measured against, which is not 1.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 4, item 55) as <c>2*sqrt(2)</c>, read off the output staircase at bit depths
    /// 3, 4 and 8 - the same constant the compressor threshold and the wave folder use. The step is
    /// <c>FullScale / 2^(bitDepth-1)</c>.
    /// </remarks>
    public const double FullScale = 2.8284271247461903;

    /// <summary>The largest sample-rate reduction factor the format allows.</summary>
    public const double MaximumSampleRateReduction = 32.0;

    private double bitDepth = MaximumBitDepth;
    private double sampleRateReduction = 1.0;

    private double heldLeft;
    private double heldRight;
    private double holdPhase = 1.0;

    /// <summary>Creates a bit crusher carrying the guide's documented defaults.</summary>
    public BitCrusherEffect()
        : base(1.0)
    {
    }

    /// <summary>How many bits the signal is quantised to, 1 to 24. Default 24.</summary>
    public double BitDepth
    {
        get { return bitDepth; }
        set { bitDepth = Clamp(value, MinimumBitDepth, MaximumBitDepth, bitDepth); }
    }

    /// <summary>
    /// How far the effective sample rate is divided, 1 to 32. Default 1, which holds nothing.
    /// </summary>
    public double SampleRateReduction
    {
        get { return sampleRateReduction; }
        set { sampleRateReduction = Clamp(value, 1.0, MaximumSampleRateReduction, sampleRateReduction); }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate)
    {
    }

    /// <inheritdoc />
    protected override void OnReset()
    {
        heldLeft = 0.0;
        heldRight = 0.0;

        // One means "capture on the very next sample", so nothing is held over from before a reset.
        holdPhase = 1.0;
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;

        // MEASURED (round 4, item 55): STEP = 2*sqrt(2) / 2^(bitDepth-1), mid-tread, no dither.
        double step = FullScale / Math.Pow(2.0, bitDepth - 1.0);
        double perStep = 1.0 / step;
        double increment = 1.0 / sampleRateReduction;

        for (int i = 0; i < frames; i++)
        {
            double dryLeft = left[i];
            double dryRight = right[i];

            if (holdPhase >= 1.0)
            {
                holdPhase -= 1.0;
                heldLeft = Math.Round(dryLeft * perStep) * step;
                heldRight = Math.Round(dryRight * perStep) * step;
            }

            holdPhase += increment;

            left[i] = (float)((dryGain * dryLeft) + (mix * heldLeft));
            right[i] = (float)((dryGain * dryRight) + (mix * heldRight));
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        switch (foldedName)
        {
            case "bitdepth": BitDepth = value; return true;
            case "sampleratereduction": SampleRateReduction = value; return true;
            default: return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "bitdepth": value = bitDepth; return true;
            case "sampleratereduction": value = sampleRateReduction; return true;
            default: return base.OnTryGetParameter(foldedName, out value);
        }
    }
}

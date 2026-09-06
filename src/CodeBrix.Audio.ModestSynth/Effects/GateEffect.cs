using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The <c>gate</c> effect: roughly every fifty milliseconds it flips a weighted coin on whether to
/// let the signal through or cut it to silence, crossfading between the two so the transitions do
/// not click.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow the developer guide: <c>amount</c> 0 to 1 (default 0.5) is the probability
/// that any one window closes, and <c>mix</c> 0 to 1 (default 1.0) is the dry/wet crossfade. At
/// <c>amount="0"</c> nothing is ever gated and the block passes through untouched; at
/// <c>amount="1"</c> every window closes.
/// </para>
/// <para>
/// DETERMINISM. The coin is a seeded generator, so two gates with the same <see cref="Seed" />
/// render the same dropouts, and <see cref="ModestEffectBase.Reset" /> restarts the sequence. That
/// is the family rule for anything random and it is what makes a rendered file reproducible. When
/// the effect is built from a preset the seed is derived from the effect's position in its chain,
/// so two gates in one instrument do not stutter in lock step.
/// </para>
/// <para>
/// WHAT IS MEASURED. The reference player at <c>amount="0.5"</c> lost 2.4 dB of average level and
/// dropped 30 dB inside a two-second note, which is the roughly half-open duty cycle the guide
/// describes. This one loses about 3 dB, the figure a half-open gate gives exactly.
/// </para>
/// <para>
/// WHAT IS NOT. The reference's window length is documented as "roughly every 50 milliseconds" and
/// was never measured; nor was its crossfade length, nor whether the window length itself is
/// randomised. This implementation uses fixed windows of <see cref="WindowSeconds" /> with
/// <see cref="EdgeSeconds" /> raised-cosine crossfades at their starts.
/// </para>
/// </remarks>
public sealed class GateEffect : ModestMixEffectBase
{
    /// <summary>The default decision-window length in seconds, as the guide describes it.</summary>
    public const double DefaultWindowSeconds = 0.050;

    /// <summary>The crossfade at a window boundary, in seconds. Not measured against the reference.</summary>
    public const double EdgeSeconds = 0.002;

    /// <summary>The seed a gate uses when nothing else set one.</summary>
    public const uint DefaultSeed = 0x51A7E9C3u;

    private double amount = 0.5;
    private double windowSeconds = DefaultWindowSeconds;
    private uint seed = DefaultSeed;

    private ModestRandom random = new ModestRandom(DefaultSeed);

    private int windowSamples = 1;
    private int edgeSamples = 1;
    private int position;
    private double previousTarget = 1.0;
    private double target = 1.0;

    /// <summary>Creates a gate carrying the guide's documented defaults.</summary>
    public GateEffect()
        : base(1.0)
    {
    }

    /// <summary>
    /// The probability that any one decision window cuts to silence, 0 to 1. Default 0.5.
    /// </summary>
    public double Amount
    {
        get { return amount; }
        set { amount = Clamp(value, 0.0, 1.0, amount); }
    }

    /// <summary>
    /// How long one decision window lasts, in seconds. Default 0.050, clamped to 0.001..1.0. The
    /// format has no attribute for this; it is here so the effect is worth having standalone.
    /// </summary>
    public double WindowSeconds
    {
        get { return windowSeconds; }
        set
        {
            windowSeconds = Clamp(value, 0.001, 1.0, windowSeconds);
            UpdateWindow();
        }
    }

    /// <summary>
    /// The seed behind the coin. Setting it restarts the sequence immediately, so a render can be
    /// made reproducible without a full reset.
    /// </summary>
    public uint Seed
    {
        get { return seed; }
        set
        {
            seed = value;
            random.Reseed(value);
        }
    }

    /// <summary>The gain the gate is currently applying, 0 (closed) to 1 (open).</summary>
    public double CurrentGain
    {
        get { return GainAt(position); }
    }

    /// <inheritdoc />
    protected override void OnPrepare(int sampleRate) => UpdateWindow();

    /// <inheritdoc />
    protected override void OnReset()
    {
        random.Reseed(seed);
        position = 0;
        previousTarget = 1.0;
        target = 1.0;
        UpdateWindow();
    }

    /// <inheritdoc />
    protected override void OnProcess(float[] left, float[] right, int frames)
    {
        double mix = Mix;
        double dryGain = 1.0 - mix;

        for (int i = 0; i < frames; i++)
        {
            if (position == 0)
            {
                previousTarget = target;
                target = NextCoin() ? 0.0 : 1.0;
            }

            double gain = GainAt(position);

            double dryLeft = left[i];
            double dryRight = right[i];

            left[i] = (float)((dryGain * dryLeft) + (mix * gain * dryLeft));
            right[i] = (float)((dryGain * dryRight) + (mix * gain * dryRight));

            position++;
            if (position >= windowSamples) { position = 0; }
        }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        // The binding appendix calls it FX_GATE_AMOUNT; the effect's own attribute is "amount".
        switch (foldedName)
        {
            case "amount":
            case "gateamount":
                Amount = value;
                return true;

            default:
                return base.OnTrySetParameter(foldedName, value);
        }
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        switch (foldedName)
        {
            case "amount":
            case "gateamount":
                value = amount;
                return true;

            default:
                return base.OnTryGetParameter(foldedName, out value);
        }
    }

    private bool NextCoin()
    {
        // A draw in [0,1) against the probability: amount 0 never closes, amount 1 always does.
        double draw = random.NextUInt32() * (1.0 / 4294967296.0);
        return draw < amount;
    }

    private double GainAt(int index)
    {
        if (index >= edgeSamples) { return target; }

        double u = 0.5 - (0.5 * Math.Cos(Math.PI * index / edgeSamples));
        return previousTarget + ((target - previousTarget) * u);
    }

    private void UpdateWindow()
    {
        int samples = (int)Math.Round(windowSeconds * SampleRate);
        if (samples < 2) { samples = 2; }

        windowSamples = samples;

        int edge = (int)Math.Round(EdgeSeconds * SampleRate);
        if (edge < 1) { edge = 1; }
        if (edge > samples / 2) { edge = Math.Max(1, samples / 2); }

        edgeSamples = edge;

        if (position >= windowSamples) { position = 0; }
    }
}

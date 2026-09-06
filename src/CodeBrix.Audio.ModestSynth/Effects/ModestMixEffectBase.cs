namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// An effect with a dry/wet <c>mix</c>: six of the seven creative effects have one, and it always
/// means the same thing.
/// </summary>
/// <remarks>
/// The mix is a straight crossfade, <c>out = (1 - mix) * dry + mix * wet</c>, which is what the
/// reference player measures as: a phaser at <c>mix="0.5"</c> cancels completely at its notch
/// frequencies, and a pitch shifter at <c>mix="1.0"</c> has no trace of the original pitch left.
/// A mix of exactly 0 passes the block through bit-for-bit.
/// </remarks>
public abstract class ModestMixEffectBase : ModestEffectBase
{
    private double mix;

    /// <summary>
    /// Creates the effect with the mix its type defaults to.
    /// </summary>
    /// <param name="defaultMix">The documented default for this effect type, 0 to 1.</param>
    protected ModestMixEffectBase(double defaultMix)
    {
        mix = Clamp(defaultMix, 0.0, 1.0, 0.5);
    }

    /// <summary>
    /// The dry/wet crossfade: 0 is the untouched signal, 1 is the effect alone. Clamped to 0..1.
    /// </summary>
    public double Mix
    {
        get { return mix; }
        set { mix = Clamp(value, 0.0, 1.0, mix); }
    }

    /// <inheritdoc />
    protected override bool OnTrySetParameter(string foldedName, double value)
    {
        if (foldedName == "mix")
        {
            Mix = value;
            return true;
        }

        return base.OnTrySetParameter(foldedName, value);
    }

    /// <inheritdoc />
    protected override bool OnTryGetParameter(string foldedName, out double value)
    {
        if (foldedName == "mix")
        {
            value = mix;
            return true;
        }

        return base.OnTryGetParameter(foldedName, out value);
    }
}

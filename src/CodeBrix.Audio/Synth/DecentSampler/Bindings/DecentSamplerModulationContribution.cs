using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// One temporary contribution to a parameter: what a modulator is currently offering, how deeply, and
/// how it combines with the base value.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED. With a base value <c>b</c>, a translated modulator value <c>v</c>, a neutral value
/// <c>n</c>, a depth <c>a</c> (the modulator's <c>modAmount</c>) and <c>u = a * v</c>:
/// </para>
/// <list type="bullet">
/// <item><description><c>set</c>: <c>u</c> - the base is discarded entirely.</description></item>
/// <item><description><c>add</c>: <c>b + u</c>.</description></item>
/// <item><description><c>modulate</c>: <c>b + u - a * n</c> - <c>add</c> with the modulator's own
/// resting output taken out, so a modulator sitting at rest contributes nothing.</description></item>
/// <item><description><c>multiply</c>: <c>b * u</c>.</description></item>
/// </list>
/// <para>
/// <c>modAmount</c> SCALES the translated value; it is not a blend depth toward the base. <c>Neutral</c>
/// is the translation of the modulator's RESTING RAW OUTPUT - zero for a <c>&lt;midiCC&gt;</c> (so its
/// <c>modulate</c> neutral is <c>translationOutputMin</c>) and 0.5 for an <c>&lt;lfo&gt;</c> (so its
/// neutral is the middle of the output range).
/// </para>
/// </remarks>
internal readonly struct DecentSamplerModulationContribution
{
    /// <summary>Builds a contribution.</summary>
    /// <param name="behavior">How it combines with the base value.</param>
    /// <param name="amount">The depth, which scales the translated value.</param>
    /// <param name="value">The translated modulator value.</param>
    /// <param name="neutral">The translation of the modulator's resting raw output.</param>
    public DecentSamplerModulationContribution(
        DecentSamplerModBehavior behavior, double amount, double value, double neutral)
    {
        Behavior = behavior;
        Amount = amount;
        Value = value;
        Neutral = neutral;
    }

    /// <summary>How the contribution combines with the base value.</summary>
    public DecentSamplerModBehavior Behavior { get; }

    /// <summary>The depth. It scales the translated value; it is not a blend toward the base.</summary>
    public double Amount { get; }

    /// <summary>The translated modulator value.</summary>
    public double Value { get; }

    /// <summary>The translation of the modulator's resting raw output, used by the modulate behaviour.</summary>
    public double Neutral { get; }

    /// <summary>Folds this contribution into a running value.</summary>
    /// <param name="current">The value so far, starting from the base value.</param>
    /// <returns>The value after this contribution.</returns>
    public double Apply(double current) =>
        Behavior switch
        {
            DecentSamplerModBehavior.Add => current + (Value * Amount),
            DecentSamplerModBehavior.Modulate => current + ((Value - Neutral) * Amount),
            DecentSamplerModBehavior.Multiply => current * Value * Amount,
            _ => Value * Amount,
        };
}

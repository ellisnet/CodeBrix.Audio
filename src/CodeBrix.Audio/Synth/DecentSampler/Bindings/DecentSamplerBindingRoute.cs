using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// One <c>&lt;binding&gt;</c> with its targets found: what it changes, and whether it writes a base
/// value or contributes a temporary modulation.
/// </summary>
/// <remarks>
/// A binding under a control or a MIDI handler is PERMANENT: firing it writes the target's base value
/// and it stays there. A binding under a modulator is TEMPORARY: the modulator adds a contribution
/// which is combined with the base value on demand and removed when the modulator or the voice stops.
/// </remarks>
internal sealed class DecentSamplerBindingRoute
{
    internal DecentSamplerBindingRoute(
        DecentSamplerBinding binding,
        IReadOnlyList<IParameterTarget> targets,
        bool isTemporary,
        DecentSamplerModBehavior behavior,
        double amount,
        bool isVoiceScope)
    {
        Binding = binding;
        Targets = targets;
        IsTemporary = isTemporary;
        Behavior = behavior;
        Amount = amount;
        IsVoiceScope = isVoiceScope;
    }

    /// <summary>The parsed binding.</summary>
    internal DecentSamplerBinding Binding { get; }

    /// <summary>What the binding changes. Never empty for a route that was kept.</summary>
    internal IReadOnlyList<IParameterTarget> Targets { get; }

    /// <summary>Whether the binding contributes modulation rather than writing a base value.</summary>
    internal bool IsTemporary { get; }

    /// <summary>How a temporary contribution combines with the base value.</summary>
    internal DecentSamplerModBehavior Behavior { get; }

    /// <summary>
    /// The depth of a temporary contribution: it SCALES the translated value. MEASURED as ignored
    /// entirely on a permanent binding, which is why nothing reads it there.
    /// </summary>
    internal double Amount { get; }

    /// <summary>Whether the modulator behind a temporary binding runs once per voice.</summary>
    internal bool IsVoiceScope { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Binding} -> {Targets.Count} target(s){(IsTemporary ? ", temporary" : string.Empty)}";
}

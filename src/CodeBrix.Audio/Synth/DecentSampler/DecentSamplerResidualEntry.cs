using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// One line of the residual table: a group of format features this engine reads and reports but does
/// not act on, with the reason.
/// </summary>
/// <remarks>
/// Nothing here is a failure. Every feature named by an entry is parsed, kept on the model and, where
/// it has live state, exposed - it simply does not change what the instrument sounds like on its own.
/// </remarks>
public sealed class DecentSamplerResidualEntry
{
    internal DecentSamplerResidualEntry(
        string name, string reason, bool suppliedByAddOn, IReadOnlyList<DecentSamplerFeature> features)
    {
        Name = name;
        Reason = reason;
        IsSuppliedByAddOn = suppliedByAddOn;
        Features = features;
    }

    /// <summary>A short title for the group, in lower case.</summary>
    public string Name { get; }

    /// <summary>One sentence saying what is read, what is not done with it, and why.</summary>
    public string Reason { get; }

    /// <summary>
    /// Whether the CodeBrix.Audio.ModestSynth add-on package supplies these features. When it has
    /// registered, <see cref="DecentSamplerSupportedFeatures.StatusOf(DecentSamplerFeatureCategory,
    /// string, string, Engine.DecentSamplerExtensionRegistry)"/> answers
    /// <see cref="DecentSamplerFeatureStatus.Implemented"/> for every one of them.
    /// </summary>
    public bool IsSuppliedByAddOn { get; }

    /// <summary>Every feature this entry accounts for, in the feature list's own order.</summary>
    public IReadOnlyList<DecentSamplerFeature> Features { get; }

    /// <summary>The entry as one line: the name, how many features it covers, and the reason.</summary>
    /// <returns>The text.</returns>
    public override string ToString() =>
        Name + " (" + Features.Count + "): " + Reason;
}

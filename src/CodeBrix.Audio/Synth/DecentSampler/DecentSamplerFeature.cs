namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// One entry of <see cref="DecentSamplerSupportedFeatures"/>: a named piece of the Decent Sampler
/// format and how far this engine has taken it.
/// </summary>
/// <param name="category">What kind of thing the feature is.</param>
/// <param name="owner">
/// What the feature belongs to - the element name for an attribute, the attribute name for an
/// enumeration value, and an empty string when the feature stands alone.
/// </param>
/// <param name="name">The feature's own name, exactly as the format spells it.</param>
/// <param name="status">Whether the engine parses the feature or also honours it.</param>
public sealed class DecentSamplerFeature(
    DecentSamplerFeatureCategory category,
    string owner,
    string name,
    DecentSamplerFeatureStatus status)
{
    /// <summary>What kind of thing the feature is.</summary>
    public DecentSamplerFeatureCategory Category { get; } = category;

    /// <summary>
    /// What the feature belongs to: the element name for an attribute, the attribute name for an
    /// enumeration value, and an empty string when the feature stands alone.
    /// </summary>
    public string Owner { get; } = owner;

    /// <summary>The feature's own name, exactly as the format spells it.</summary>
    public string Name { get; } = name;

    /// <summary>Whether the engine parses the feature or also honours it.</summary>
    /// <remarks>
    /// Set once, when a phase of the engine makes the feature audible; see
    /// <see cref="DecentSamplerSupportedFeatures"/>.
    /// </remarks>
    public DecentSamplerFeatureStatus Status { get; internal set; } = status;

    /// <summary>
    /// The feature's qualified name: <c>owner@name</c> for an attribute, <c>owner=name</c> for an
    /// enumeration value, and just the name when it stands alone.
    /// </summary>
    public string QualifiedName =>
        Owner.Length == 0
            ? Name
            : Category == DecentSamplerFeatureCategory.EnumerationValue
                ? Owner + "=" + Name
                : Owner + "@" + Name;

    /// <inheritdoc/>
    public override string ToString() => $"{Category} {QualifiedName} ({Status})";
}

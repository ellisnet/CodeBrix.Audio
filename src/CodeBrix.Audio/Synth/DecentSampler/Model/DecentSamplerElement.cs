using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The base of every parsed <c>.dspreset</c> element: its name, where it was written, and anything in
/// it the parser did not recognise.
/// </summary>
/// <remarks>
/// Parsing is deliberately lossless. An attribute or child element this engine has no property for is
/// still kept - in <see cref="UnknownAttributes"/> and <see cref="UnknownElements"/> - so a preset
/// written for a newer version of the format can be inspected, reported on and round-tripped rather
/// than silently truncated.
/// </remarks>
public abstract class DecentSamplerElement
{
    private readonly List<DecentSamplerUnknownAttribute> _unknownAttributes = [];
    private readonly List<DecentSamplerUnknownElement> _unknownElements = [];
    private readonly List<DecentSamplerElement> _children = [];

    /// <summary>The element name as the format spells it, such as <c>labeled-knob</c>.</summary>
    public string ElementName { get; internal set; } = string.Empty;

    /// <summary>
    /// The line the element started on, or 0 when the preset was parsed from a source with no line
    /// information.
    /// </summary>
    public int LineNumber { get; internal set; }

    /// <summary>Attributes on this element that the parser did not recognise, in document order.</summary>
    public IReadOnlyList<DecentSamplerUnknownAttribute> UnknownAttributes => _unknownAttributes;

    /// <summary>Child elements the parser did not recognise, in document order.</summary>
    public IReadOnlyList<DecentSamplerUnknownElement> UnknownElements => _unknownElements;

    /// <summary>
    /// Every parsed child element, in document order. Walking this from the preset itself visits the
    /// whole document.
    /// </summary>
    public IReadOnlyList<DecentSamplerElement> Children => _children;

    /// <summary>This element and every element beneath it, depth first, in document order.</summary>
    /// <returns>The elements.</returns>
    public IEnumerable<DecentSamplerElement> Descendants()
    {
        yield return this;

        foreach (var child in _children)
        {
            foreach (var descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    internal void AddUnknownAttribute(DecentSamplerUnknownAttribute attribute) =>
        _unknownAttributes.Add(attribute);

    internal void AddUnknownElement(DecentSamplerUnknownElement element) =>
        _unknownElements.Add(element);

    internal TChild AddChild<TChild>(TChild child) where TChild : DecentSamplerElement
    {
        _children.Add(child);
        return child;
    }

    /// <inheritdoc/>
    public override string ToString() => $"<{ElementName}> (line {LineNumber})";
}

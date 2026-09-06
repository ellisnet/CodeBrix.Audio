namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A child element the parser did not recognise, kept as raw text so nothing in a preset is lost.
/// </summary>
/// <remarks>
/// Unknown elements never stop a preset from loading. Each distinct <c>parent/child</c> pair is also
/// reported once per preset in the instrument's problem list.
/// </remarks>
/// <param name="parentElementName">The element it was found under, such as <c>groups</c>.</param>
/// <param name="name">The element name, exactly as written.</param>
/// <param name="rawText">The element and its contents, serialized back to XML.</param>
/// <param name="lineNumber">The line the element started on, or 0 when the source had no line information.</param>
public sealed class DecentSamplerUnknownElement(string parentElementName, string name, string rawText, int lineNumber)
{
    /// <summary>The element it was found under, such as <c>groups</c>.</summary>
    public string ParentElementName { get; } = parentElementName;

    /// <summary>The element name, exactly as written.</summary>
    public string Name { get; } = name;

    /// <summary>The element and its contents, serialized back to XML.</summary>
    public string RawText { get; } = rawText;

    /// <summary>The line the element started on, or 0 when the source had no line information.</summary>
    public int LineNumber { get; } = lineNumber;

    /// <inheritdoc/>
    public override string ToString() => $"<{Name}> under <{ParentElementName}> (line {LineNumber})";
}

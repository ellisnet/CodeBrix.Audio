namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An attribute the parser did not recognise, kept verbatim so nothing in a preset is lost.
/// </summary>
/// <remarks>
/// Unknown attributes never stop a preset from loading. Each distinct
/// <c>element@attribute</c> pair is also reported once per preset in the instrument's problem list,
/// which is how a library written for a newer version of the format announces itself.
/// </remarks>
/// <param name="elementName">The element the attribute was written on, such as <c>sample</c>.</param>
/// <param name="name">The attribute name, exactly as written.</param>
/// <param name="value">The attribute value, exactly as written.</param>
/// <param name="lineNumber">The line the element started on, or 0 when the source had no line information.</param>
public sealed class DecentSamplerUnknownAttribute(string elementName, string name, string value, int lineNumber)
{
    /// <summary>The element the attribute was written on, such as <c>sample</c>.</summary>
    public string ElementName { get; } = elementName;

    /// <summary>The attribute name, exactly as written.</summary>
    public string Name { get; } = name;

    /// <summary>The attribute value, exactly as written.</summary>
    public string Value { get; } = value;

    /// <summary>The line the element started on, or 0 when the source had no line information.</summary>
    public int LineNumber { get; } = lineNumber;

    /// <inheritdoc/>
    public override string ToString() => $"{ElementName}@{Name}=\"{Value}\" (line {LineNumber})";
}

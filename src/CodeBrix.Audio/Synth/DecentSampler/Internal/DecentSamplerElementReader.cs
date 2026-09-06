using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Internal;

/// <summary>
/// Reads the attributes of one XML element into a model object, keeping track of which ones were
/// understood so the rest can be reported and preserved.
/// </summary>
/// <remarks>
/// Every read marks its attribute as consumed, whether or not the value parsed. <see cref="Finish"/>
/// then sweeps up whatever is left: names the format documents but this element does not use are
/// reported as unknown, and every unconsumed attribute is attached to the model object so nothing in
/// the preset is lost.
/// </remarks>
internal sealed class DecentSamplerElementReader
{
    private readonly XElement _element;
    private readonly DecentSamplerElement _target;
    private readonly DecentSamplerParseContext _context;
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    /// <summary>Prepares to read one element.</summary>
    /// <param name="element">The XML element.</param>
    /// <param name="target">The model object being filled in.</param>
    /// <param name="context">Where problems are recorded.</param>
    public DecentSamplerElementReader(
        XElement element, DecentSamplerElement target, DecentSamplerParseContext context)
    {
        _element = element;
        _target = target;
        _context = context;

        target.ElementName = element.Name.LocalName;
        target.LineNumber = LineNumberOf(element);
    }

    /// <summary>The element name, as the format spells it.</summary>
    public string ElementName => _target.ElementName;

    /// <summary>The line the element started on, or 0 when the source had no line information.</summary>
    public int LineNumber => _target.LineNumber;

    /// <summary>The line an element started on, or 0 when the source had no line information.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The line number.</returns>
    public static int LineNumberOf(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    /// <summary>The attribute's text, or null when it is absent. Marks the attribute consumed.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The text as written, trimmed of surrounding whitespace, or null.</returns>
    public string Text(string name)
    {
        _consumed.Add(name);
        var attribute = _element.Attribute(name);
        return attribute?.Value;
    }

    /// <summary>The attribute's text with surrounding whitespace removed.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The trimmed text, or null when the attribute is absent.</returns>
    public string Trimmed(string name)
    {
        var text = Text(name);
        return text?.Trim();
    }

    /// <summary>Reads a floating-point attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The number, or null when the attribute is absent or unreadable.</returns>
    public double? Double(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerValues.TryDouble(text, out var value))
        {
            return value;
        }

        Bad(name, text, "a number");
        return null;
    }

    /// <summary>Reads a whole-number attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The number, or null when the attribute is absent or unreadable.</returns>
    public int? Int(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerValues.TryInt(text, out var value))
        {
            return value;
        }

        Bad(name, text, "a whole number");
        return null;
    }

    /// <summary>Reads a frame-position attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The number of frames, or null when the attribute is absent or unreadable.</returns>
    public long? Long(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerValues.TryLong(text, out var value))
        {
            return value;
        }

        Bad(name, text, "a frame position");
        return null;
    }

    /// <summary>Reads a boolean attribute, accepting <c>true</c>, <c>false</c>, <c>1</c> and <c>0</c>.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or null when the attribute is absent or unreadable.</returns>
    public bool? Bool(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerValues.TryBool(text, out var value))
        {
            return value;
        }

        Bad(name, text, "true or false");
        return null;
    }

    /// <summary>Reads a MIDI note attribute, accepting a number or a note name.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The note number, or null when the attribute is absent or unreadable.</returns>
    public int? Note(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerValues.TryNote(text, out var value))
        {
            return value;
        }

        Bad(name, text, "a MIDI note number or name");
        return null;
    }

    /// <summary>Reads a colour attribute, keeping it as text.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The colour text, or null when the attribute is absent.</returns>
    public string Color(string name) => DecentSamplerValues.Color(Text(name));

    /// <summary>Reads a comma-separated list of names.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The names. Never null; empty when the attribute is absent.</returns>
    public string[] Names(string name) => DecentSamplerValues.TagList(Text(name));

    /// <summary>Reads a comma-separated list of numbers.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The numbers. Never null; empty when the attribute is absent or unreadable.</returns>
    public double[] Numbers(string name)
    {
        var text = Text(name);
        if (text == null)
        {
            return [];
        }

        var parts = DecentSamplerValues.TagList(text);
        var values = new List<double>(parts.Length);

        foreach (var part in parts)
        {
            if (DecentSamplerValues.TryDouble(part, out var value))
            {
                values.Add(value);
            }
            else
            {
                Bad(name, text, "a comma-separated list of numbers");
                return [];
            }
        }

        return values.ToArray();
    }

    /// <summary>Reads an enumerated attribute.</summary>
    /// <typeparam name="TEnum">The enumeration the value must name.</typeparam>
    /// <param name="name">The attribute name.</param>
    /// <param name="text">The text as written, or null when the attribute is absent.</param>
    /// <returns>The value, or null when the attribute is absent or names nothing.</returns>
    public TEnum? Enumeration<TEnum>(string name, out string text) where TEnum : struct, Enum
    {
        text = Text(name);
        if (text == null)
        {
            return null;
        }

        if (DecentSamplerEnumNames.TryParse<TEnum>(text, out var value))
        {
            return value;
        }

        Bad(name, text, "a value this engine recognises");
        return null;
    }

    /// <summary>Reads an enumerated attribute, discarding the text as written.</summary>
    /// <typeparam name="TEnum">The enumeration the value must name.</typeparam>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or null when the attribute is absent or names nothing.</returns>
    public TEnum? Enumeration<TEnum>(string name) where TEnum : struct, Enum =>
        Enumeration<TEnum>(name, out _);

    /// <summary>The attributes not yet read, in document order.</summary>
    /// <returns>The attribute name and value of each.</returns>
    public IEnumerable<KeyValuePair<string, string>> Remaining()
    {
        foreach (var attribute in _element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (!_consumed.Contains(name) && !attribute.IsNamespaceDeclaration)
            {
                yield return new KeyValuePair<string, string>(name, attribute.Value);
            }
        }
    }

    /// <summary>Marks an attribute as read without reading it.</summary>
    /// <param name="name">The attribute name.</param>
    public void Consume(string name) => _consumed.Add(name);

    /// <summary>Records a value that could not be read.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value as written.</param>
    /// <param name="expected">What the parser was looking for.</param>
    public void Bad(string name, string value, string expected) =>
        _context.ReportBadValue(ElementName, name, value, expected, LineNumber);

    /// <summary>
    /// Attaches every attribute still unread to the model object and reports each name once.
    /// </summary>
    public void Finish()
    {
        foreach (var attribute in _element.Attributes())
        {
            var name = attribute.Name.LocalName;

            if (_consumed.Contains(name) || attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            _target.AddUnknownAttribute(
                new DecentSamplerUnknownAttribute(ElementName, name, attribute.Value, LineNumber));
            _context.ReportUnknownAttribute(ElementName, name, LineNumber);
        }
    }
}

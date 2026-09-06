using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Internal;

/// <summary>
/// Collects everything the parser noticed while reading one preset: the problems, and the set of
/// unknown names already reported so each is mentioned only once.
/// </summary>
internal sealed class DecentSamplerParseContext
{
    private readonly List<string> _problems = [];
    private readonly HashSet<string> _reportedAttributes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedElements = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedValues = new(StringComparer.Ordinal);

    /// <summary>A short name for the source, used to prefix every problem.</summary>
    public string SourceName { get; set; } = "preset";

    /// <summary>Every problem noticed, in the order it was noticed.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Records a problem verbatim.</summary>
    /// <param name="problem">The message, without the source-name prefix.</param>
    public void Report(string problem) => _problems.Add(SourceName + ": " + problem);

    /// <summary>
    /// Records an attribute whose value could not be read, once per element, attribute and value.
    /// </summary>
    /// <param name="elementName">The element the attribute is on.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="value">The value as written.</param>
    /// <param name="expected">What the parser was looking for, such as "a number".</param>
    /// <param name="lineNumber">The line the element started on.</param>
    public void ReportBadValue(
        string elementName, string attributeName, string value, string expected, int lineNumber)
    {
        var key = elementName + "@" + attributeName + "=" + value;
        if (!_reportedValues.Add(key))
        {
            return;
        }

        Report($"{elementName}@{attributeName}=\"{value}\" is not {expected}; the default is used " +
               $"(line {lineNumber})");
    }

    /// <summary>Records an attribute the parser does not know, once per element and attribute.</summary>
    /// <param name="elementName">The element the attribute is on.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="lineNumber">The line the element started on.</param>
    /// <returns><see langword="true"/> when this is the first time the pair has been seen.</returns>
    public bool ReportUnknownAttribute(string elementName, string attributeName, int lineNumber)
    {
        if (!_reportedAttributes.Add(elementName + "@" + attributeName))
        {
            return false;
        }

        Report($"attribute not recognised: {elementName}@{attributeName} (line {lineNumber})");
        return true;
    }

    /// <summary>Records a child element the parser does not know, once per parent and child.</summary>
    /// <param name="parentElementName">The element it was found under.</param>
    /// <param name="elementName">The element name.</param>
    /// <param name="lineNumber">The line the element started on.</param>
    /// <returns><see langword="true"/> when this is the first time the pair has been seen.</returns>
    public bool ReportUnknownElement(string parentElementName, string elementName, int lineNumber)
    {
        if (!_reportedElements.Add(parentElementName + "/" + elementName))
        {
            return false;
        }

        Report($"element not recognised: <{elementName}> under <{parentElementName}> (line {lineNumber})");
        return true;
    }
}

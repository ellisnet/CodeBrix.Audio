using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;tab&gt;</c> beneath the <c>&lt;ui&gt;</c> element, holding its controls.
/// </summary>
public sealed class DecentSamplerUiTab : DecentSamplerElement
{
    private readonly List<DecentSamplerUiElement> _elements = [];

    /// <summary>The tab's name (<c>name</c>). The reference player does not display it.</summary>
    public string Name { get; internal set; }

    /// <summary>The elements on this tab, in document order.</summary>
    public IReadOnlyList<DecentSamplerUiElement> Elements => _elements;

    internal void Add(DecentSamplerUiElement element)
    {
        _elements.Add(element);
        AddChild(element);
    }
}

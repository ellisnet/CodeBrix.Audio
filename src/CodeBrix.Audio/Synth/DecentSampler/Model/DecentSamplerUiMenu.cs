using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;menu&gt;</c>: a drop-down whose selected option fires that option's bindings.
/// </summary>
/// <remarks>
/// A menu's <see cref="Value"/> counts from 1, not 0 - the one place in the format where an index
/// does. A value of 0 means nothing is selected.
/// </remarks>
public sealed class DecentSamplerUiMenu : DecentSamplerUiElement
{
    private readonly List<DecentSamplerUiMenuOption> _options = [];

    /// <summary>The 1-based index of the selected option (<c>value</c>). 0 selects nothing.</summary>
    public double? Value { get; internal set; }

    /// <summary>The menu text colour, eight hexadecimal ARGB digits (<c>textColor</c>).</summary>
    public string TextColor { get; internal set; }

    /// <summary>The menu background colour, eight hexadecimal ARGB digits (<c>backgroundColor</c>).</summary>
    public string BackgroundColor { get; internal set; }

    /// <summary>The selected item's text colour (<c>highlightedTextColor</c>).</summary>
    public string HighlightedTextColor { get; internal set; }

    /// <summary>The selected item's background colour (<c>highlightedBackgroundColor</c>).</summary>
    public string HighlightedBackgroundColor { get; internal set; }

    /// <summary>Vertical alignment of the menu text (<c>vAlign</c>). Default centre.</summary>
    public DecentSamplerVerticalAlignment? VerticalAlignment { get; internal set; }

    /// <summary>Horizontal alignment of the menu text (<c>hAlign</c>). Default left.</summary>
    public DecentSamplerHorizontalAlignment? HorizontalAlignment { get; internal set; }

    /// <summary>Whether the user must pick an option (<c>requireSelection</c>).</summary>
    public bool? RequireSelection { get; internal set; }

    /// <summary>The text shown while nothing is selected (<c>placeholderText</c>).</summary>
    public string PlaceholderText { get; internal set; }

    /// <summary>The options, in document order. The first is option 1 for <see cref="Value"/>.</summary>
    public IReadOnlyList<DecentSamplerUiMenuOption> Options => _options;

    internal void Add(DecentSamplerUiMenuOption option)
    {
        option.Index = _options.Count;
        _options.Add(option);
        AddChild(option);
    }
}

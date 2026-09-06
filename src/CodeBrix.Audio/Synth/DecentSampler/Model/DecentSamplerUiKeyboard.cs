using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;keyboard&gt;</c> element: how the on-screen keyboard is positioned and coloured.
/// </summary>
public sealed class DecentSamplerUiKeyboard : DecentSamplerElement
{
    private readonly List<DecentSamplerUiKeyboardColor> _colors = [];

    /// <summary>
    /// The note the keyboard is centred on when the preset loads (<c>centerNote</c>). When unset the
    /// reference player centres on the mapped range.
    /// </summary>
    public int? CenterNote { get; internal set; }

    /// <summary>The colour ranges, in document order. A binding's <c>colorIndex</c> counts these.</summary>
    public IReadOnlyList<DecentSamplerUiKeyboardColor> Colors => _colors;

    internal void Add(DecentSamplerUiKeyboardColor color)
    {
        color.Index = _colors.Count;
        _colors.Add(color);
        AddChild(color);
    }
}

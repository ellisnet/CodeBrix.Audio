namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;rectangle&gt;</c>: a filled box with an optional border, used for panels and dividers.
/// </summary>
public sealed class DecentSamplerUiRectangle : DecentSamplerUiElement
{
    /// <summary>The fill colour, eight hexadecimal ARGB digits (<c>fillColor</c>). Default FF808080.</summary>
    public string FillColor { get; internal set; }

    /// <summary>The border colour, eight hexadecimal ARGB digits (<c>borderColor</c>). Default 00000000.</summary>
    public string BorderColor { get; internal set; }

    /// <summary>The border thickness in pixels (<c>borderThickness</c>). Default 0.</summary>
    public double? BorderThickness { get; internal set; }
}

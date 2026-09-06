namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;label&gt;</c>: a block of static text, whose content a binding can change through the
/// <c>TEXT</c> parameter.
/// </summary>
public sealed class DecentSamplerUiLabel : DecentSamplerUiElement
{
    /// <summary>The text to display (<c>text</c>).</summary>
    public string Text { get; internal set; }

    /// <summary>The text colour, eight hexadecimal ARGB digits (<c>textColor</c>).</summary>
    public string TextColor { get; internal set; }

    /// <summary>The font size (<c>textSize</c>). Default 12.</summary>
    public double? TextSize { get; internal set; }

    /// <summary>Vertical alignment within the box (<c>vAlign</c>). Default centre.</summary>
    public DecentSamplerVerticalAlignment? VerticalAlignment { get; internal set; }

    /// <summary>Horizontal alignment within the box (<c>hAlign</c>). Default centre.</summary>
    public DecentSamplerHorizontalAlignment? HorizontalAlignment { get; internal set; }

    /// <summary>The direction the text runs in (<c>orientation</c>). Default horizontal.</summary>
    public DecentSamplerTextOrientation? Orientation { get; internal set; }
}

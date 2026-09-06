namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;line&gt;</c>: a straight line between two points, used for dividers and decoration.
/// </summary>
/// <remarks>
/// A line is placed by its two end points rather than by a box, so it leaves the inherited
/// <see cref="DecentSamplerUiElement.X"/>, <see cref="DecentSamplerUiElement.Y"/>,
/// <see cref="DecentSamplerUiElement.Width"/> and <see cref="DecentSamplerUiElement.Height"/> unset.
/// </remarks>
public sealed class DecentSamplerUiLine : DecentSamplerUiElement
{
    /// <summary>The horizontal coordinate of the start point (<c>x1</c>).</summary>
    public double? X1 { get; internal set; }

    /// <summary>The vertical coordinate of the start point (<c>y1</c>).</summary>
    public double? Y1 { get; internal set; }

    /// <summary>The horizontal coordinate of the end point (<c>x2</c>).</summary>
    public double? X2 { get; internal set; }

    /// <summary>The vertical coordinate of the end point (<c>y2</c>).</summary>
    public double? Y2 { get; internal set; }

    /// <summary>The line colour, eight hexadecimal ARGB digits (<c>lineColor</c>). Default FFFFFFFF.</summary>
    public string LineColor { get; internal set; }

    /// <summary>The line thickness in pixels (<c>lineThickness</c>). Default 1.</summary>
    public double? LineThickness { get; internal set; }
}

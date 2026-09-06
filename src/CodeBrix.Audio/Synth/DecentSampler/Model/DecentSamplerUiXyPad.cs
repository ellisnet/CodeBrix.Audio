namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;xyPad&gt;</c>: two sliders in one control, each axis running from 0 to 1 and driving its
/// own set of bindings.
/// </summary>
public sealed class DecentSamplerUiXyPad : DecentSamplerUiElement
{
    /// <summary>The name a host shows for this control (<c>parameterName</c>).</summary>
    public string ParameterName { get; internal set; }

    /// <summary>The diameter of the marker in pixels (<c>markerDiameter</c>). Default 10.</summary>
    public double? MarkerDiameter { get; internal set; }

    /// <summary>The marker outline colour, eight hexadecimal ARGB digits (<c>markerOutlineColor</c>).</summary>
    public string MarkerOutlineColor { get; internal set; }

    /// <summary>The marker fill colour, eight hexadecimal ARGB digits (<c>markerFillColor</c>).</summary>
    public string MarkerFillColor { get; internal set; }

    /// <summary>The pad outline colour, eight hexadecimal ARGB digits (<c>outlineColor</c>).</summary>
    public string OutlineColor { get; internal set; }

    /// <summary>The pad background colour, eight hexadecimal ARGB digits (<c>bgColor</c>).</summary>
    public string BgColor { get; internal set; }

    /// <summary>The horizontal axis's starting value, 0 to 1 (<c>xValue</c>). Default 0.</summary>
    public double? XValue { get; internal set; }

    /// <summary>The vertical axis's starting value, 0 to 1 (<c>yValue</c>). Default 0.</summary>
    public double? YValue { get; internal set; }

    /// <summary>The <c>&lt;x&gt;</c> element and its bindings, or null when the pad writes none.</summary>
    public DecentSamplerUiXyAxis XAxis { get; internal set; }

    /// <summary>The <c>&lt;y&gt;</c> element and its bindings, or null when the pad writes none.</summary>
    public DecentSamplerUiXyAxis YAxis { get; internal set; }
}

using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Anything that can appear on a tab: a control, a button, a menu, an XY pad, a label, an image, a
/// rectangle, a line or an oscilloscope.
/// </summary>
public abstract class DecentSamplerUiElement : DecentSamplerBindingHost
{
    /// <summary>
    /// This element's position in <see cref="DecentSamplerUi.Controls"/>, which is what a binding's
    /// <c>controlIndex</c> and <c>position</c> count.
    /// </summary>
    public int ControlIndex { get; internal set; }

    /// <summary>The left edge in pixels (<c>x</c>).</summary>
    public double? X { get; internal set; }

    /// <summary>The top edge in pixels (<c>y</c>).</summary>
    public double? Y { get; internal set; }

    /// <summary>The width in pixels (<c>width</c>).</summary>
    public double? Width { get; internal set; }

    /// <summary>The height in pixels (<c>height</c>).</summary>
    public double? Height { get; internal set; }

    /// <summary>Whether the element is drawn (<c>visible</c>). Default true.</summary>
    public bool? Visible { get; internal set; }

    /// <summary>Whether the element responds to the user (<c>enabled</c>). Default true.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>The tag names written in <c>tags</c>, which bindings can target instead of an index.</summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>Hover text (<c>tooltip</c>).</summary>
    public string Tooltip { get; internal set; }

    /// <summary>An identifier the reference editor writes (<c>uid</c>). Carried, never interpreted.</summary>
    public string Uid { get; internal set; }
}

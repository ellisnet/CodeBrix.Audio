using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;ui&gt;</c> element: the instrument's controls, artwork and on-screen keyboard.
/// </summary>
/// <remarks>
/// <para>
/// This library parses the interface but does not draw it. The reason it parses it in full is that
/// the initial values of the controls decide how the instrument sounds the moment it loads - a knob
/// with <c>value="0.3"</c> and an <c>AMP_VOLUME</c> binding sets that volume before a note is played -
/// and because a host needs the control list to expose the instrument's parameters.
/// </para>
/// <para>
/// <see cref="Controls"/> is the flat list a binding's <c>controlIndex</c> or <c>position</c> counts.
/// It includes every element under every tab, labels and images among them, exactly as the developer
/// guide warns.
/// </para>
/// </remarks>
public sealed class DecentSamplerUi : DecentSamplerElement
{
    private readonly List<DecentSamplerUiTab> _tabs = [];
    private readonly List<DecentSamplerUiElement> _controls = [];

    /// <summary>A cover art image for the library, relative to the preset (<c>coverArt</c>).</summary>
    public string CoverArt { get; internal set; }

    /// <summary>A background image, relative to the preset (<c>bgImage</c>).</summary>
    public string BgImage { get; internal set; }

    /// <summary>The background colour, eight hexadecimal ARGB digits (<c>bgColor</c>).</summary>
    public string BgColor { get; internal set; }

    /// <summary>The interface width in pixels (<c>width</c>). The guide recommends 812.</summary>
    public double? Width { get; internal set; }

    /// <summary>The interface height in pixels (<c>height</c>). The guide recommends 375.</summary>
    public double? Height { get; internal set; }

    /// <summary>
    /// How coordinates are interpreted (<c>layoutMode</c>), kept as text: the guide does not document
    /// its values, and presets in the wild write <c>relative</c>.
    /// </summary>
    public string LayoutMode { get; internal set; }

    /// <summary>
    /// How the background image is placed (<c>bgMode</c>), kept as text: the guide does not document
    /// its values, and presets in the wild write <c>top_left</c>.
    /// </summary>
    public string BgMode { get; internal set; }

    /// <summary>The tabs, in document order. The guide allows at most one.</summary>
    public IReadOnlyList<DecentSamplerUiTab> Tabs => _tabs;

    /// <summary>
    /// Every element under every tab, in document order. A binding's <c>controlIndex</c> or
    /// <c>position</c> is a position in this list, counting labels, images and every other
    /// non-interactive element.
    /// </summary>
    public IReadOnlyList<DecentSamplerUiElement> Controls => _controls;

    /// <summary>The on-screen keyboard settings, or null when the preset writes none.</summary>
    public DecentSamplerUiKeyboard Keyboard { get; internal set; }

    internal void Add(DecentSamplerUiTab tab)
    {
        _tabs.Add(tab);
        AddChild(tab);
    }

    internal void Register(DecentSamplerUiElement control)
    {
        control.ControlIndex = _controls.Count;
        _controls.Add(control);
    }
}

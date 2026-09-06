namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;state&gt;</c> of a button or a multi-state control, with the bindings that fire when it
/// becomes the selected state.
/// </summary>
public sealed class DecentSamplerUiState : DecentSamplerBindingHost
{
    /// <summary>The state's 0-based position, which a binding's <c>stateIndex</c> counts.</summary>
    public int Index { get; internal set; }

    /// <summary>The text shown on a text button in this state (<c>name</c>).</summary>
    public string Name { get; internal set; }

    /// <summary>The image shown in this state (<c>mainImage</c>).</summary>
    public string MainImage { get; internal set; }

    /// <summary>The image shown while hovering in this state (<c>hoverImage</c>).</summary>
    public string HoverImage { get; internal set; }

    /// <summary>The image shown while clicking in this state (<c>clickImage</c>).</summary>
    public string ClickImage { get; internal set; }
}

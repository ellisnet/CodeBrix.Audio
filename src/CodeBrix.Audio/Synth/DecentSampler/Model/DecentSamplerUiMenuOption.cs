namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;option&gt;</c> of a menu, with the bindings that fire when it is selected.
/// </summary>
public sealed class DecentSamplerUiMenuOption : DecentSamplerBindingHost
{
    /// <summary>The option's 0-based position. A menu's <c>value</c> counts these from 1.</summary>
    public int Index { get; internal set; }

    /// <summary>The text shown for this option (<c>name</c>).</summary>
    public string Name { get; internal set; }
}

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One handler beneath the <c>&lt;midi&gt;</c> element.
/// </summary>
public abstract class DecentSamplerMidiHandler : DecentSamplerBindingHost
{
    /// <summary>
    /// The handler's 0-based position among every handler in the <c>&lt;midi&gt;</c> element, which is
    /// what a binding's <c>midiElementIndex</c> counts.
    /// </summary>
    public int Index { get; internal set; }
}

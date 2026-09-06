using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A modulation source beneath <c>&lt;modulators&gt;</c>, with the attributes every kind shares.
/// </summary>
/// <remarks>
/// A modulator's changes are temporary: they last as long as the voice, or as long as the modulator
/// runs for a global one, and they never overwrite the base value a control or MIDI binding wrote.
/// </remarks>
public abstract class DecentSamplerModulator : DecentSamplerBindingHost
{
    /// <summary>The modulator's 0-based position, which bindings use as their <c>modulatorIndex</c>.</summary>
    public int Index { get; internal set; }

    /// <summary>Which of the seven modulator elements this is.</summary>
    public abstract DecentSamplerModulatorKind Kind { get; }

    /// <summary>Modulation depth, 0 to 1 (<c>modAmount</c>). Default 1.</summary>
    public double? ModAmount { get; internal set; }

    /// <summary>
    /// Whether the modulator is shared or per note (<c>scope</c>). The default is global for LFOs and
    /// voice for the envelope, velocity, MPE timbre and MPE pressure modulators.
    /// </summary>
    public DecentSamplerModulatorScope? Scope { get; internal set; }

    /// <summary>How the value combines with its target (<c>modBehavior</c>). Default set.</summary>
    public DecentSamplerModBehavior? ModBehavior { get; internal set; }

    /// <summary>
    /// The tag names written in <c>tags</c>, which a binding can target instead of a
    /// <c>modulatorIndex</c>.
    /// </summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];
}

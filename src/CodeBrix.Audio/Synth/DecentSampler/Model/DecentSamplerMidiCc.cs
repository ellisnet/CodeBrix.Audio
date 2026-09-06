namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;cc&gt;</c> handler: bindings that fire when one MIDI continuous controller changes.
/// </summary>
/// <remarks>
/// These bindings make permanent changes, the way a knob does. For per-note modulation that lasts
/// only as long as the voice, use a <c>&lt;midiCC&gt;</c> modulator instead.
/// </remarks>
public sealed class DecentSamplerMidiCc : DecentSamplerMidiHandler
{
    /// <summary>The controller number this handler listens on, 0 to 127 (<c>number</c>).</summary>
    public int? Number { get; internal set; }
}

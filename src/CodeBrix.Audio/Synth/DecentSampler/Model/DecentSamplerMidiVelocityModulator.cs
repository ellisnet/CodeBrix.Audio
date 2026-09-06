namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;midiVelocity&gt;</c> modulator: modulation driven by note-on velocity.
/// </summary>
public sealed class DecentSamplerMidiVelocityModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.MidiVelocity;
}

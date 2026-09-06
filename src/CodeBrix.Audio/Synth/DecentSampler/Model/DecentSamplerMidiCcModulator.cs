namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;midiCC&gt;</c> modulator: per-note modulation driven by one MIDI continuous controller.
/// </summary>
/// <remarks>
/// Unlike a <c>&lt;midi&gt;&lt;cc&gt;</c> binding, which permanently changes a parameter, this source
/// contributes a temporary value for as long as each voice lives. With
/// <see cref="ChannelFollowsVoice"/> every note reads its own MIDI channel, which is what makes an MPE
/// performance behave per note.
/// </remarks>
public sealed class DecentSamplerMidiCcModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.MidiCc;

    /// <summary>The controller number to follow, 0 to 127 (<c>number</c>).</summary>
    public int? Number { get; internal set; }

    /// <summary>The <c>channel</c> attribute exactly as written: <c>voice</c> or a number.</summary>
    public string ChannelText { get; internal set; }

    /// <summary>
    /// Whether each voice reads its own MIDI channel (<c>channel="voice"</c>). The default for
    /// voice-scope modulators; global-scope modulators default to channel 1.
    /// </summary>
    public bool? ChannelFollowsVoice { get; internal set; }

    /// <summary>The fixed MIDI channel to read, 1 to 16, when the channel is not the voice's own.</summary>
    public int? Channel { get; internal set; }
}

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Which of the seven modulator elements a modulator is.
/// </summary>
public enum DecentSamplerModulatorKind
{
    /// <summary>A low-frequency oscillator (<c>&lt;lfo&gt;</c>).</summary>
    Lfo,

    /// <summary>An additional ADSR envelope (<c>&lt;envelope&gt;</c>).</summary>
    Envelope,

    /// <summary>A MIDI continuous controller source (<c>&lt;midiCC&gt;</c>).</summary>
    MidiCc,

    /// <summary>A note-on velocity source (<c>&lt;midiVelocity&gt;</c>).</summary>
    MidiVelocity,

    /// <summary>An MPE timbre (CC 74) source (<c>&lt;mpeTimbre&gt;</c>).</summary>
    MpeTimbre,

    /// <summary>An MPE pressure source (<c>&lt;mpePressure&gt;</c>).</summary>
    MpePressure,

    /// <summary>A random value source (<c>&lt;random&gt;</c>).</summary>
    Random,
}

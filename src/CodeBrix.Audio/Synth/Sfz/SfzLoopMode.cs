namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The playback loop behaviour of an SFZ region, from the <c>loop_mode</c> opcode.
/// </summary>
/// <remarks>
/// When a region does not set <c>loop_mode</c>, the default depends on the sample: <see cref="NoLoop"/>
/// for samples without embedded loop points, <see cref="Continuous"/> for samples that carry them (a WAV
/// <c>smpl</c> chunk). <see cref="SfzInstrument"/> resolves that default at load time.
/// </remarks>
public enum SfzLoopMode
{
    /// <summary><c>no_loop</c> - play once from start to end; note-off releases the voice.</summary>
    NoLoop = 0,

    /// <summary><c>one_shot</c> - play to the end regardless of note-off. Envelope release is ignored.</summary>
    OneShot,

    /// <summary><c>loop_continuous</c> - loop between the loop points until the voice ends.</summary>
    Continuous,

    /// <summary><c>loop_sustain</c> - loop while the note is held, then play through to the end on release.</summary>
    Sustain
}

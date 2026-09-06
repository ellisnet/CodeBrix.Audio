namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Which MIDI note events a <c>&lt;note&gt;</c> handler listens for.
/// </summary>
public enum DecentSamplerMidiEventType
{
    /// <summary>Note-on messages only (<c>note_on</c>).</summary>
    NoteOn,

    /// <summary>Note-off messages only (<c>note_off</c>).</summary>
    NoteOff,

    /// <summary>Both (<c>any</c>). The default.</summary>
    Any,
}

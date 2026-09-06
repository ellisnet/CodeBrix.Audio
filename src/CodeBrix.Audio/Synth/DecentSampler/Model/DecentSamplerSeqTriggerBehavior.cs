namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// What a note-sequence binding does to its sequence.
/// </summary>
public enum DecentSamplerSeqTriggerBehavior
{
    /// <summary>Start playing the sequence (<c>on</c>).</summary>
    On,

    /// <summary>Stop playing the sequence (<c>off</c>).</summary>
    Off,

    /// <summary>Follow the MIDI note binding that owns it (<c>midi_key</c>). The default.</summary>
    MidiKey,
}

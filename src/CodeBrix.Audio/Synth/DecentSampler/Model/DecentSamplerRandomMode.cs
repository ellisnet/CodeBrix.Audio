namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// What makes a random modulator produce a new value.
/// </summary>
public enum DecentSamplerRandomMode
{
    /// <summary>A new value on every note-on (<c>note_on</c>).</summary>
    NoteOn,

    /// <summary>A new value at the modulator's frequency (<c>periodic</c>).</summary>
    Periodic,
}

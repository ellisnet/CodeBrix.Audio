namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// What makes a zone sound.
/// </summary>
public enum DecentSamplerTrigger
{
    /// <summary>Played when a note-on arrives (<c>attack</c>). The default.</summary>
    Attack,

    /// <summary>Played when a note-off arrives - a release trigger (<c>release</c>).</summary>
    Release,

    /// <summary>Played only when no other note is already sounding (<c>first</c>).</summary>
    First,

    /// <summary>Played only when some other note is already sounding (<c>legato</c>).</summary>
    Legato,

    /// <summary>Always sounding (<c>continuous</c>).</summary>
    Continuous,
}

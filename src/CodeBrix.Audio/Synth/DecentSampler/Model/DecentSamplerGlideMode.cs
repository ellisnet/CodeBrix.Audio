namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// When portamento (glide) is applied between notes.
/// </summary>
public enum DecentSamplerGlideMode
{
    /// <summary>Glide is always performed (<c>always</c>).</summary>
    Always,

    /// <summary>Glide is performed only when moving from one held note to another (<c>legato</c>). The default.</summary>
    Legato,

    /// <summary>No glide (<c>off</c>).</summary>
    Off,
}

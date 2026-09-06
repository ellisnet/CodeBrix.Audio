namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How quickly a voice silenced by tags or a polyphony limit stops.
/// </summary>
public enum DecentSamplerSilencingMode
{
    /// <summary>Silenced immediately (<c>fast</c>). The default.</summary>
    Fast,

    /// <summary>Silenced by triggering the zone's release phase (<c>normal</c>).</summary>
    Normal,
}

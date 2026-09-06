namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Which envelope shape an FM operator uses.
/// </summary>
public enum DecentSamplerFmEnvelopeType
{
    /// <summary>A standard ADSR envelope (<c>adsr</c>). The default.</summary>
    Adsr,

    /// <summary>A four-stage DX7-compatible rate/level envelope (<c>dx7</c>).</summary>
    Dx7,
}

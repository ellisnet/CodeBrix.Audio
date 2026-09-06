namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How an FM operator derives its frequency.
/// </summary>
public enum DecentSamplerFmOperatorMode
{
    /// <summary>A ratio of the played note (<c>ratio</c>). The default.</summary>
    Ratio,

    /// <summary>An absolute frequency in Hz (<c>fixed</c>).</summary>
    Fixed,
}

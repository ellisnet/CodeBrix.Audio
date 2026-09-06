namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The unit a delay or retrigger interval is measured in.
/// </summary>
public enum DecentSamplerTimeUnit
{
    /// <summary>Wall-clock seconds (<c>seconds</c>).</summary>
    Seconds,

    /// <summary>Musical beats, following the tempo (<c>beats</c>).</summary>
    Beats,

    /// <summary>Sample frames (<c>samples</c>).</summary>
    Samples,
}

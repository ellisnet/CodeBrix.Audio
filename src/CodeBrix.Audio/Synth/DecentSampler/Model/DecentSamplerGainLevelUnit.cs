namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The unit a gain effect's <c>level</c> is expressed in.
/// </summary>
public enum DecentSamplerGainLevelUnit
{
    /// <summary>Decibels (<c>decibels</c>). The default, for backwards compatibility.</summary>
    Decibels,

    /// <summary>A linear multiplier (<c>linear</c>).</summary>
    Linear,
}

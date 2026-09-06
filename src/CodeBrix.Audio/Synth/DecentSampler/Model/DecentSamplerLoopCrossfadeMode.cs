namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The curve used when crossfading a loop.
/// </summary>
public enum DecentSamplerLoopCrossfadeMode
{
    /// <summary>A straight-line crossfade (<c>linear</c>).</summary>
    Linear,

    /// <summary>An equal-power crossfade (<c>equal_power</c>). The default.</summary>
    EqualPower,
}

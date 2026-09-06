namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;mpeTimbre&gt;</c> modulator: modulation driven by MPE timbre, which a controller sends as
/// per-channel CC 74.
/// </summary>
public sealed class DecentSamplerMpeTimbreModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.MpeTimbre;

    /// <summary>Milliseconds taken to rise to a new value (<c>risingSmoothingTime</c>). Default 0.</summary>
    public double? RisingSmoothingTime { get; internal set; }

    /// <summary>Milliseconds taken to fall to a new value (<c>fallingSmoothingTime</c>). Default 0.</summary>
    public double? FallingSmoothingTime { get; internal set; }
}

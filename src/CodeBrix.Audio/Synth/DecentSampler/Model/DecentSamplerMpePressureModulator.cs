namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;mpePressure&gt;</c> modulator: modulation driven by MPE pressure, which a controller sends
/// as per-channel aftertouch.
/// </summary>
public sealed class DecentSamplerMpePressureModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.MpePressure;

    /// <summary>Milliseconds taken to rise to a new value (<c>risingSmoothingTime</c>). Default 0.</summary>
    public double? RisingSmoothingTime { get; internal set; }

    /// <summary>Milliseconds taken to fall to a new value (<c>fallingSmoothingTime</c>). Default 0.</summary>
    public double? FallingSmoothingTime { get; internal set; }
}

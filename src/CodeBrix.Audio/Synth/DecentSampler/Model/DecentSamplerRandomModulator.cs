namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;random&gt;</c> modulator: a source of random values between -1 and 1.
/// </summary>
/// <remarks>
/// A written <see cref="Seed"/> makes the sequence reproducible, which is what a test or an offline
/// render needs.
/// </remarks>
public sealed class DecentSamplerRandomModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.Random;

    /// <summary>What produces a new value (<c>mode</c>): on note-on, or at a fixed rate.</summary>
    public DecentSamplerRandomMode? Mode { get; internal set; }

    /// <summary>How often a periodic generator produces a value, in hertz (<c>frequency</c>).</summary>
    public double? Frequency { get; internal set; }

    /// <summary>Whether the generator resets on note-on (<c>trigger</c>).</summary>
    public DecentSamplerModulatorTrigger? Trigger { get; internal set; }

    /// <summary>The seed for the generator (<c>seed</c>), which makes its output reproducible.</summary>
    public int? Seed { get; internal set; }
}

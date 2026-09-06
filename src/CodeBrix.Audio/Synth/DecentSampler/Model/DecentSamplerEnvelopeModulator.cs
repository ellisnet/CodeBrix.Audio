namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;envelope&gt;</c> modulator: an extra ADSR, most often used to drive a group-level filter.
/// </summary>
public sealed class DecentSamplerEnvelopeModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.Envelope;

    /// <summary>Attack time in seconds (<c>attack</c>).</summary>
    public double? Attack { get; internal set; }

    /// <summary>Decay time in seconds (<c>decay</c>).</summary>
    public double? Decay { get; internal set; }

    /// <summary>Sustain level, 0 to 1 (<c>sustain</c>).</summary>
    public double? Sustain { get; internal set; }

    /// <summary>Release time in seconds (<c>release</c>).</summary>
    public double? Release { get; internal set; }

    /// <summary>Attack curve, -100 logarithmic to 100 exponential (<c>attackCurve</c>). Default -100.</summary>
    public double? AttackCurve { get; internal set; }

    /// <summary>Decay curve, -100 logarithmic to 100 exponential (<c>decayCurve</c>). Default 100.</summary>
    public double? DecayCurve { get; internal set; }

    /// <summary>Release curve, -100 logarithmic to 100 exponential (<c>releaseCurve</c>). Default 100.</summary>
    public double? ReleaseCurve { get; internal set; }

    /// <summary>Seconds of silence before the envelope starts (<c>delayTime</c>). Default 0.</summary>
    public double? DelayTime { get; internal set; }
}

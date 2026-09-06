namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;lfo&gt;</c> modulator: a cyclic source, in hertz or in musical time.
/// </summary>
public sealed class DecentSamplerLfoModulator : DecentSamplerModulator
{
    /// <inheritdoc/>
    public override DecentSamplerModulatorKind Kind => DecentSamplerModulatorKind.Lfo;

    /// <summary>The waveform (<c>shape</c>). Default sine.</summary>
    public DecentSamplerLfoShape? Shape { get; internal set; }

    /// <summary>The <c>shape</c> attribute exactly as written.</summary>
    public string ShapeName { get; internal set; }

    /// <summary>
    /// The rate (<c>frequency</c>): cycles per second when the format is hertz, otherwise a musical
    /// subdivision index.
    /// </summary>
    public double? Frequency { get; internal set; }

    /// <summary>How <see cref="Frequency"/> is read (<c>frequencyFormat</c>). Default hertz.</summary>
    public DecentSamplerFrequencyFormat? FrequencyFormat { get; internal set; }

    /// <summary>Seconds of silence before the LFO starts (<c>delayTime</c>). Default 0.</summary>
    public double? DelayTime { get; internal set; }

    /// <summary>Whether the LFO resets on note-on (<c>trigger</c>).</summary>
    public DecentSamplerModulatorTrigger? Trigger { get; internal set; }
}

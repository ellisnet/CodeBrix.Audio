namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The shape an <c>&lt;oscillator&gt;</c> generates.
/// </summary>
public enum DecentSamplerWaveform
{
    /// <summary>A pure sine wave (<c>sine</c>). The default.</summary>
    Sine,

    /// <summary>A sawtooth wave (<c>saw</c>).</summary>
    Saw,

    /// <summary>A square wave (<c>square</c>).</summary>
    Square,

    /// <summary>A triangle wave (<c>triangle</c>).</summary>
    Triangle,

    /// <summary>White noise (<c>noise</c>).</summary>
    Noise,

    /// <summary>White noise, spelled <c>white_noise</c>. Identical to <see cref="Noise"/>.</summary>
    WhiteNoise,

    /// <summary>A plucked-string waveguide (<c>pluck1</c>).</summary>
    Pluck1,

    /// <summary>A multi-frame wavetable read from a file (<c>wavetable</c>).</summary>
    Wavetable,

    /// <summary>An additive oscillator of up to 64 partials (<c>harmonic</c>).</summary>
    Harmonic,

    /// <summary>A six-operator FM oscillator (<c>fm6op</c>).</summary>
    Fm6Op,

    /// <summary>A waveform name this engine does not recognise. The raw text is kept alongside.</summary>
    Unknown,
}

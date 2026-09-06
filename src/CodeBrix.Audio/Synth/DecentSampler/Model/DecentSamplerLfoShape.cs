namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The waveform of an LFO modulator.
/// </summary>
public enum DecentSamplerLfoShape
{
    /// <summary>A sine wave (<c>sine</c>).</summary>
    Sine,

    /// <summary>A square wave (<c>square</c>).</summary>
    Square,

    /// <summary>A sawtooth wave (<c>saw</c>).</summary>
    Saw,

    /// <summary>A triangle wave (<c>triangle</c>).</summary>
    Triangle,
}

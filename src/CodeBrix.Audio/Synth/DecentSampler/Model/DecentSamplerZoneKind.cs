namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Whether a resolved zone plays a sample file or generates a waveform.
/// </summary>
public enum DecentSamplerZoneKind
{
    /// <summary>The zone plays an audio file declared by a <c>&lt;sample&gt;</c> element.</summary>
    Sample,

    /// <summary>The zone generates a waveform declared by an <c>&lt;oscillator&gt;</c> element.</summary>
    Oscillator,
}

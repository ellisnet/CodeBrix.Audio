namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How an LFO interprets its <c>frequency</c>.
/// </summary>
public enum DecentSamplerFrequencyFormat
{
    /// <summary>Cycles per second (<c>hz</c>). The default.</summary>
    Hz,

    /// <summary>A musical subdivision index, following the tempo (<c>musical_time</c>).</summary>
    MusicalTime,
}

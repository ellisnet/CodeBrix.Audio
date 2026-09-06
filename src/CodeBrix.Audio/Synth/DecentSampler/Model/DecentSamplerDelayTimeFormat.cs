namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a delay effect interprets its <c>delayTime</c>.
/// </summary>
public enum DecentSamplerDelayTimeFormat
{
    /// <summary>Seconds, independent of tempo (<c>seconds</c>). The default.</summary>
    Seconds,

    /// <summary>A musical subdivision index, following the tempo (<c>musical_time</c>).</summary>
    MusicalTime,
}

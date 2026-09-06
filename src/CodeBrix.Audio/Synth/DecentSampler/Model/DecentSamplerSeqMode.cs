namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a round-robin queue picks the next zone.
/// </summary>
public enum DecentSamplerSeqMode
{
    /// <summary>Round robins are off; every matching zone plays (<c>always</c>). The default.</summary>
    Always,

    /// <summary>A random zone is chosen, never the same one twice in a row (<c>random</c>).</summary>
    Random,

    /// <summary>A random zone is chosen with no repeat rule (<c>true_random</c>).</summary>
    TrueRandom,

    /// <summary>Zones are played in <c>seqPosition</c> order (<c>round_robin</c>).</summary>
    RoundRobin,
}

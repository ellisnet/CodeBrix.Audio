namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a note sequence repeats.
/// </summary>
public enum DecentSamplerSeqLoopMode
{
    /// <summary>Play forwards and repeat (<c>forward</c>). The default.</summary>
    Forward,

    /// <summary>Play backwards and repeat (<c>reverse</c>).</summary>
    Reverse,

    /// <summary>Pick notes at random (<c>random</c>).</summary>
    Random,

    /// <summary>Pick notes at random, never the same one twice in a row (<c>random_no_repeat</c>).</summary>
    RandomNoRepeat,

    /// <summary>Play once (<c>no_loop</c>).</summary>
    NoLoop,
}

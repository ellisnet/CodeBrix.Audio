namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The pseudo-stereo algorithm a stereo simulator uses.
/// </summary>
public enum DecentSamplerStereoSimulatorAlgorithm
{
    /// <summary>Complementary comb filters (<c>lauridsen</c>).</summary>
    Lauridsen,

    /// <summary>Double-delay comb filters (<c>schroeder</c>).</summary>
    Schroeder,

    /// <summary>Artificial double tracking with an LFO-modulated delay (<c>adt</c>). The default.</summary>
    Adt,
}

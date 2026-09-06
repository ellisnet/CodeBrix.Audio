namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Whether a modulator resets on note-on.
/// </summary>
public enum DecentSamplerModulatorTrigger
{
    /// <summary>Reset on every note-on (<c>attack</c>).</summary>
    Attack,

    /// <summary>Never reset (<c>none</c>).</summary>
    None,
}

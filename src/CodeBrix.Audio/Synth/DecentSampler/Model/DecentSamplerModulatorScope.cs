namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Whether a modulator is shared by every voice or created per note.
/// </summary>
public enum DecentSamplerModulatorScope
{
    /// <summary>One modulator shared by every voice (<c>global</c>).</summary>
    Global,

    /// <summary>A fresh modulator for each note (<c>voice</c>).</summary>
    Voice,
}

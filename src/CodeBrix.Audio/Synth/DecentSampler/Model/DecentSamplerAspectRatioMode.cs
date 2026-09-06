namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Whether an image keeps its aspect ratio.
/// </summary>
public enum DecentSamplerAspectRatioMode
{
    /// <summary>Keep the aspect ratio (<c>preserve</c>). The default.</summary>
    Preserve,

    /// <summary>Stretch to the given width and height (<c>stretch</c>).</summary>
    Stretch,
}

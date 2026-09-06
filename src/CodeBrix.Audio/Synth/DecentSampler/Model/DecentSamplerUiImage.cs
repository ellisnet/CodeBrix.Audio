namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;image&gt;</c>: a static picture, whose file and opacity a binding can change through the
/// <c>PATH</c> and <c>OPACITY</c> parameters.
/// </summary>
public sealed class DecentSamplerUiImage : DecentSamplerUiElement
{
    /// <summary>The image file, relative to the preset (<c>path</c>).</summary>
    public string Path { get; internal set; }

    /// <summary>Whether the image keeps its aspect ratio (<c>aspectRatioMode</c>). Default preserve.</summary>
    public DecentSamplerAspectRatioMode? AspectRatioMode { get; internal set; }

    /// <summary>How opaque the image is, 0 to 1 (<c>opacity</c>). Default 1.</summary>
    public double? Opacity { get; internal set; }
}

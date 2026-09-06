namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;tag&gt;</c>: the settings shared by every zone carrying that tag name.
/// </summary>
public sealed class DecentSamplerTag : DecentSamplerElement
{
    /// <summary>The tag name (<c>name</c>), which bindings address it by.</summary>
    public string Name { get; internal set; }

    /// <summary>Whether zones with this tag play (<c>enabled</c>). Default true.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>The tag's volume, 0 to 1 (<c>volume</c>). Default 1.</summary>
    public double? Volume { get; internal set; }

    /// <summary>The tag's stereo position, -100 to 100 (<c>pan</c>). Default 0.</summary>
    public double? Pan { get; internal set; }

    /// <summary>
    /// How many voices this tag allows (<c>polyphony</c>). Default -1, meaning unlimited; 1 makes the
    /// tag monophonic.
    /// </summary>
    public int? Polyphony { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() => $"<tag name=\"{Name}\"> (line {LineNumber})";
}

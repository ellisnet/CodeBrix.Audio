namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a modulator combines its value with the parameter it targets.
/// </summary>
public enum DecentSamplerModBehavior
{
    /// <summary>The value is added to the target (<c>add</c>).</summary>
    Add,

    /// <summary>A zero-centred delta is added around the base value (<c>modulate</c>).</summary>
    Modulate,

    /// <summary>The target is multiplied by the value (<c>multiply</c>).</summary>
    Multiply,

    /// <summary>The target is replaced by the value (<c>set</c>). The default.</summary>
    Set,
}

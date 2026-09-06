namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// How far this engine has gone with one feature of the Decent Sampler format.
/// </summary>
/// <remarks>
/// The distinction matters to a consumer: a <see cref="Parsed"/> feature is read from the preset and
/// available on the object model, but it does not change what you hear. A feature moves to
/// <see cref="Implemented"/> when the engine acts on it, and nothing is ever removed from the list, so
/// a name that was answerable once stays answerable.
/// </remarks>
public enum DecentSamplerFeatureStatus
{
    /// <summary>The feature is read into the object model but does not yet affect playback.</summary>
    Parsed,

    /// <summary>The feature is read and honoured by the engine.</summary>
    Implemented,
}

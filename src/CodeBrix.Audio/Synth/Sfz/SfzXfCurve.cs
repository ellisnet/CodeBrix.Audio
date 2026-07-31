namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The gain law of a key, velocity or controller crossfade, from the <c>xf_keycurve</c>,
/// <c>xf_velcurve</c> and <c>xf_cccurve</c> opcodes.
/// </summary>
public enum SfzXfCurve
{
    /// <summary>
    /// <c>power</c> (the default) - an equal-power crossfade: gain follows the square root of the fade
    /// position, keeping constant power when the crossfaded layers hold different material.
    /// </summary>
    Power = 0,

    /// <summary>
    /// <c>gain</c> - a linear amplitude crossfade, the right law for phase-aligned layers that sum
    /// coherently.
    /// </summary>
    Gain
}

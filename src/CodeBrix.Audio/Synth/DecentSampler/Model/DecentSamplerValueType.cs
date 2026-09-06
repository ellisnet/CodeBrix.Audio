namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a user-interface control interprets its value.
/// </summary>
public enum DecentSamplerValueType
{
    /// <summary>A number with two decimal places (<c>float</c>). The default.</summary>
    Float,

    /// <summary>A whole number (<c>integer</c>).</summary>
    Integer,

    /// <summary>One of the control's <c>&lt;state&gt;</c> elements (<c>multi_state</c>).</summary>
    MultiState,

    /// <summary>A musical subdivision index (<c>musical_time</c>).</summary>
    MusicalTime,

    /// <summary>A percentage (<c>percent</c>).</summary>
    Percent,
}

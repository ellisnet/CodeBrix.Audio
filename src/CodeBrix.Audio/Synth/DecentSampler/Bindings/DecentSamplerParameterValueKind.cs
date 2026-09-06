namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>What a parameter target holds, which decides how a binding's value is coerced into it.</summary>
internal enum DecentSamplerParameterValueKind
{
    /// <summary>A number: a volume, a time, a frequency, a note number, a frame position.</summary>
    Number,

    /// <summary>A true/false switch, such as a group's <c>ENABLED</c>.</summary>
    Boolean,

    /// <summary>Text: a file path, a colour, a label's words, or an enumeration member's name.</summary>
    Text,

    /// <summary>An action rather than a value, such as <c>ALL_NOTES_OFF</c>.</summary>
    Action,
}

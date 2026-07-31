namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The header kinds an SFZ file can contain. A header opens a section, and the opcodes that follow it
/// belong to that section until the next header.
/// </summary>
/// <remarks>
/// Headers form a hierarchy for the purpose of resolving opcodes: a <see cref="Region"/> inherits from
/// its enclosing <see cref="Group"/>, which inherits from <see cref="Global"/>, which inherits from
/// <see cref="Master"/>. The nearest definition wins.
/// </remarks>
public enum SfzHeaderKind
{
    /// <summary>An unrecognised header. Its opcodes are parsed but belong to no known scope.</summary>
    Unknown = 0,

    /// <summary>
    /// <c>&lt;control&gt;</c> - file-level settings such as <c>default_path</c> and <c>#define</c>
    /// variables. Applies to everything that follows it in the file.
    /// </summary>
    Control,

    /// <summary><c>&lt;global&gt;</c> - opcodes inherited by every group and region in the file.</summary>
    Global,

    /// <summary><c>&lt;master&gt;</c> - an ARIA scope sitting between global and group.</summary>
    Master,

    /// <summary><c>&lt;group&gt;</c> - opcodes inherited by every region that follows it.</summary>
    Group,

    /// <summary><c>&lt;region&gt;</c> - one playable zone. The only header that produces sound.</summary>
    Region,

    /// <summary><c>&lt;curve&gt;</c> - a named modulation curve referenced by <c>curve_index</c>.</summary>
    Curve,

    /// <summary><c>&lt;effect&gt;</c> - an effect bus definition.</summary>
    Effect,

    /// <summary><c>&lt;sample&gt;</c> - embedded sample data (rare; ARIA extension).</summary>
    Sample,

    /// <summary><c>&lt;midi&gt;</c> - MIDI-triggered behaviour (rare; ARIA extension).</summary>
    Midi
}

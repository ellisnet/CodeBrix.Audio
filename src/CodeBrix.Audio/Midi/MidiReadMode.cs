namespace CodeBrix.Audio.Midi;

/// <summary>
/// How a MIDI reader reacts to content that does not follow the Standard MIDI File specification.
/// </summary>
/// <remarks>
/// <para>
/// Real-world MIDI files are frequently out of spec in small ways, and some generators - notably
/// the stem exports produced by online music services - write values the specification does not
/// allow at all. <see cref="Tolerant"/> is the default everywhere in this library so that such a
/// file loads and plays; <see cref="Strict"/> is there for tools that need to validate a file
/// rather than use it.
/// </para>
/// <para>
/// Tolerance never invents musical content. Everything that could not be honoured verbatim is
/// listed, one human-readable line at a time, in the reader's <c>Problems</c> collection.
/// </para>
/// </remarks>
public enum MidiReadMode
{
    /// <summary>
    /// Read whatever the file contains: keep out-of-range values as raw bytes, skip meta events
    /// that cannot be decoded, close notes that were never released, and record each of those in
    /// the reader's <c>Problems</c> collection. Reading only fails when nothing sensible can be
    /// recovered at all.
    /// </summary>
    Tolerant = 0,

    /// <summary>
    /// Reject anything the Standard MIDI File specification does not allow, by throwing.
    /// </summary>
    Strict = 1,
}

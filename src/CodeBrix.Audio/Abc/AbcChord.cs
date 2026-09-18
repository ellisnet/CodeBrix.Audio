using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// A chord: notes written inside <c>[ ]</c> and sounded together.
/// </summary>
/// <remarks>
/// <para>
/// The standard's rule is that all the notes in a chord normally share a length, and where they do
/// not, the chord lasts as long as its FIRST note. A length written after the closing bracket
/// multiplies the length inside it, so <c>[C2E2G2]3</c> means the same as <c>[CEG]6</c>.
/// <see cref="Length"/> is the result of both rules; each <see cref="AbcNote"/> in
/// <see cref="Notes"/> keeps the length it was written with.
/// </para>
/// <para>
/// A tie written after the closing bracket ties every note in the chord to the matching note of the
/// next chord or note.
/// </para>
/// </remarks>
public sealed class AbcChord : AbcElement
{
    private readonly List<AbcNote> _notes;

    /// <summary>
    /// Creates a chord.
    /// </summary>
    /// <param name="notes">The notes sounded together, in the order written.</param>
    /// <param name="length">How long the chord sounds, as a fraction of a whole note.</param>
    /// <param name="tiedToNext">Whether a tie was written after the chord.</param>
    public AbcChord(IEnumerable<AbcNote> notes, AbcDuration length, bool tiedToNext)
    {
        _notes = notes == null ? new List<AbcNote>() : new List<AbcNote>(notes);
        Length = length;
        TiedToNext = tiedToNext;
    }

    /// <summary>The notes sounded together, in the order written.</summary>
    public IReadOnlyList<AbcNote> Notes => _notes;

    /// <summary>How long the chord sounds, as a fraction of a whole note.</summary>
    public AbcDuration Length { get; }

    /// <summary>Whether a tie, <c>-</c>, was written after the chord.</summary>
    public bool TiedToNext { get; }

    /// <summary>Describes the chord.</summary>
    /// <returns>For example <c>"[3 notes] 1/4"</c>.</returns>
    public override string ToString() => $"[{_notes.Count} notes] {Length}";
}

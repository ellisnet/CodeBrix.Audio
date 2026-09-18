using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// A tuplet: <c>p</c> notes played in the time of <c>q</c>, covering the next <c>r</c> notes.
/// </summary>
/// <remarks>
/// <para>
/// Abc writes the simple form <c>(3</c> and the general form <c>(p:q:r</c>. When <c>q</c> is left
/// out the standard supplies it from <c>p</c>: 3 for a duplet, 2 for a triplet, 3 for a quadruplet,
/// 2 for a sextuplet, 3 for an octuplet, and for 5, 7 and 9 it is three in a compound meter (6/8,
/// 9/8, 12/8) and two otherwise. When <c>r</c> is left out it is <c>p</c>.
/// </para>
/// <para>
/// The notes inside keep the length they were written with; the ratio is applied when the tune is
/// converted, so every written length in the model reads back as it was typed.
/// </para>
/// </remarks>
public sealed class AbcTupletGroup : AbcElement
{
    private readonly List<AbcElement> _elements;

    /// <summary>
    /// Creates a tuplet group.
    /// </summary>
    /// <param name="notesInTuplet">The <c>p</c> of <c>(p:q:r</c> - how many notes are written.</param>
    /// <param name="inTheTimeOf">The <c>q</c> of <c>(p:q:r</c> - how many they are played in the time of.</param>
    /// <param name="elementCount">The <c>r</c> of <c>(p:q:r</c> - how many elements the tuplet covers.</param>
    /// <param name="elements">The notes, rests and chords inside the tuplet, in the order written.</param>
    public AbcTupletGroup(int notesInTuplet, int inTheTimeOf, int elementCount, IEnumerable<AbcElement> elements)
    {
        NotesInTuplet = notesInTuplet;
        InTheTimeOf = inTheTimeOf;
        ElementCount = elementCount;
        _elements = elements == null ? new List<AbcElement>() : new List<AbcElement>(elements);
    }

    /// <summary>The <c>p</c> of <c>(p:q:r</c>: how many notes are written.</summary>
    public int NotesInTuplet { get; }

    /// <summary>The <c>q</c> of <c>(p:q:r</c>: how many notes they are played in the time of.</summary>
    public int InTheTimeOf { get; }

    /// <summary>The <c>r</c> of <c>(p:q:r</c>: how many elements the tuplet covers.</summary>
    public int ElementCount { get; }

    /// <summary>The notes, rests and chords inside the tuplet, in the order written.</summary>
    public IReadOnlyList<AbcElement> Elements => _elements;

    /// <summary>
    /// The factor every length inside is multiplied by: <c>q / p</c>.
    /// </summary>
    public AbcDuration Ratio => new AbcDuration(InTheTimeOf, NotesInTuplet);

    /// <summary>Describes the tuplet.</summary>
    /// <returns>For example <c>"(3:2:3"</c>.</returns>
    public override string ToString() => $"({NotesInTuplet}:{InTheTimeOf}:{ElementCount}";
}

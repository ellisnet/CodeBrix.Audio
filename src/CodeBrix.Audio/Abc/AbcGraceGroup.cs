using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// A group of grace notes, written inside <c>{ }</c>.
/// </summary>
/// <remarks>
/// <para>
/// Abc deliberately leaves the duration of a grace note to the software rather than to the file, so
/// the lengths written inside the braces are recorded on the notes but the time they actually take
/// comes from <see cref="AbcToMidiOptions.GraceNoteLength"/>.
/// </para>
/// <para>
/// A leading slash - <c>{/g}</c> - marks an acciaccatura rather than an appoggiatura. Both are
/// played the same way here; the distinction is recorded for a caller that wants it.
/// </para>
/// </remarks>
public sealed class AbcGraceGroup : AbcElement
{
    private readonly List<AbcNote> _notes;

    /// <summary>
    /// Creates a grace group.
    /// </summary>
    /// <param name="notes">The grace notes, in the order written.</param>
    /// <param name="isAcciaccatura">Whether the group opened with <c>{/</c>.</param>
    public AbcGraceGroup(IEnumerable<AbcNote> notes, bool isAcciaccatura)
    {
        _notes = notes == null ? new List<AbcNote>() : new List<AbcNote>(notes);
        IsAcciaccatura = isAcciaccatura;
    }

    /// <summary>The grace notes, in the order written.</summary>
    public IReadOnlyList<AbcNote> Notes => _notes;

    /// <summary>Whether the group opened with <c>{/</c>, marking an acciaccatura.</summary>
    public bool IsAcciaccatura { get; }

    /// <summary>Describes the group.</summary>
    /// <returns>For example <c>"{2 grace notes}"</c>.</returns>
    public override string ToString() => $"{{{_notes.Count} grace notes}}";
}

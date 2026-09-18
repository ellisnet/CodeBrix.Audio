using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// The tunes found in one piece of abc text or one <c>.abc</c> file.
/// </summary>
/// <remarks>
/// <para>
/// An abc file holds one tune or many. Each tune starts at an <c>X:</c> field and ends at a blank
/// line or at the end of the file; anything before the first <c>X:</c> is free text and is ignored.
/// A file holding more than one tune is what the standard calls a TUNEBOOK, which is where the name
/// comes from.
/// </para>
/// <para>
/// <see cref="Problems"/> holds what went wrong with the FILE - no tune found, a file header that
/// was not applied. What went wrong inside a tune is on that tune's own
/// <see cref="AbcTune.Problems"/>. Neither is ever thrown.
/// </para>
/// </remarks>
public sealed class AbcTuneBook
{
    private readonly List<AbcTune> _tunes;
    private readonly List<string> _problems;

    /// <summary>
    /// Creates a tune book.
    /// </summary>
    /// <param name="tunes">The tunes found, in the order they appeared.</param>
    /// <param name="problems">What went wrong with the file as a whole.</param>
    public AbcTuneBook(IEnumerable<AbcTune> tunes, IEnumerable<string> problems)
    {
        _tunes = tunes == null ? new List<AbcTune>() : new List<AbcTune>(tunes);
        _problems = problems == null ? new List<string>() : new List<string>(problems);
    }

    /// <summary>The tunes found, in the order they appeared.</summary>
    public IReadOnlyList<AbcTune> Tunes => _tunes;

    /// <summary>
    /// What went wrong with the file as a whole, one human-readable line each. Problems inside a
    /// tune are on that tune instead. Never thrown.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Describes the tune book.</summary>
    /// <returns>For example <c>"2 tune(s)"</c>.</returns>
    public override string ToString() => $"{_tunes.Count} tune(s)";
}

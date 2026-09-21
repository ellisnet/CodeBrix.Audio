using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// One tune read from abc notation: its header fields, its voices, and what could not be honoured.
/// </summary>
/// <remarks>
/// <para>
/// The tune is a faithful reading of the text. Lengths are the lengths that were written, bars are
/// the bars that were written, and REPEATS ARE NOT UNROLLED - the <c>|:</c> and <c>:|</c> marks and
/// the numbered endings are still there, on the bars. <see cref="AbcToMidi"/> unrolls them, because
/// MIDI has no repeats and playback has to choose.
/// </para>
/// <para>
/// <see cref="Problems"/> follows the same contract as the MIDI reader's: one human-readable line
/// per departure, in the order they were found, empty when the tune held nothing this reader could
/// not honour, capped so a broken file cannot become a memory problem, and never thrown. Each KIND
/// of skipped construct is listed ONCE, not once per occurrence, so an ordinary tune full of
/// decorations and chord symbols produces two lines rather than two hundred.
/// </para>
/// </remarks>
public sealed class AbcTune
{
    private readonly List<string> _titles;
    private readonly List<AbcVoice> _voices;
    private readonly List<string> _problems;

    /// <summary>
    /// Creates a tune.
    /// </summary>
    /// <param name="referenceNumber">The <c>X:</c> reference number.</param>
    /// <param name="titles">The <c>T:</c> titles, in the order written.</param>
    /// <param name="composer">The <c>C:</c> composer, or an empty string.</param>
    /// <param name="meter">The meter in force at the start of the body.</param>
    /// <param name="unitNoteLength">The unit note length in force at the start of the body.</param>
    /// <param name="tempo">The header tempo, or <see langword="null"/> when the header carried no <c>Q:</c>.</param>
    /// <param name="key">The key in force at the start of the body.</param>
    /// <param name="voices">The voices, in the order their ids first appeared.</param>
    /// <param name="problems">What could not be honoured.</param>
    public AbcTune(
        int referenceNumber,
        IEnumerable<string> titles,
        string composer,
        AbcMeter meter,
        AbcUnitNoteLength unitNoteLength,
        AbcTempo tempo,
        AbcKey key,
        IEnumerable<AbcVoice> voices,
        IEnumerable<string> problems)
    {
        ReferenceNumber = referenceNumber;
        _titles = titles == null ? new List<string>() : new List<string>(titles);
        Composer = composer ?? string.Empty;
        Meter = meter ?? AbcMeter.Free;
        UnitNoteLength = unitNoteLength ?? new AbcUnitNoteLength(new AbcDuration(1, 8), true);
        Tempo = tempo;
        Key = key ?? AbcKey.CMajor;
        _voices = voices == null ? new List<AbcVoice>() : new List<AbcVoice>(voices);
        _problems = problems == null ? new List<string>() : new List<string>(problems);
    }

    /// <summary>The <c>X:</c> reference number. Zero when the field could not be read as a number.</summary>
    public int ReferenceNumber { get; }

    /// <summary>
    /// The <c>T:</c> titles, in the order written. The field repeats, and the first is the tune's
    /// title.
    /// </summary>
    public IReadOnlyList<string> Titles => _titles;

    /// <summary>The first title, or an empty string when the tune carried none.</summary>
    public string Title => _titles.Count > 0 ? _titles[0] : string.Empty;

    /// <summary>The <c>C:</c> composer, or an empty string.</summary>
    public string Composer { get; }

    /// <summary>The meter in force at the start of the body. Free meter when there was no <c>M:</c>.</summary>
    public AbcMeter Meter { get; }

    /// <summary>
    /// The unit note length in force at the start of the body, from the <c>L:</c> field or, when
    /// there was none, from the standard's default rule.
    /// </summary>
    public AbcUnitNoteLength UnitNoteLength { get; }

    /// <summary>
    /// The tempo from the tune header, or <see langword="null"/> when the header carried no
    /// <c>Q:</c> field. A field that carried only a label is a tempo with no value. Changes in the
    /// body are <see cref="AbcInlineField"/> elements at the positions where they occur.
    /// </summary>
    public AbcTempo Tempo { get; }

    /// <summary>The key in force at the start of the body.</summary>
    public AbcKey Key { get; }

    /// <summary>The voices, in the order their ids first appeared. Never empty for a tune with a body.</summary>
    public IReadOnlyList<AbcVoice> Voices => _voices;

    /// <summary>
    /// What this tune held that could not be honoured, one human-readable line each, in the order
    /// found. Empty when there was nothing. Never thrown.
    /// </summary>
    /// <remarks>
    /// The list grows in two places. Reading the text fills it, and
    /// <see cref="AbcToMidi.Convert(AbcTune)"/> ADDS to it what only the conversion can know - a
    /// tuplet that does not land on whole ticks at the chosen resolution, a tie that joined two
    /// different pitches, a note outside the MIDI range. A line is added only once however many
    /// times a tune is converted.
    /// </remarks>
    public IReadOnlyList<string> Problems => _problems;

    // Adds a conversion problem, unless the same line is already there. Converting one tune twice
    // must not double its problem list.
    internal void AddProblem(string problem)
    {
        if (problem == null || _problems.Count >= MaximumProblems)
        {
            return;
        }

        for (int i = 0; i < _problems.Count; i++)
        {
            if (string.Equals(_problems[i], problem, System.StringComparison.Ordinal))
            {
                return;
            }
        }

        _problems.Add(problem);
    }

    // The same cap the MIDI reader uses, for the same reason.
    private const int MaximumProblems = 200;

    /// <summary>Describes the tune.</summary>
    /// <returns>For example <c>"X:1 The Butterfly"</c>.</returns>
    public override string ToString() => $"X:{ReferenceNumber} {Title}";
}

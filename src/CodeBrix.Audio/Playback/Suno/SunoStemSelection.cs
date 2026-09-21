using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// Which stems of a song play from their TRANSCRIPTION rather than from their recording, said once
/// instead of written as a loop over every track.
/// </summary>
/// <remarks>
/// <para>
/// Every consumer who swaps instruments into a stems export writes the same loop: walk the tracks,
/// skip the vocals, skip the stems whose <c>.mid</c> holds barely any notes, and set
/// <see cref="PlayerTrack.ActiveSource"/> on the rest. The rule belongs to the consumer - which
/// parts are vocals, and how empty is too empty, are decisions about the music - but the LOOP does
/// not, and this is the loop.
/// </para>
/// <para>
/// USABLE IS A FLOOR ON TWO NUMBERS, both of which a stem carries. A <c>.mid</c> beside a stem says
/// nothing about whether it is worth playing: a real export holds a three-note transcription of a
/// four-minute backing vocal, and playing that in place of the recording makes the part vanish.
/// <see cref="MinimumNoteCount"/> and <see cref="MinimumMidiCoverage"/> are both applied, and BOTH
/// matter: a two-note pad passes an ordinary coverage floor because its two notes are long, and a
/// dense part of very short notes passes a note count while covering almost nothing.
/// </para>
/// <code>
/// using var player = song.CreatePlayer(new SunoPlayerOptions
/// {
///     InstrumentLibraryName = "ModestSynthGm",
///     MidiStems = SunoStemSelection.EverythingBut("Vocals", "Backing Vocals"),
/// });
/// </code>
/// <para>
/// A selection is a plain object with settable floors, so the defaults are a starting point rather
/// than a ceiling:
/// </para>
/// <code>
/// var selection = SunoStemSelection.EverythingBut("Vocals");
/// selection.MinimumNoteCount = 40;          // this arrangement wants real parts only
/// </code>
/// <para>
/// A name that the song has no stem for is REPORTED - in the player's
/// <see cref="MultiTrackPlayer.Problems"/> - and never thrown, because which parts an export
/// carries is a property of the download rather than of the calling code.
/// </para>
/// </remarks>
public sealed class SunoStemSelection
{
    /// <summary>
    /// The note count a transcription must reach to count as usable, when nothing else is said.
    /// </summary>
    /// <remarks>
    /// Twelve notes. Below it, a transcription in a real export is a handful of accents rather than
    /// a part, and playing it instead of the recording removes that part from the mix.
    /// </remarks>
    public const int DefaultMinimumNoteCount = 12;

    /// <summary>
    /// The <see cref="SunoStem.MidiCoverage"/> a transcription must reach to count as usable, when
    /// nothing else is said.
    /// </summary>
    /// <remarks>
    /// Two per cent of the song. A complete part sits between 0.1 and 0.9; a few stray notes
    /// measure a thousandth.
    /// </remarks>
    public const double DefaultMinimumMidiCoverage = 0.02;

    private readonly string[] names;
    private readonly bool namesAreExcluded;

    private SunoStemSelection(string[] names, bool namesAreExcluded)
    {
        this.names = names;
        this.namesAreExcluded = namesAreExcluded;
    }

    /// <summary>
    /// Every stem whose transcription is usable plays from MIDI - the vocals included.
    /// </summary>
    /// <returns>A selection naming nothing, excluding nothing.</returns>
    public static SunoStemSelection Everything() => new SunoStemSelection([], namesAreExcluded: true);

    /// <summary>
    /// No stem plays from MIDI: every track stays on its recording, which is what a player does
    /// when no selection is given at all.
    /// </summary>
    /// <returns>A selection that includes nothing.</returns>
    public static SunoStemSelection Nothing() => new SunoStemSelection([], namesAreExcluded: false);

    /// <summary>
    /// Every stem whose transcription is usable plays from MIDI EXCEPT the ones named - the usual
    /// shape, because the vocals are the parts worth keeping as the recording.
    /// </summary>
    /// <param name="stemNames">
    /// The stems to keep on their recordings, matched the way <c>song["Backing Vocals"]</c> matches:
    /// case-insensitively, with surrounding space ignored.
    /// </param>
    /// <returns>A selection excluding those names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stemNames"/> is null.</exception>
    public static SunoStemSelection EverythingBut(params string[] stemNames) =>
        new SunoStemSelection(Clean(stemNames, nameof(stemNames)), namesAreExcluded: true);

    /// <summary>
    /// ONLY the stems named play from MIDI, and only where their transcription is usable.
    /// </summary>
    /// <param name="stemNames">The stems to play from MIDI, matched case-insensitively.</param>
    /// <returns>A selection holding those names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stemNames"/> is null.</exception>
    public static SunoStemSelection Only(params string[] stemNames) =>
        new SunoStemSelection(Clean(stemNames, nameof(stemNames)), namesAreExcluded: false);

    /// <summary>
    /// The fewest note-ons a transcription may hold and still be played. Defaults to
    /// <see cref="DefaultMinimumNoteCount"/>; zero accepts any transcription that has notes at all.
    /// </summary>
    public int MinimumNoteCount { get; set; } = DefaultMinimumNoteCount;

    /// <summary>
    /// The least <see cref="SunoStem.MidiCoverage"/> a transcription may have and still be played.
    /// Defaults to <see cref="DefaultMinimumMidiCoverage"/>.
    /// </summary>
    public double MinimumMidiCoverage { get; set; } = DefaultMinimumMidiCoverage;

    /// <summary>
    /// The stem names this selection was built with, in the order they were given.
    /// </summary>
    public IReadOnlyList<string> NamedStems => names;

    /// <summary>
    /// Whether <see cref="NamedStems"/> names the stems to EXCLUDE (<see cref="EverythingBut"/>) or
    /// the only ones to include (<see cref="Only"/>).
    /// </summary>
    public bool NamesAreExcluded => namesAreExcluded;

    /// <summary>
    /// Whether this stem should play from its transcription: it is on the right side of the names,
    /// it has MIDI, and that MIDI clears both floors.
    /// </summary>
    /// <param name="stem">The stem to decide about.</param>
    /// <returns><see langword="true"/> when the stem should play from MIDI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stem"/> is null.</exception>
    /// <remarks>
    /// Whether an INSTRUMENT could be built for the stem is a separate question, answered by the
    /// track: a selection that includes a stem no instrument can play leaves that track on its
    /// recording, because there is nothing else for it to be.
    /// </remarks>
    public bool Includes(SunoStem stem)
    {
        if (stem == null)
        {
            throw new ArgumentNullException(nameof(stem));
        }

        // Named AND excluded, or unnamed AND only-the-named: either way this stem is not in the
        // selection at all, and the floors never come into it.
        if (Names(stem.Name) == namesAreExcluded)
        {
            return false;
        }

        return stem.Midi != null
            && stem.NoteCount >= MinimumNoteCount
            && stem.MidiCoverage >= MinimumMidiCoverage;
    }

    /// <summary>Whether a stem name is one of the names this selection was built with.</summary>
    /// <param name="stemName">The name to look for, matched case-insensitively.</param>
    /// <returns><see langword="true"/> when the name is in <see cref="NamedStems"/>.</returns>
    public bool Names(string stemName)
    {
        if (stemName == null)
        {
            return false;
        }

        var wanted = stemName.Trim();
        foreach (var name in names)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>What it selects and the floors it applies.</returns>
    public override string ToString()
    {
        var what = names.Length == 0
            ? namesAreExcluded ? "every stem" : "no stem"
            : $"{(namesAreExcluded ? "every stem but" : "only")} {string.Join(", ", names)}";

        return $"{what} (at least {MinimumNoteCount} note(s), coverage {MinimumMidiCoverage:0.###})";
    }

    // An independent copy, so that the options a player was built from cannot be changed under it.
    internal SunoStemSelection Clone() =>
        new SunoStemSelection(names.ToArray(), namesAreExcluded)
        {
            MinimumNoteCount = MinimumNoteCount,
            MinimumMidiCoverage = MinimumMidiCoverage,
        };

    private static string[] Clean(string[] stemNames, string parameterName)
    {
        if (stemNames == null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var cleaned = new List<string>(stemNames.Length);
        foreach (var name in stemNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                cleaned.Add(name.Trim());
            }
        }

        return cleaned.ToArray();
    }
}

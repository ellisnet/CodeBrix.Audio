using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// What an <see cref="IInstrumentLibrary"/> can really play: which General MIDI programs it has a
/// sound for, over which notes, and which percussion notes its kit holds.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that nothing has to find out by listening. A voicing can skip a program the
/// library does not have instead of asking for it and getting a fallback, and a diagnostic can say
/// why a part came out silent - which is otherwise indistinguishable from a bug in the music.
/// </para>
/// <para>
/// A library built from oscillators covers everything and reports <see cref="General"/>. A library
/// built from recordings covers what was recorded, which is usually neither all 128 programs nor
/// the whole keyboard of the ones it has.
/// </para>
/// </remarks>
public sealed class InstrumentCoverage
{
    private readonly Dictionary<int, InstrumentKeyRange> programRanges;
    private readonly HashSet<int> percussionNotes;
    private readonly int[] programs;
    private readonly int[] orderedPercussionNotes;

    /// <summary>
    /// Creates a coverage report over whole programs, each covering the whole keyboard.
    /// </summary>
    /// <param name="programs">The General MIDI program numbers covered, 0 to 127.</param>
    /// <param name="percussionNotes">The percussion note numbers covered, 0 to 127.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A program or note number is outside 0 to 127.</exception>
    public InstrumentCoverage(IEnumerable<int> programs, IEnumerable<int> percussionNotes)
        : this(BuildFullRanges(programs), percussionNotes)
    {
    }

    /// <summary>
    /// Creates a coverage report where each covered program names the notes it answers to.
    /// </summary>
    /// <param name="programs">
    /// The covered General MIDI program numbers, each with the note range it answers to. A program
    /// whose range is empty counts as not covered.
    /// </param>
    /// <param name="percussionNotes">The percussion note numbers covered, 0 to 127.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A program or note number is outside 0 to 127.</exception>
    public InstrumentCoverage(
        IEnumerable<KeyValuePair<int, InstrumentKeyRange>> programs,
        IEnumerable<int> percussionNotes)
    {
        if (programs == null)
        {
            throw new ArgumentNullException(nameof(programs));
        }

        if (percussionNotes == null)
        {
            throw new ArgumentNullException(nameof(percussionNotes));
        }

        programRanges = new Dictionary<int, InstrumentKeyRange>();

        foreach (var entry in programs)
        {
            if (entry.Key < 0 || entry.Key >= GeneralMidi.ProgramCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(programs), entry.Key, "A General MIDI program number is 0 to 127.");
            }

            if (entry.Value.IsEmpty)
            {
                continue;
            }

            programRanges[entry.Key] = programRanges.TryGetValue(entry.Key, out var existing)
                ? existing.UnionWith(entry.Value)
                : entry.Value;
        }

        this.percussionNotes = new HashSet<int>();

        foreach (var note in percussionNotes)
        {
            if (note < 0 || note > 127)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(percussionNotes), note, "A MIDI note number is 0 to 127.");
            }

            this.percussionNotes.Add(note);
        }

        this.programs = programRanges.Keys.OrderBy(program => program).ToArray();
        orderedPercussionNotes = this.percussionNotes.OrderBy(note => note).ToArray();
    }

    /// <summary>
    /// Every General MIDI program over the whole keyboard, and the whole General MIDI percussion
    /// key map - what a library built from synthesis reports.
    /// </summary>
    public static InstrumentCoverage General { get; } = new InstrumentCoverage(
        Enumerable.Range(0, GeneralMidi.ProgramCount),
        Enumerable.Range(
            GeneralMidi.LowestPercussionNote,
            GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1));

    /// <summary>A library that can play nothing at all.</summary>
    public static InstrumentCoverage None { get; } =
        new InstrumentCoverage(Array.Empty<int>(), Array.Empty<int>());

    /// <summary>The covered General MIDI program numbers, in ascending order.</summary>
    public IReadOnlyList<int> Programs => programs;

    /// <summary>The covered percussion note numbers, in ascending order.</summary>
    public IReadOnlyList<int> PercussionNotes => orderedPercussionNotes;

    /// <summary>Whether the library has a sound for a General MIDI program.</summary>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <returns>True when the program is covered.</returns>
    public bool CoversProgram(int program) => programRanges.ContainsKey(program);

    /// <summary>Whether the library has a sound for a percussion note.</summary>
    /// <param name="noteNumber">The MIDI note number on the percussion channel.</param>
    /// <returns>True when the kit holds that note.</returns>
    public bool CoversPercussionNote(int noteNumber) => percussionNotes.Contains(noteNumber);

    /// <summary>The notes a covered program answers to.</summary>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <returns>
    /// The program's note range, or <see cref="InstrumentKeyRange.Empty"/> when the program is not
    /// covered at all.
    /// </returns>
    public InstrumentKeyRange KeyRangeOf(int program) =>
        programRanges.TryGetValue(program, out var range) ? range : InstrumentKeyRange.Empty;

    /// <summary>Whether a program will sound at a given note.</summary>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <param name="noteNumber">The MIDI note number to play.</param>
    /// <returns>True when the program is covered and answers to that note.</returns>
    public bool CoversNote(int program, int noteNumber) =>
        programRanges.TryGetValue(program, out var range) && range.Contains(noteNumber);

    /// <summary>How many programs and percussion notes are covered.</summary>
    /// <returns>A short description, for a diagnostic message.</returns>
    public override string ToString() =>
        $"{programs.Length} program(s), {orderedPercussionNotes.Length} percussion note(s)";

    private static IEnumerable<KeyValuePair<int, InstrumentKeyRange>> BuildFullRanges(IEnumerable<int> programs)
    {
        if (programs == null)
        {
            throw new ArgumentNullException(nameof(programs));
        }

        return programs.Select(program =>
            new KeyValuePair<int, InstrumentKeyRange>(program, InstrumentKeyRange.Full));
    }
}

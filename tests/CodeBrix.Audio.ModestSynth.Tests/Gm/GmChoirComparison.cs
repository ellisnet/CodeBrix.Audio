using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The four pieces the choir voicing is judged on, plus the same chords on Voice Oohs - written so
/// that one voicing's renders can be put beside another's under the same names.
/// </summary>
/// <remarks>
/// <para>
/// A family tour proves a program makes a sound. Whether it sounds like PEOPLE SINGING is a
/// different question, and it is answered by the textures a choir part really plays: a slow
/// progression in close harmony, one exposed line, the whole range from a bass's bottom note to a
/// soprano's top one, and a real piece of music with the choir under a melody.
/// </para>
/// <para>
/// Every piece is written at <see cref="GmAudition.TicksPerQuarterNote" /> and 120 beats per
/// minute, so a tick is a 960th of a second, and every plan's file name ends in the LABEL it was
/// rendered under - so a before and an after sit side by side in one folder and differ only in the
/// last word of the name.
/// </para>
/// </remarks>
public static class GmChoirComparison
{
    /// <summary>The folder under <c>TestResults/GmRenders</c> the comparison is written to.</summary>
    public const string FolderName = "choir-ab";

    /// <summary>The program the comparison is about.</summary>
    public const int ChoirProgram = (int)GeneralMidiProgram.ChoirAahs;

    /// <summary>The second voice program that shares the choir's voicing.</summary>
    public const int VoiceOohsProgram = (int)GeneralMidiProgram.VoiceOohs;

    /// <summary>The channel every piece here plays the choir on.</summary>
    public const int ChoirChannel = GmAudition.MelodicChannel;

    // A choir sings; it does not hammer. Everything here is written under a mezzo-forte velocity so
    // the comparison is about the voice rather than about how hard the key went down.
    private const int ChordVelocity = 86;
    private const int LineVelocity = 82;
    private const int RangeVelocity = 90;

    /// <summary>
    /// The pieces, each named for the voicing label they are being rendered under.
    /// </summary>
    /// <param name="label">The label, for instance <c>BEFORE</c> or <c>AFTER</c>.</param>
    /// <returns>The plans, in the order a listening session takes them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="label" /> is null.</exception>
    public static IReadOnlyList<GmAuditionPlan> Plans(string label)
    {
        if (label == null) { throw new ArgumentNullException(nameof(label)); }

        return
        [
            SustainedChords(label),
            SoloLine(label),
            Range(label),
            Duet(label),
            OohsChords(label),
        ];
    }

    /// <summary>
    /// A slow four-chord progression in close harmony, mid range - the texture a pad-like choir
    /// part really plays.
    /// </summary>
    /// <param name="label">The voicing label the file name ends in.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan SustainedChords(string label) =>
        Chords(ChoirProgram, "1-sustained-chords", "Choir Aahs - four chords in close harmony", label);

    /// <summary>The same chords on Voice Oohs, which shares the choir's voicing.</summary>
    /// <param name="label">The voicing label the file name ends in.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan OohsChords(string label) =>
        Chords(VoiceOohsProgram, "5-oohs-chords", "Voice Oohs - the same four chords", label);

    /// <summary>A slow single line, one "singer" exposed with nothing to hide behind.</summary>
    /// <param name="label">The voicing label the file name ends in.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan SoloLine(string label)
    {
        // A minor, the key of the duet, moving by step so the line is about the voice rather than
        // about the intervals.
        int[] keys = [69, 72, 71, 69, 67, 64, 65, 67, 69];
        long[] lengths = [1920, 1920, 1920, 1920, 1920, 1920, 1920, 2880, 3840];

        MidiEventCollection events = GmAudition.NewCollection();
        List<GmAuditionCue> cues = [];

        events.AddEvent(new PatchChangeEvent(0, ChoirChannel, ChoirProgram), ChoirChannel);

        long tick = 0;

        for (int i = 0; i < keys.Length; i++)
        {
            AddNote(events, tick, lengths[i], keys[i], LineVelocity);
            tick += lengths[i];
        }

        cues.Add(new GmAuditionCue(TimeSpan.Zero, ChoirProgram, "Choir Aahs - one line, exposed"));

        return GmAudition.Finish(
            "Choir Aahs - a slow single line", Name("2-solo-line", label), events, cues);
    }

    /// <summary>
    /// Sustained notes from the bottom of a bass's range to the top of a soprano's, so the vowel
    /// can be heard behaving - or failing to - all the way across.
    /// </summary>
    /// <param name="label">The voicing label the file name ends in.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Range(string label)
    {
        // E2 to C6: a bass's bottom note to a soprano's top one.
        int[] keys = [40, 43, 47, 50, 54, 57, 61, 64, 67, 71, 74, 78, 81, 84];
        const long length = 1920;       // two seconds a note

        MidiEventCollection events = GmAudition.NewCollection();
        List<GmAuditionCue> cues = [];

        events.AddEvent(new PatchChangeEvent(0, ChoirChannel, ChoirProgram), ChoirChannel);

        long tick = 0;

        foreach (int key in keys)
        {
            cues.Add(new GmAuditionCue(
                GmAudition.TimeOfTick(tick),
                GmAuditionCue.NoNumber,
                "key " + key.ToString(CultureInfo.InvariantCulture)));

            AddNote(events, tick, length, key, RangeVelocity);
            tick += length;
        }

        return GmAudition.Finish(
            "Choir Aahs - the whole range, a bass's bottom note to a soprano's top one",
            Name("3-range", label),
            events,
            cues);
    }

    /// <summary>
    /// The highest-rated piece of either audition - celesta over choir aahs - under the comparison's
    /// own name, so it lands beside the three written-for-the-purpose pieces.
    /// </summary>
    /// <param name="label">The voicing label the file name ends in.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Duet(string label)
    {
        GmAuditionPlan duet = GmRealPieces.Duet();

        return new GmAuditionPlan(duet.Title, Name("4-the-duet", label), duet.Sequence, duet.Cues);
    }

    private static GmAuditionPlan Chords(int program, string stem, string title, string label)
    {
        // Am - F - C - E, close harmony in the middle of the keyboard, each chord held for seven
        // seconds and butted against the next so the releases overlap the way a choir's do.
        int[][] chords =
        [
            [57, 60, 64, 69],
            [57, 60, 65, 69],
            [55, 60, 64, 67],
            [52, 59, 64, 68],
        ];

        string[] names = ["A minor", "F major", "C major", "E major"];
        const long length = 6720;       // seven seconds

        MidiEventCollection events = GmAudition.NewCollection();
        List<GmAuditionCue> cues = [];

        events.AddEvent(new PatchChangeEvent(0, ChoirChannel, program), ChoirChannel);

        long tick = 0;

        for (int chord = 0; chord < chords.Length; chord++)
        {
            cues.Add(new GmAuditionCue(GmAudition.TimeOfTick(tick), program, names[chord]));

            foreach (int key in chords[chord])
            {
                AddNote(events, tick, length, key, ChordVelocity);
            }

            tick += length;
        }

        return GmAudition.Finish(title, Name(stem, label), events, cues);
    }

    private static void AddNote(MidiEventCollection events, long tick, long length, int key, int velocity)
    {
        events.AddEvent(
            new NoteEvent(tick, ChoirChannel, MidiCommandCode.NoteOn, key, velocity), ChoirChannel);
        events.AddEvent(
            new NoteEvent(tick + length, ChoirChannel, MidiCommandCode.NoteOff, key, 0), ChoirChannel);
    }

    private static string Name(string stem, string label) => stem + "-" + label + ".wav";
}

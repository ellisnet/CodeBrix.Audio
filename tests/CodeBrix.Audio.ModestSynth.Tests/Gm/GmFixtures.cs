using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Ready-made MIDI sequences for auditioning the General MIDI bank: a tour of one family, a tour of
/// all 128 programs, a pattern for the percussion kit, and a deliberately dense piece for measuring
/// how fast the synthesizer renders.
/// </summary>
/// <remarks>
/// <para>
/// These exist so that the listening renders and the render-speed figure are a couple of lines
/// rather than a fixture-building exercise. Everything here is a plain
/// <see cref="MidiSequence" />, so it plays through <c>MidiSequencer</c> for a live audition and
/// through <c>SoundFontRenderer.RenderToFile</c> for a <c>.wav</c> to listen to later:
/// </para>
/// <code>
/// var synthesizer = new GeneralMidiSynthesizer(44100);
/// SoundFontRenderer.RenderToFile(
///     synthesizer,
///     GmFixtures.FamilyTour(GeneralMidiProgramFamily.SynthPad),
///     "synth-pad.wav",
///     TimeSpan.FromSeconds(3));
/// </code>
/// <para>
/// Every sequence carries its own program changes, so a multi-timbral
/// <see cref="GeneralMidiSynthesizer" /> plays it with no configuration at all.
/// </para>
/// </remarks>
public static class GmFixtures
{
    /// <summary>The ticks per quarter note every sequence here is written at.</summary>
    public const int TicksPerQuarterNote = 480;

    // A short rising-and-falling phrase, in semitones from the root, with the beat each note lands
    // on. Long enough to hear an attack, a body and a release, short enough that eight of them fit
    // into a listening session.
    private static readonly int[] PhraseSteps = [0, 4, 7, 12, 7, 4];
    private static readonly int[] PhraseBeats = [0, 1, 2, 3, 4, 5];

    /// <summary>
    /// A tour of one General MIDI family: the same short phrase on each of its eight programs, one
    /// after another.
    /// </summary>
    /// <param name="family">The family to tour.</param>
    /// <param name="root">The note the phrase is built from. Default middle C.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence FamilyTour(GeneralMidiProgramFamily family, int root = 60)
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);

        long tick = 0;
        int first = (int)family * GeneralMidi.ProgramsPerFamily;

        for (int offset = 0; offset < GeneralMidi.ProgramsPerFamily; offset++)
        {
            events.AddEvent(new PatchChangeEvent(tick, 1, first + offset), 1);
            tick = AddPhrase(events, tick, root);

            // A beat of silence between programs, so one does not run into the next.
            tick += TicksPerQuarterNote;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// A tour of every one of the 128 programs, in order - the whole bank in one sitting.
    /// </summary>
    /// <param name="root">The note the phrase is built from. Default middle C.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence AllProgramsTour(int root = 60)
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);

        long tick = 0;

        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            events.AddEvent(new PatchChangeEvent(tick, 1, program), 1);
            tick = AddPhrase(events, tick, root);
            tick += TicksPerQuarterNote;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// One strike of every percussion note from 35 to 81, in order, on the percussion channel.
    /// </summary>
    /// <returns>The sequence.</returns>
    public static MidiSequence PercussionTour()
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);

        long tick = 0;

        for (int note = GeneralMidi.LowestPercussionNote; note <= GeneralMidi.HighestPercussionNote; note++)
        {
            AddNote(events, GeneralMidi.PercussionChannel, tick, TicksPerQuarterNote / 2, note, 110);
            tick += TicksPerQuarterNote;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// A two-bar drum pattern - kick, snare, hi-hats and a crash - so the kit can be heard doing the
    /// thing a kit does rather than one piece at a time.
    /// </summary>
    /// <param name="bars">How many times to repeat it. Default four.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence PercussionPattern(int bars = 4)
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);

        int eighth = TicksPerQuarterNote / 2;

        for (int bar = 0; bar < Math.Max(1, bars); bar++)
        {
            long start = (long)bar * 4 * TicksPerQuarterNote;

            for (int step = 0; step < 8; step++)
            {
                long tick = start + (step * eighth);

                bool open = step == 7;
                int hat = open
                    ? (int)GeneralMidiPercussion.OpenHiHat
                    : (int)GeneralMidiPercussion.ClosedHiHat;

                AddNote(events, GeneralMidi.PercussionChannel, tick, eighth / 2, hat, step % 2 == 0 ? 100 : 74);

                if (step == 0 || step == 3 || step == 6)
                {
                    AddNote(events, GeneralMidi.PercussionChannel, tick, eighth / 2,
                        (int)GeneralMidiPercussion.BassDrum1, 118);
                }

                if (step == 2 || step == 6)
                {
                    AddNote(events, GeneralMidi.PercussionChannel, tick, eighth / 2,
                        (int)GeneralMidiPercussion.AcousticSnare, 112);
                }
            }

            if (bar == 0)
            {
                AddNote(events, GeneralMidi.PercussionChannel, start, eighth,
                    (int)GeneralMidiPercussion.CrashCymbal1, 108);
            }
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// A deliberately DENSE piece for measuring how fast the synthesizer renders: eight melodic
    /// parts holding chords plus a busy drum part, written to keep the voice pool full.
    /// </summary>
    /// <param name="seconds">Roughly how long it lasts. Default eight seconds.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// It is the fixture the render-speed figure is taken over. Play it into a synthesizer whose
    /// polyphony is set to the limit being measured, render it offline with
    /// <c>SoundFontRenderer.Render</c>, and compare the wall-clock time with
    /// <see cref="MidiSequence.Length" />.
    /// </remarks>
    public static MidiSequence DenseFixture(double seconds = 8.0)
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);

        // Eight parts, each on a program from a different family, so the measurement covers cheap
        // voicings and expensive ones alike rather than eight copies of a sine.
        int[] parts =
        [
            (int)GeneralMidiProgram.AcousticGrandPiano,
            (int)GeneralMidiProgram.StringEnsemble1,
            (int)GeneralMidiProgram.ChoirAahs,
            (int)GeneralMidiProgram.Pad2Warm,
            (int)GeneralMidiProgram.ElectricPiano1,
            (int)GeneralMidiProgram.AcousticGuitarSteel,
            (int)GeneralMidiProgram.SynthBass1,
            (int)GeneralMidiProgram.Lead2Sawtooth,
        ];

        int[] chord = [0, 4, 7, 11];

        long length = (long)(seconds * 2 * TicksPerQuarterNote);

        for (int part = 0; part < parts.Length; part++)
        {
            int channel = part + 1;
            events.AddEvent(new PatchChangeEvent(0, channel, parts[part]), channel);

            int root = 36 + (part * 5);

            for (long tick = 0; tick < length; tick += TicksPerQuarterNote)
            {
                for (int note = 0; note < chord.Length; note++)
                {
                    AddNote(events, channel, tick + (part * 7), TicksPerQuarterNote, root + chord[note], 96);
                }
            }
        }

        int sixteenth = TicksPerQuarterNote / 4;

        for (long tick = 0; tick < length; tick += sixteenth)
        {
            AddNote(events, GeneralMidi.PercussionChannel, tick, sixteenth,
                (int)GeneralMidiPercussion.ClosedHiHat, 92);

            if (tick % TicksPerQuarterNote == 0)
            {
                AddNote(events, GeneralMidi.PercussionChannel, tick, sixteenth,
                    (int)GeneralMidiPercussion.BassDrum1, 118);
            }
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>Every General MIDI family, in order, for a loop that renders one file each.</summary>
    /// <returns>The sixteen families.</returns>
    public static IEnumerable<GeneralMidiProgramFamily> Families()
    {
        for (int family = 0; family <= (int)GeneralMidiProgramFamily.SoundEffects; family++)
        {
            yield return (GeneralMidiProgramFamily)family;
        }
    }

    private static long AddPhrase(MidiEventCollection events, long start, int root)
    {
        long end = start;

        for (int i = 0; i < PhraseSteps.Length; i++)
        {
            long tick = start + ((long)PhraseBeats[i] * TicksPerQuarterNote / 2);
            long length = TicksPerQuarterNote / 2;

            // The last note of the phrase is held, so a release can be heard.
            if (i == PhraseSteps.Length - 1) { length = TicksPerQuarterNote * 2; }

            AddNote(events, 1, tick, length, root + PhraseSteps[i], i == 0 ? 112 : 92);

            if (tick + length > end) { end = tick + length; }
        }

        return end;
    }

    private static void AddNote(
        MidiEventCollection events, int channel, long tick, long length, int key, int velocity)
    {
        events.AddEvent(new NoteEvent(tick, channel, MidiCommandCode.NoteOn, key, velocity), channel);
        events.AddEvent(new NoteEvent(tick + length, channel, MidiCommandCode.NoteOff, key, 0), channel);
    }
}

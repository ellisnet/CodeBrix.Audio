using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Builds the auditions a listening session is made of: a tour of all 128 programs, a tour of one
/// General MIDI family, a roll call of the whole percussion kit and a groove that uses it.
/// </summary>
/// <remarks>
/// <para>
/// <c>GmFixtures</c> plays the SAME short phrase on everything, which is what a measurement wants:
/// one variable at a time. A listening session wants the opposite - a phrase that SUITS the family,
/// because a pad judged on a plucked figure and a woodblock judged on a held chord both sound like
/// faults that are not there. So each family here gets a phrase of the right kind, in a register
/// that suits it: something struck for the hammered and plucked families, a swell for the sustaining
/// ones, a melodic line for the blown and lead ones, a walking figure for the basses, and two long
/// notes for the effects, which are textures rather than instruments.
/// </para>
/// <para>
/// Every sequence carries an explicit tempo of 120 beats per minute, so one tick of the
/// <see cref="TicksPerQuarterNote" /> grid is exactly a 960th of a second and a cue's time is
/// arithmetic rather than a guess.
/// </para>
/// </remarks>
public static class GmAudition
{
    /// <summary>The ticks per quarter note every sequence here is written at.</summary>
    public const int TicksPerQuarterNote = 480;

    /// <summary>Microseconds per quarter note - 120 beats per minute.</summary>
    public const int MicrosecondsPerQuarterNote = 500000;

    /// <summary>The channel the melodic auditions play on, counted the way <c>MidiEvent</c> counts.</summary>
    public const int MelodicChannel = 1;

    /// <summary>How long a phrase lasts in the tour of all 128 programs, in ticks - two seconds.</summary>
    public const long TourPhraseTicks = 1920;

    /// <summary>How long a phrase lasts in a single family's audition, in ticks - three and a half seconds.</summary>
    public const long FamilyPhraseTicks = 3360;

    // The silence after a phrase, so a program does not smear into the next one through the reverb.
    private const long TourGapTicks = 480;
    private const long FamilyGapTicks = 600;

    // One quarter note is 480 ticks and lasts half a second, so a tick is a 960th of a second.
    private const double TicksPerSecond = TicksPerQuarterNote * 2.0;

    /// <summary>
    /// A tour of every one of the 128 programs, in program order, each playing a phrase that suits
    /// its family.
    /// </summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan AllPrograms()
    {
        MidiEventCollection events = NewCollection();
        List<GmAuditionCue> cues = new List<GmAuditionCue>();

        long tick = 0;

        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            GeneralMidiProgramFamily family = GeneralMidi.FamilyOf((GeneralMidiProgram)program);

            if (program % GeneralMidi.ProgramsPerFamily == 0)
            {
                cues.Add(new GmAuditionCue(
                    TimeOfTick(tick), GmAuditionCue.NoNumber, "--- " + GeneralMidi.DisplayName(family) + " ---"));
            }

            tick = AddProgram(events, cues, tick, program, family, TourPhraseTicks);
            tick += TourGapTicks;
        }

        return Finish("A tour of all 128 General MIDI programs", "all-128-programs.wav", events, cues);
    }

    /// <summary>
    /// One program on its own, playing exactly the phrase the tour of all 128 gives it - so a
    /// program can be measured or heard beside its neighbours on equal terms.
    /// </summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program" /> is outside 0 to 127.</exception>
    public static GmAuditionPlan SingleProgram(int program)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(program), program, "A General MIDI program is 0 to 127.");
        }

        MidiEventCollection events = NewCollection();
        List<GmAuditionCue> cues = new List<GmAuditionCue>();

        AddProgram(
            events, cues, 0, program,
            GeneralMidi.FamilyOf((GeneralMidiProgram)program), TourPhraseTicks);

        return Finish(
            GeneralMidi.DisplayName((GeneralMidiProgram)program),
            "program-" + program.ToString("000", CultureInfo.InvariantCulture) + ".wav",
            events,
            cues);
    }

    /// <summary>
    /// A tour of one General MIDI family: its eight programs in order, each with a longer phrase
    /// than the tour of all 128 gives them.
    /// </summary>
    /// <param name="family">The family to audition.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Family(GeneralMidiProgramFamily family)
    {
        string familyName = GeneralMidi.DisplayName(family);

        MidiEventCollection events = NewCollection();
        List<GmAuditionCue> cues = new List<GmAuditionCue>();

        long tick = 0;
        int first = (int)family * GeneralMidi.ProgramsPerFamily;

        for (int offset = 0; offset < GeneralMidi.ProgramsPerFamily; offset++)
        {
            tick = AddProgram(events, cues, tick, first + offset, family, FamilyPhraseTicks);
            tick += FamilyGapTicks;
        }

        return Finish(
            familyName + " - programs " + first.ToString(CultureInfo.InvariantCulture) + " to " +
                (first + GeneralMidi.ProgramsPerFamily - 1).ToString(CultureInfo.InvariantCulture),
            "family-" + ((int)family).ToString("00", CultureInfo.InvariantCulture) + "-" +
                familyName.Replace(' ', '-').ToLowerInvariant() + ".wav",
            events,
            cues);
    }

    /// <summary>
    /// One strike of every percussion note from 35 to 81, in order, far enough apart to name each
    /// one as it sounds.
    /// </summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan PercussionRollCall()
    {
        const long step = 720;      // three quarters of a second
        const long strike = 240;

        MidiEventCollection events = NewCollection();
        List<GmAuditionCue> cues = new List<GmAuditionCue>();

        long tick = 0;

        for (int note = GeneralMidi.LowestPercussionNote; note <= GeneralMidi.HighestPercussionNote; note++)
        {
            cues.Add(new GmAuditionCue(
                TimeOfTick(tick), note, GeneralMidi.DisplayName((GeneralMidiPercussion)note)));

            AddNote(events, GeneralMidi.PercussionChannel, tick, strike, note, 110);
            tick += step;
        }

        return Finish("Every piece of the percussion kit, 35 to 81", "kit-roll-call.wav", events, cues);
    }

    /// <summary>
    /// A groove that uses the kit the way music does: kick and snare, closed hi-hats, the ride, a
    /// tom fill across the stereo field, and an open hi-hat choked first by the closed hat and then
    /// by the pedal.
    /// </summary>
    /// <param name="repeats">How many times the four bars go round. Default two.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan PercussionGroove(int repeats = 2)
    {
        const long bar = TicksPerQuarterNote * 4;
        const long beat = TicksPerQuarterNote;
        const long eighth = TicksPerQuarterNote / 2;
        const long hit = 240;

        int kick = (int)GeneralMidiPercussion.BassDrum1;
        int snare = (int)GeneralMidiPercussion.AcousticSnare;
        int closed = (int)GeneralMidiPercussion.ClosedHiHat;
        int open = (int)GeneralMidiPercussion.OpenHiHat;
        int pedal = (int)GeneralMidiPercussion.PedalHiHat;
        int ride = (int)GeneralMidiPercussion.RideCymbal1;

        // High to low, which is left to right in the kit's layout.
        int[] fill =
        [
            (int)GeneralMidiPercussion.HighTom,
            (int)GeneralMidiPercussion.HiMidTom,
            (int)GeneralMidiPercussion.LowMidTom,
            (int)GeneralMidiPercussion.LowTom,
            (int)GeneralMidiPercussion.HighFloorTom,
            (int)GeneralMidiPercussion.LowFloorTom,
        ];

        MidiEventCollection events = NewCollection();
        List<GmAuditionCue> cues = new List<GmAuditionCue>();

        for (int round = 0; round < Math.Max(1, repeats); round++)
        {
            long start = round * 4 * bar;

            // Bar 1 - the plain groove, opening on a crash.
            cues.Add(Note(start, "bar 1 - a crash, then kick, snare and closed hi-hats"));
            AddNote(events, GeneralMidi.PercussionChannel, start, hit,
                (int)GeneralMidiPercussion.CrashCymbal1, 108);
            AddNote(events, GeneralMidi.PercussionChannel, start, hit, kick, 118);
            AddNote(events, GeneralMidi.PercussionChannel, start + (beat * 2) + eighth, hit, kick, 110);
            AddNote(events, GeneralMidi.PercussionChannel, start + beat, hit, snare, 112);
            AddNote(events, GeneralMidi.PercussionChannel, start + (beat * 3), hit, snare, 112);
            AddEighths(events, start, bar, closed);

            // Bar 2 - the ride takes over from the hats.
            long second = start + bar;
            cues.Add(Note(second, "bar 2 - the ride cymbal takes over from the hats"));
            AddNote(events, GeneralMidi.PercussionChannel, second, hit, kick, 118);
            AddNote(events, GeneralMidi.PercussionChannel, second + (beat * 2) + eighth, hit, kick, 110);
            AddNote(events, GeneralMidi.PercussionChannel, second + beat, hit, snare, 112);
            AddNote(events, GeneralMidi.PercussionChannel, second + (beat * 3), hit, snare, 112);
            AddEighths(events, second, bar, ride);

            // Bar 3 - half a bar of groove, then the toms sweep across the field.
            long third = start + (bar * 2);
            cues.Add(Note(third, "bar 3 - a tom fill sweeping across the stereo field"));
            AddNote(events, GeneralMidi.PercussionChannel, third, hit, kick, 118);
            AddNote(events, GeneralMidi.PercussionChannel, third + beat, hit, snare, 112);
            AddEighths(events, third, bar / 2, closed);

            for (int i = 0; i < fill.Length; i++)
            {
                AddNote(events, GeneralMidi.PercussionChannel, third + (beat * 2) + (i * 160), hit,
                    fill[i], 96 + (i * 4));
            }

            // Bar 4 - the open hat choked twice, and a crash to finish.
            long fourth = start + (bar * 3);
            cues.Add(Note(fourth, "bar 4 - an open hi-hat choked by the closed hat, then by the pedal"));
            AddNote(events, GeneralMidi.PercussionChannel, fourth, hit,
                (int)GeneralMidiPercussion.CrashCymbal2, 104);
            AddNote(events, GeneralMidi.PercussionChannel, fourth, hit, kick, 118);
            AddNote(events, GeneralMidi.PercussionChannel, fourth, hit, open, 104);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + eighth, hit, closed, 96);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + beat, hit, snare, 112);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + (beat * 2), hit, open, 104);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + (beat * 2) + eighth, hit, pedal, 90);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + (beat * 2) + eighth, hit, kick, 110);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + (beat * 3), hit, snare, 112);
            AddNote(events, GeneralMidi.PercussionChannel, fourth + (beat * 3) + eighth, hit,
                (int)GeneralMidiPercussion.CrashCymbal1, 112);
        }

        return Finish("A groove on the percussion kit", "kit-groove.wav", events, cues);
    }

    /// <summary>Every General MIDI family, in order.</summary>
    /// <returns>The sixteen families.</returns>
    public static IEnumerable<GeneralMidiProgramFamily> Families()
    {
        for (int family = 0; family <= (int)GeneralMidiProgramFamily.SoundEffects; family++)
        {
            yield return (GeneralMidiProgramFamily)family;
        }
    }

    /// <summary>The time a tick of one of these sequences falls at.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Its time from the start.</returns>
    public static TimeSpan TimeOfTick(long tick) => TimeSpan.FromSeconds(tick / TicksPerSecond);

    /// <summary>Starts an event collection carrying the tempo every audition here is written at.</summary>
    /// <returns>The collection.</returns>
    public static MidiEventCollection NewCollection()
    {
        MidiEventCollection events = new MidiEventCollection(1, TicksPerQuarterNote);
        events.AddEvent(new TempoEvent(MicrosecondsPerQuarterNote, 0), 1);
        return events;
    }

    /// <summary>Turns a finished collection and its cues into a plan.</summary>
    /// <param name="title">What the audition is.</param>
    /// <param name="fileName">The name the offline render writes it under.</param>
    /// <param name="events">The events.</param>
    /// <param name="cues">The cues, in time order.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Finish(
        string title, string fileName, MidiEventCollection events, IEnumerable<GmAuditionCue> cues)
    {
        if (events == null) { throw new ArgumentNullException(nameof(events)); }

        events.PrepareForExport();

        return new GmAuditionPlan(title, fileName, MidiSequence.FromEvents(events), cues);
    }

    private static GmAuditionCue Note(long tick, string text) =>
        new GmAuditionCue(TimeOfTick(tick), GmAuditionCue.NoNumber, text);

    private static void AddEighths(MidiEventCollection events, long start, long length, int note)
    {
        const long eighth = TicksPerQuarterNote / 2;

        for (long offset = 0; offset < length; offset += eighth)
        {
            AddNote(events, GeneralMidi.PercussionChannel, start + offset, eighth / 2, note,
                offset % TicksPerQuarterNote == 0 ? 100 : 74);
        }
    }

    private static long AddProgram(
        MidiEventCollection events,
        List<GmAuditionCue> cues,
        long tick,
        int program,
        GeneralMidiProgramFamily family,
        long span)
    {
        cues.Add(new GmAuditionCue(
            TimeOfTick(tick), program, GeneralMidi.DisplayName((GeneralMidiProgram)program)));

        events.AddEvent(new PatchChangeEvent(tick, MelodicChannel, program), MelodicChannel);

        return AddPhrase(events, tick, family, span);
    }

    private static long AddPhrase(
        MidiEventCollection events, long start, GeneralMidiProgramFamily family, long span)
    {
        int root = RootOf(family);
        bool longer = span >= FamilyPhraseTicks;

        foreach (PhraseStep step in StepsFor(ShapeOf(family), longer))
        {
            AddNote(events, MelodicChannel, start + step.Tick, step.Length, root + step.Semitone,
                step.Velocity);
        }

        return start + span;
    }

    private static void AddNote(
        MidiEventCollection events, int channel, long tick, long length, int key, int velocity)
    {
        events.AddEvent(new NoteEvent(tick, channel, MidiCommandCode.NoteOn, key, velocity), channel);
        events.AddEvent(new NoteEvent(tick + length, channel, MidiCommandCode.NoteOff, key, 0), channel);
    }

    // The register each family is heard in. A bass judged at middle C and a piccolo judged two
    // octaves below it both sound wrong for a reason that has nothing to do with the voicing.
    private static int RootOf(GeneralMidiProgramFamily family)
    {
        switch (family)
        {
            case GeneralMidiProgramFamily.ChromaticPercussion: return 72;
            case GeneralMidiProgramFamily.Organ: return 55;
            case GeneralMidiProgramFamily.Guitar: return 52;
            case GeneralMidiProgramFamily.Bass: return 36;
            case GeneralMidiProgramFamily.Strings: return 55;
            case GeneralMidiProgramFamily.Ensemble: return 57;
            case GeneralMidiProgramFamily.Brass: return 57;
            case GeneralMidiProgramFamily.Reed: return 62;
            case GeneralMidiProgramFamily.Pipe: return 67;
            case GeneralMidiProgramFamily.SynthPad: return 48;
            default: return 60;
        }
    }

    private static PhraseShape ShapeOf(GeneralMidiProgramFamily family)
    {
        switch (family)
        {
            case GeneralMidiProgramFamily.Piano:
            case GeneralMidiProgramFamily.ChromaticPercussion:
            case GeneralMidiProgramFamily.Guitar:
            case GeneralMidiProgramFamily.Ethnic:
            case GeneralMidiProgramFamily.Percussive:
                return PhraseShape.Struck;

            case GeneralMidiProgramFamily.Organ:
            case GeneralMidiProgramFamily.Strings:
            case GeneralMidiProgramFamily.Ensemble:
            case GeneralMidiProgramFamily.SynthPad:
                return PhraseShape.Sustained;

            case GeneralMidiProgramFamily.Brass:
            case GeneralMidiProgramFamily.Reed:
            case GeneralMidiProgramFamily.Pipe:
            case GeneralMidiProgramFamily.SynthLead:
                return PhraseShape.Line;

            case GeneralMidiProgramFamily.Bass:
                return PhraseShape.Walk;

            default:
                return PhraseShape.Texture;
        }
    }

    private static PhraseStep[] StepsFor(PhraseShape shape, bool longer)
    {
        switch (shape)
        {
            case PhraseShape.Struck:
                return longer
                    ? [
                        new PhraseStep(0, 0, 240, 112), new PhraseStep(240, 4, 240, 96),
                        new PhraseStep(480, 7, 240, 96), new PhraseStep(720, 12, 240, 104),
                        new PhraseStep(960, 7, 240, 92), new PhraseStep(1200, 4, 240, 92),
                        new PhraseStep(1440, 0, 1920, 100), new PhraseStep(1440, 4, 1920, 92),
                        new PhraseStep(1440, 7, 1920, 92), new PhraseStep(1440, 12, 1920, 96),
                    ]
                    : [
                        new PhraseStep(0, 0, 240, 112), new PhraseStep(240, 4, 240, 96),
                        new PhraseStep(480, 7, 240, 96), new PhraseStep(720, 12, 240, 104),
                        new PhraseStep(960, 0, 960, 100), new PhraseStep(960, 4, 960, 92),
                        new PhraseStep(960, 7, 960, 92), new PhraseStep(960, 12, 960, 96),
                    ];

            case PhraseShape.Sustained:
                return longer
                    ? [
                        new PhraseStep(0, 0, 3360, 92), new PhraseStep(0, 7, 3360, 84),
                        new PhraseStep(360, 4, 3000, 80), new PhraseStep(960, 12, 2400, 88),
                        new PhraseStep(1920, 14, 1440, 82),
                    ]
                    : [
                        new PhraseStep(0, 0, 1920, 92), new PhraseStep(0, 7, 1920, 84),
                        new PhraseStep(240, 4, 1680, 80), new PhraseStep(720, 12, 1200, 88),
                    ];

            case PhraseShape.Line:
                return longer
                    ? [
                        new PhraseStep(0, 0, 220, 104), new PhraseStep(240, 2, 220, 92),
                        new PhraseStep(480, 4, 220, 92), new PhraseStep(720, 7, 220, 96),
                        new PhraseStep(960, 9, 220, 92), new PhraseStep(1200, 7, 240, 92),
                        new PhraseStep(1440, 4, 460, 96), new PhraseStep(1920, 5, 220, 92),
                        new PhraseStep(2160, 7, 220, 92), new PhraseStep(2400, 9, 220, 96),
                        new PhraseStep(2640, 12, 720, 100),
                    ]
                    : [
                        new PhraseStep(0, 0, 220, 104), new PhraseStep(240, 2, 220, 92),
                        new PhraseStep(480, 4, 220, 92), new PhraseStep(720, 7, 220, 96),
                        new PhraseStep(960, 9, 220, 92), new PhraseStep(1200, 7, 240, 92),
                        new PhraseStep(1440, 4, 480, 96),
                    ];

            case PhraseShape.Walk:
                return longer
                    ? [
                        new PhraseStep(0, 0, 400, 112), new PhraseStep(480, 7, 400, 96),
                        new PhraseStep(960, 0, 400, 100), new PhraseStep(1440, 10, 400, 96),
                        new PhraseStep(1920, 12, 400, 104), new PhraseStep(2400, 10, 400, 96),
                        new PhraseStep(2880, 7, 480, 96),
                    ]
                    : [
                        new PhraseStep(0, 0, 400, 112), new PhraseStep(480, 7, 400, 96),
                        new PhraseStep(960, 0, 400, 100), new PhraseStep(1440, 10, 480, 96),
                    ];

            default:
                return longer
                    ? [
                        new PhraseStep(0, 0, 1440, 100), new PhraseStep(1440, 7, 960, 100),
                        new PhraseStep(2400, 12, 960, 100),
                    ]
                    : [
                        new PhraseStep(0, 0, 960, 100), new PhraseStep(960, 7, 960, 100),
                    ];
        }
    }

    // The kinds of phrase a family can be auditioned with.
    private enum PhraseShape
    {
        Struck,
        Sustained,
        Line,
        Walk,
        Texture,
    }

    // One note of a phrase, in ticks from the start of the phrase and semitones from its root.
    private readonly struct PhraseStep
    {
        public PhraseStep(long tick, int semitone, long length, int velocity)
        {
            Tick = tick;
            Semitone = semitone;
            Length = length;
            Velocity = velocity;
        }

        public long Tick { get; }

        public int Semitone { get; }

        public long Length { get; }

        public int Velocity { get; }
    }
}

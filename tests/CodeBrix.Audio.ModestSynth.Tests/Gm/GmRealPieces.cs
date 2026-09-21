using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeBrix.Audio.Abc;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The four pieces of real generated music under <c>tests/Assets/generated-music/</c>, turned into
/// auditions.
/// </summary>
/// <remarks>
/// <para>
/// A phrase built by a fixture proves a voicing makes a sound. Only a piece of music proves the
/// bank can carry one, and these four were chosen because they were RATED: two SkyTNT MIDI files
/// and two MuPT ABC tunes, all generated locally from Apache-2.0 models. See
/// <c>tests/Assets/generated-music/GENERATED-MUSIC.txt</c> and THIRD-PARTY-NOTICES.txt.
/// </para>
/// <para>
/// THE TWO <c>.mid</c> FILES ARE NOT RE-VOICED. They carry their own program changes and play
/// through a plain <see cref="GeneralMidiSynthesizer" /> with no configuration at all, which is the
/// "plays any <c>.mid</c>" promise being kept or broken in public. The two ABC tunes carry no
/// instrument at all - ABC has no notion of one unless the transcriber writes
/// <c>%%MIDI program</c> - so a voice is chosen for each, one distinct voice per part.
/// </para>
/// </remarks>
public static class GmRealPieces
{
    /// <summary>The folder the pieces are copied into beside the test assembly.</summary>
    public const string AssetFolder = "generated-music";

    /// <summary>SkyTNT, 1:00, piano and violin and flute. Rated 8/10, "very ambient ... ethereal".</summary>
    public const string OrdinaryFileName = "skytnt-fp32-ordinary.mid";

    /// <summary>SkyTNT, 0:34, four parts and a drum part on channel 10. Rated 8/10.</summary>
    public const string DenseFileName = "skytnt-fp32-dense.mid";

    /// <summary>MuPT, a two-voice waltz duet. The highest-rated piece of either audition, at 9.7/10.</summary>
    public const string DuetFileName = "mupt-q8_0-duet-Am-waltz-seed29.abc";

    /// <summary>MuPT, a slow single-voice air. Rated 8.5/10.</summary>
    public const string AirFileName = "mupt-q8_0-air-Dmix-seed11.abc";

    /// <summary>The duet's upper voice: celesta, which is how the piece was heard when it was rated.</summary>
    public const int DuetUpperVoiceProgram = (int)GeneralMidiProgram.Celesta;

    /// <summary>The duet's lower voice: choir aahs, the other half of the sound that was rated 9.7.</summary>
    public const int DuetLowerVoiceProgram = (int)GeneralMidiProgram.ChoirAahs;

    /// <summary>The voice chosen for the air.</summary>
    public const int AirProgram = (int)GeneralMidiProgram.PanFlute;

    /// <summary>A second voicing of the air, written beside the first so the two can be compared.</summary>
    public const int AirAlternativeProgram = (int)GeneralMidiProgram.ChoirAahs;

    /// <summary>The full path of one of the pieces, beside the test assembly.</summary>
    /// <param name="fileName">The file name, as one of the constants on this class gives it.</param>
    /// <returns>The path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileName" /> is null.</exception>
    public static string Path(string fileName)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        return System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", AssetFolder, fileName);
    }

    /// <summary>The SkyTNT piece, played exactly as the file asks.</summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Ordinary() =>
        FromMidiFile(
            OrdinaryFileName,
            "SkyTNT - the ordinary piece (rated 8/10, \"very ambient ... ethereal\")",
            "piece-1-skytnt-ordinary.wav");

    /// <summary>The denser SkyTNT piece - the one with a drum part - played exactly as the file asks.</summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Dense() =>
        FromMidiFile(
            DenseFileName,
            "SkyTNT - the dense piece with a drum part (rated 8/10)",
            "piece-2-skytnt-dense.wav");

    /// <summary>The MuPT duet, voiced celesta over choir aahs, one voice each.</summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Duet() =>
        FromAbcFile(
            DuetFileName,
            "MuPT - the waltz duet (rated 9.7/10), celesta over choir aahs",
            "piece-3-mupt-duet-celesta-and-choir.wav",
            [DuetUpperVoiceProgram, DuetLowerVoiceProgram]);

    /// <summary>The MuPT air, on the voice chosen for it.</summary>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan Air() => AirVoicedAs(AirProgram, "piece-4-mupt-air-pan-flute.wav");

    /// <summary>The MuPT air on a voice of your choosing, so two voicings can be compared.</summary>
    /// <param name="program">The General MIDI program to play it on, 0 to 127.</param>
    /// <param name="fileName">The name the offline render writes it under.</param>
    /// <returns>The plan.</returns>
    public static GmAuditionPlan AirVoicedAs(int program, string fileName) =>
        FromAbcFile(
            AirFileName,
            "MuPT - the slow air (rated 8.5/10), on " +
                GeneralMidi.DisplayName((GeneralMidiProgram)program),
            fileName,
            [program]);

    /// <summary>The four pieces, in the order a listening session takes them.</summary>
    /// <returns>The plans.</returns>
    public static IReadOnlyList<GmAuditionPlan> All() => [Ordinary(), Dense(), Duet(), Air()];

    private static GmAuditionPlan FromMidiFile(string fileName, string title, string outputFileName)
    {
        string path = Path(fileName);

        MidiFile file = new MidiFile(path);
        List<GmAuditionCue> cues = [new GmAuditionCue(TimeSpan.Zero, GmAuditionCue.NoNumber, title)];

        double secondsPerTick = SecondsPerTick(file);
        int[] notes = NotesPerChannel(file);
        HashSet<int> announced = [];

        for (int track = 0; track < file.Events.Tracks; track++)
        {
            foreach (MidiEvent midiEvent in file.Events[track])
            {
                PatchChangeEvent patch = midiEvent as PatchChangeEvent;
                if (patch == null || !announced.Add(patch.Channel)) { continue; }

                // On channel 10 a program change chooses a drum KIT rather than an instrument, so
                // naming it after a melodic program there would say the opposite of what happens.
                string what = patch.Channel == GeneralMidi.PercussionChannel
                    ? "the percussion channel - drum kit " +
                        patch.Patch.ToString(CultureInfo.InvariantCulture)
                    : "channel " + patch.Channel.ToString("00", CultureInfo.InvariantCulture) +
                        " - " + GeneralMidi.DisplayName((GeneralMidiProgram)patch.Patch);

                if (notes[patch.Channel] == 0) { what += "  (no notes in this file)"; }

                cues.Add(new GmAuditionCue(
                    TimeSpan.FromSeconds(patch.AbsoluteTime * secondsPerTick), patch.Patch, what));
            }
        }

        cues.Sort(CompareCues);

        return new GmAuditionPlan(title, outputFileName, new MidiSequence(path), cues);
    }

    private static GmAuditionPlan FromAbcFile(
        string fileName, string title, string outputFileName, int[] programs)
    {
        AbcTuneBook book = AbcReader.Read(Path(fileName));
        AbcTune tune = book.Tunes[0];

        AbcToMidiOptions options = new AbcToMidiOptions
        {
            TicksPerQuarterNote = GmAudition.TicksPerQuarterNote,
        };

        // Pin the channels rather than letting them be assigned, so the program change written
        // below lands on the voice it was meant for.
        for (int voice = 0; voice < tune.Voices.Count; voice++)
        {
            options.VoiceChannels[tune.Voices[voice].Id] = voice + 1;
        }

        MidiEventCollection events = AbcToMidi.Convert(tune, options);

        List<GmAuditionCue> cues = [new GmAuditionCue(TimeSpan.Zero, GmAuditionCue.NoNumber, title)];

        for (int voice = 0; voice < tune.Voices.Count; voice++)
        {
            int program = programs[Math.Min(voice, programs.Length - 1)];
            int channel = voice + 1;

            // The conversion writes voice i to track i + 1; the conductor track is 0.
            events.AddEvent(new PatchChangeEvent(0, channel, program), voice + 1);

            cues.Add(new GmAuditionCue(
                TimeSpan.Zero,
                program,
                "voice " + (voice + 1).ToString(CultureInfo.InvariantCulture) + " - " +
                    GeneralMidi.DisplayName((GeneralMidiProgram)program)));
        }

        return GmAudition.Finish(title, outputFileName, events, cues);
    }

    // How many notes each channel actually plays. A generated file often declares an instrument on
    // a channel it then leaves empty, and announcing a part that never sounds is worse than
    // silence: the listener goes looking for it.
    private static int[] NotesPerChannel(MidiFile file)
    {
        int[] notes = new int[17];

        for (int track = 0; track < file.Events.Tracks; track++)
        {
            foreach (MidiEvent midiEvent in file.Events[track])
            {
                NoteEvent note = midiEvent as NoteEvent;

                if (note != null && note.CommandCode == MidiCommandCode.NoteOn && note.Velocity > 0)
                {
                    notes[note.Channel]++;
                }
            }
        }

        return notes;
    }

    // These files carry one tempo, at the start. A file with a tempo map would need the sequencer's
    // own conversion; nothing here has one, and the cues are only announcements.
    private static double SecondsPerTick(MidiFile file)
    {
        int microseconds = GmAudition.MicrosecondsPerQuarterNote;

        for (int track = 0; track < file.Events.Tracks; track++)
        {
            foreach (MidiEvent midiEvent in file.Events[track])
            {
                TempoEvent tempo = midiEvent as TempoEvent;

                if (tempo != null && tempo.AbsoluteTime == 0)
                {
                    microseconds = tempo.MicrosecondsPerQuarterNote;
                    break;
                }
            }
        }

        return microseconds / 1000000.0 / file.DeltaTicksPerQuarterNote;
    }

    private static int CompareCues(GmAuditionCue first, GmAuditionCue second) =>
        first.Start.CompareTo(second.Start);
}

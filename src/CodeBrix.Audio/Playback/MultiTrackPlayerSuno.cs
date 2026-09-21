using System;
using System.Collections.Generic;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// Building a player from a Suno stems export. A <see cref="SunoSong"/> is one loader's answer for
/// this player; nothing about the player knows or cares where its tracks came from.
/// </summary>
public sealed partial class MultiTrackPlayer
{
    /// <summary>
    /// Builds a player from a loaded stems export: one track per stem, playing the recordings.
    /// </summary>
    /// <param name="song">The song to play.</param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="song"/> is null.</exception>
    public static MultiTrackPlayer Load(SunoSong song) => Load(song, null);

    /// <summary>
    /// Builds a player from a loaded stems export.
    /// </summary>
    /// <param name="song">The song to play.</param>
    /// <param name="options">
    /// What to do beyond putting one track on each stem; null for the defaults, which play the
    /// recordings and attach a MIDI source to any stem an instrument can be found for.
    /// </param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="song"/> is null.</exception>
    /// <exception cref="System.IO.IOException">A stem's audio could not be read.</exception>
    /// <exception cref="InvalidOperationException">
    /// An instrument library was named and nothing is registered under that name, or nothing is
    /// registered at all. The message is the registry's own.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The instrument library named does not offer the per-part shape, which a stems export needs.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Every stem with audio becomes a track playing that audio - the WAV when there is one, the MP3
    /// otherwise - which is the default mix: the song as it was downloaded. Where the stem also has
    /// MIDI and an instrument can be built for it, the same track carries the MIDI as its second
    /// source, so switching a part from the recording to a synthesized rendition is a property
    /// change rather than a rebuild. A stem with MIDI and no audio becomes a MIDI-only track.
    /// </para>
    /// <para>
    /// The instrument comes from the first of these that is set:
    /// <see cref="SunoPlayerOptions.StemInstruments"/> for that stem,
    /// <see cref="SunoPlayerOptions.InstrumentLibrary"/> or
    /// <see cref="SunoPlayerOptions.InstrumentLibraryName"/>,
    /// <see cref="SunoPlayerOptions.InstrumentFactory"/>, and finally a General MIDI SoundFont - the
    /// options' path, or the one the song was loaded with. A SoundFont is loaded once and shared by
    /// every track; each track still gets its own synthesizer, because a synthesizer is not
    /// thread-safe.
    /// </para>
    /// <para>
    /// WHAT COULD NOT BE HONOURED IS REPORTED, never thrown: a per-stem instrument naming a stem
    /// the song does not have, a part whose program or notes the chosen library does not cover, a
    /// selection naming an absent stem. They land in <see cref="Problems"/>, in the same idiom as
    /// <see cref="SunoSong.Problems"/>.
    /// </para>
    /// <para>
    /// Extracting a stems zip to memory rather than to a cache folder makes every stem's audio a
    /// stream, and a stream source is copied into memory when the track is built. A four-minute
    /// export is several hundred megabytes of WAV, so that combination is for hosts that cannot
    /// write to disk.
    /// </para>
    /// </remarks>
    public static MultiTrackPlayer Load(SunoSong song, SunoPlayerOptions options)
    {
        if (song == null)
        {
            throw new ArgumentNullException(nameof(song));
        }

        var settings = options == null ? new SunoPlayerOptions() : options.Clone();
        var problems = new List<string>();

        // The library is resolved HERE, on the caller's thread, so an unknown name is the
        // registry's own error on the line that built the player rather than an exception from a
        // worker in the middle of a song.
        var library = ResolveLibrary(settings);
        var instruments = BuildInstruments(song, settings, library, problems);

        var player = new MultiTrackPlayer
        {
            AutoSetRelativeTrackLevels = settings.AutoSetRelativeTrackLevels,
        };

        try
        {
            foreach (var stem in song.Stems)
            {
                var track = BuildTrack(song, stem, settings, instruments);
                if (track != null)
                {
                    player.Add(track);
                }
            }

            ApplyStemSelection(song, settings, player, problems);
            ReportUnknownStemNames(song, settings, problems);
            ReportCoverage(song, settings, library, problems);
        }
        catch
        {
            player.Dispose();
            throw;
        }

        player.AddProblems(problems);
        return player;
    }

    // The library the whole song is played through, or null when none was asked for. An instance
    // wins over a name, so a consumer who has the library in hand never has to register it.
    private static IInstrumentLibrary ResolveLibrary(SunoPlayerOptions settings)
    {
        var library = settings.InstrumentLibrary;

        if (library == null)
        {
            if (string.IsNullOrWhiteSpace(settings.InstrumentLibraryName))
            {
                return null;
            }

            library = InstrumentLibraryRegistry.Resolve(settings.InstrumentLibraryName);
        }

        if (!library.SupportsPerPart)
        {
            throw new NotSupportedException(
                $"The instrument library '{library.Name}' does not offer the per-part shape, so it " +
                "cannot play a stems export: each stem is a part of an arrangement with a gain and " +
                "a source of its own, and one multi-timbral synthesizer mixes internally at one " +
                "level. Name a library whose SupportsPerPart is true, or build the instruments " +
                "yourself with SunoPlayerOptions.InstrumentFactory.");
        }

        return library;
    }

    // The instrument every MIDI stem is played through, or null when there is nothing to play one
    // through. The SoundFont is loaded HERE, once, so that the factory the tracks hold shares it.
    private static Func<SunoStem, int, IMidiSynthesizer> BuildInstruments(
        SunoSong song, SunoPlayerOptions settings, IInstrumentLibrary library, List<string> problems)
    {
        var perStem = settings.StemInstruments;
        var fallback = BuildSongInstruments(song, settings, library, problems);

        if (perStem.Count == 0)
        {
            return fallback;
        }

        return (stem, sampleRate) => perStem.TryGet(stem.Name, out var factory)
            ? factory(sampleRate)
            : fallback == null ? null : fallback(stem, sampleRate);
    }

    // Everything below the per-stem overrides, in order: the library, the consumer's factory, a
    // General MIDI SoundFont.
    private static Func<SunoStem, int, IMidiSynthesizer> BuildSongInstruments(
        SunoSong song, SunoPlayerOptions settings, IInstrumentLibrary library, List<string> problems)
    {
        if (library != null)
        {
            if (settings.InstrumentFactory != null)
            {
                problems.Add(
                    $"Both an instrument library ('{library.Name}') and an InstrumentFactory were " +
                    "given. The library is used and the factory is never called; use " +
                    "SunoPlayerOptions.StemInstruments to give one part an instrument of its own.");
            }

            return (stem, sampleRate) => stem.IsPercussion
                ? library.CreatePercussionSynthesizer(sampleRate)
                : library.CreateSynthesizer(ProgramOf(stem), sampleRate);
        }

        if (settings.InstrumentFactory != null)
        {
            return settings.InstrumentFactory;
        }

        var path = settings.GeneralMidiSoundFontPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = song.Options == null ? null : song.Options.GeneralMidiSoundFontPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cache = settings.SoundFontCache ?? SunoPlayerOptions.SharedSoundFonts;
        var soundFont = cache.Get(path);
        return (stem, sampleRate) => new SoundFontSynthesizer(soundFont, sampleRate);
    }

    private static PlayerTrack BuildTrack(SunoSong song, SunoStem stem, SunoPlayerOptions settings,
        Func<SunoStem, int, IMidiSynthesizer> instruments)
    {
        var wantsMidi = settings.IncludeMidiSources && stem.Midi != null && instruments != null;

        PlayerTrack track;
        if (stem.HasAudio)
        {
            track = InMemory(song)
                ? new AudioTrack(stem.OpenAudio(), leaveOpen: false, name: stem.Name)
                : new AudioTrack(stem.GetAudioPath(), stem.Name);

            if (wantsMidi)
            {
                track.SetMidiSource(stem.Midi, sampleRate => instruments(stem, sampleRate));
            }
        }
        else if (wantsMidi)
        {
            track = new MidiTrack(stem.Midi, sampleRate => instruments(stem, sampleRate), stem.Name);
        }
        else
        {
            // Nothing to play: a MIDI-only stem with no instrument to play it through.
            return null;
        }

        ApplyStemSettings(song, stem, settings, track);
        return track;
    }

    // Everything a stem says about how its part should be played, whichever source is heard.
    private static void ApplyStemSettings(SunoSong song, SunoStem stem, SunoPlayerOptions settings,
        PlayerTrack track)
    {
        if (stem.GmProgram >= 0 && stem.GmProgram <= 127)
        {
            track.GmProgram = stem.GmProgram;
        }

        track.IsPercussion = stem.IsPercussion;
        track.MinimumNoteHold = song.Options == null
            ? PlayerTrack.DefaultMinimumNoteHold
            : song.Options.MinimumNoteHold;
        track.IgnoreNoteOff = settings.IgnoreNoteOffOnPercussion && stem.IsPercussion;

        if (settings.ApplyAlignmentOffsets)
        {
            track.MidiSourceOffset = stem.AlignmentOffset;
        }
    }

    // Which tracks start on their transcription. Without a selection nothing is touched, which is
    // what a player has always done: every track starts on its recording.
    private static void ApplyStemSelection(
        SunoSong song, SunoPlayerOptions settings, MultiTrackPlayer player, List<string> problems)
    {
        var selection = settings.MidiStems;
        if (selection == null)
        {
            return;
        }

        foreach (var track in player.Tracks)
        {
            var stem = song[track.Name];
            if (stem == null || !track.HasMidiSource)
            {
                continue;
            }

            if (selection.Includes(stem))
            {
                track.ActiveSource = TrackSource.Midi;
            }
        }

        foreach (var name in selection.NamedStems)
        {
            if (song[name] == null)
            {
                problems.Add(
                    $"The stem selection names '{name}', which this song has no stem for. " +
                    $"It has: {DescribeStems(song)}.");
            }
        }
    }

    private static void ReportUnknownStemNames(
        SunoSong song, SunoPlayerOptions settings, List<string> problems)
    {
        foreach (var name in settings.StemInstruments.StemNames)
        {
            if (song[name] == null)
            {
                problems.Add(
                    $"A per-stem instrument was given for '{name}', which this song has no stem " +
                    $"for, so nothing plays it. It has: {DescribeStems(song)}.");
            }
        }
    }

    // What the chosen library cannot play of what this song holds. Reported, never thrown: a part
    // the library has no sound for comes out SILENT, which is otherwise indistinguishable from a
    // fault in the music.
    private static void ReportCoverage(
        SunoSong song, SunoPlayerOptions settings, IInstrumentLibrary library, List<string> problems)
    {
        if (library == null)
        {
            return;
        }

        var coverage = library.Coverage;

        foreach (var stem in song.Stems)
        {
            if (stem.Midi == null || settings.StemInstruments.Contains(stem.Name))
            {
                continue;
            }

            if (stem.IsPercussion)
            {
                var missing = MissingPercussionNotes(coverage, stem);
                if (missing != null)
                {
                    problems.Add(
                        $"The instrument library '{library.Name}' has no percussion sound for " +
                        $"note(s) {missing} of the stem '{stem.Name}', which will not be heard.");
                }

                continue;
            }

            var program = ProgramOf(stem);
            if (!coverage.CoversProgram(program))
            {
                problems.Add(
                    $"The instrument library '{library.Name}' does not cover General MIDI program " +
                    $"{program}, which the stem '{stem.Name}' plays, so that part will be silent.");
                continue;
            }

            var outside = NotesOutsideRange(coverage, program, stem);
            if (outside != null)
            {
                problems.Add(
                    $"The instrument library '{library.Name}' plays General MIDI program {program} " +
                    $"over {coverage.KeyRangeOf(program)}, and the stem '{stem.Name}' uses " +
                    $"note(s) {outside} outside it, which will not be heard.");
            }
        }
    }

    private static string MissingPercussionNotes(InstrumentCoverage coverage, SunoStem stem)
    {
        var missing = new List<int>();
        foreach (var note in stem.UsedNotes)
        {
            if (!coverage.CoversPercussionNote(note))
            {
                missing.Add(note);
            }
        }

        return missing.Count == 0 ? null : Describe(missing);
    }

    private static string NotesOutsideRange(InstrumentCoverage coverage, int program, SunoStem stem)
    {
        var outside = new List<int>();
        foreach (var note in stem.UsedNotes)
        {
            if (!coverage.CoversNote(program, note))
            {
                outside.Add(note);
            }
        }

        return outside.Count == 0 ? null : Describe(outside);
    }

    // A handful of note numbers reads as a list; a whole part's worth reads as a count and a range.
    private static string Describe(List<int> notes) =>
        notes.Count <= 6
            ? string.Join(", ", notes)
            : $"{notes.Count} of them, {notes[0]} to {notes[notes.Count - 1]}";

    private static string DescribeStems(SunoSong song)
    {
        var names = new List<string>();
        foreach (var stem in song.Stems)
        {
            names.Add($"'{stem.Name}'");
        }

        return names.Count == 0 ? "no stems at all" : string.Join(", ", names);
    }

    // A percussion stem's "program" is a kit number, and a library's coverage is about melodic
    // programs, so a melodic stem outside 0 to 127 falls back to the first program rather than
    // asking for one that cannot exist.
    private static int ProgramOf(SunoStem stem) =>
        stem.GmProgram >= 0 && stem.GmProgram <= 127 ? stem.GmProgram : 0;

    private static bool InMemory(SunoSong song) =>
        song.Source == SunoSourceKind.ZipArchive &&
        song.Options != null &&
        song.Options.ZipExtraction == SunoZipExtraction.Memory;
}

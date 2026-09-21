using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Samples.FluidR3Gm;
using CodeBrix.Audio.Wave;

namespace SunoInstrumentSwap;

/// <summary>
/// The sample itself: load a stems export, render it as it was downloaded, and render it again
/// through each instrument library named on the command line.
/// </summary>
/// <remarks>
/// Every render here is OFFLINE. No audio device is opened at any point, which is why the levels
/// are matched with <c>MeasureRelativeTrackLevels</c> rather than through <c>Prepare</c>.
/// </remarks>
internal sealed class SwapRunner
{
    // Every render is a WAV. The writer seam reaches every registered format by EXTENSION, so a
    // sample that wanted .aiff would change this one line and nothing else.
    private const string Extension = ".wav";

    private readonly SwapOptions options;

    internal SwapRunner(SwapOptions options) => this.options = options;

    /// <summary>Runs the whole job.</summary>
    /// <returns>Zero when everything was written, and 1 when something could not be.</returns>
    internal int Run()
    {
        RegisterLibraries();

        Console.WriteLine($"Loading  {options.SourcePath}");
        var loading = Stopwatch.StartNew();
        var song = SunoStemsLoader.Load(options.SourcePath, new SunoLoadOptions
        {
            CacheFolder = options.CacheFolder,
        });

        loading.Stop();

        Console.WriteLine($"  \"{song.Title}\"  {Minutes(song.Duration)}  " +
            $"{song.Stems.Count} stem(s)  loaded in {loading.ElapsedMilliseconds} ms");

        ReportProblems("the download", song.Problems);
        PrintStems(song);

        var folder = Path.Combine(options.OutputFolder, SafeName(song.Title));
        Directory.CreateDirectory(folder);
        Console.WriteLine();
        Console.WriteLine($"Writing into {folder}");

        var written = 0;
        written += RenderOriginal(song, folder) ? 1 : 0;

        var index = 1;
        foreach (var libraryName in options.LibraryNames)
        {
            index++;
            written += RenderSwapped(song, folder, libraryName, index) ? 1 : 0;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {written} file(s) written.");
        return written == options.LibraryNames.Count + 1 ? 0 : 1;
    }

    // ModestSynthGm needs no files at all, so it is always available. FluidR3Gm is a sampled bank
    // that arrives with its own SoundFont beside the executable, so it is registered only when
    // that file is really there. A .sf2 the user named becomes a library of its own under the
    // name the user chose.
    private void RegisterLibraries()
    {
        GeneralMidiInstrumentLibrary.Register();

        if (FluidR3GmInstrumentLibrary.IsSoundFontAvailable)
        {
            FluidR3GmInstrumentLibrary.Register();
        }

        foreach (var soundFont in options.SoundFonts)
        {
            new SoundFontInstrumentLibrary(
                soundFont.Name,
                $"The SoundFont '{Path.GetFileName(soundFont.Path)}'.",
                soundFont.Path).Register();
        }

        Console.WriteLine($"Libraries registered: {string.Join(", ", InstrumentLibraryRegistry.RegisteredNames)}");
    }

    private void PrintStems(SunoSong song)
    {
        Console.WriteLine();
        Console.WriteLine("  stem                 audio   midi  notes  coverage  notes used   align");
        Console.WriteLine("  ------------------------------------------------------------------------");

        foreach (var stem in song.Stems)
        {
            var range = stem.UsedNotes.Count == 0
                ? "-"
                : $"{stem.LowestNote}-{stem.HighestNote}";

            Console.WriteLine(
                $"  {Fit(stem.Name, 20)} {(stem.HasAudio ? "yes" : "no "),5}   " +
                $"{(stem.HasMidi ? "yes" : "no "),4}  {stem.NoteCount,5}  " +
                $"{stem.MidiCoverage,8:0.000}  {Fit(range, 10)}  " +
                $"{stem.AlignmentOffset.TotalMilliseconds,6:+0;-0;0} ms" +
                (stem.AlignmentIsFallback ? " (song)" : string.Empty));
        }
    }

    // Version one: the song exactly as it was downloaded. The MIDI sources are left off because
    // nothing will switch to them, and a track holding two sources renders both on every block.
    private bool RenderOriginal(SunoSong song, string folder)
    {
        Console.WriteLine();
        Console.WriteLine("01  the download's own audio");

        using var player = song.CreatePlayer(new SunoPlayerOptions { IncludeMidiSources = false });

        return Write(player, Path.Combine(folder, "01-original" + Extension), song.Duration);
    }

    private bool RenderSwapped(SunoSong song, string folder, string libraryName, int index)
    {
        Console.WriteLine();
        Console.WriteLine($"{index:00}  through the instrument library '{libraryName}'");

        var selection = SunoStemSelection.EverythingBut(VocalStemsOf(song));
        selection.MinimumNoteCount = options.MinimumNoteCount;
        selection.MinimumMidiCoverage = options.MinimumCoverage;

        var playerOptions = new SunoPlayerOptions
        {
            InstrumentLibraryName = libraryName,
            MidiStems = selection,
        };

        foreach (var instrument in options.StemInstruments)
        {
            playerOptions.StemInstruments.SetFromFile(instrument.StemName, instrument.Path);
        }

        MultiTrackPlayer player;
        try
        {
            player = song.CreatePlayer(playerOptions);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            // An unregistered name, an empty registry, or a library that cannot voice parts
            // separately. The library's own message says which and what to do about it.
            Console.WriteLine($"    cannot use '{libraryName}': {error.Message}");
            return false;
        }

        using (player)
        {
            ReportProblems("the player", player.Problems);

            Console.WriteLine("    matching the levels...");
            var measuring = Stopwatch.StartNew();
            player.MeasureRelativeTrackLevels(options.SampleRate);
            measuring.Stop();

            var match = player.LastLevelMatch;
            if (match != null)
            {
                Console.WriteLine(
                    $"    matched {match.MatchedTrackCount} track(s) at {match.SampleRate} Hz in " +
                    $"{measuring.Elapsed.TotalSeconds:0.0} s; the matched mix peaks at " +
                    $"{match.MixPeak:0.000}");

                if (match.WouldClip)
                {
                    player.Volume = match.SuggestedVolume;
                    Console.WriteLine(
                        $"    that is past full scale, so Volume is set to {match.SuggestedVolume:0.000}");
                }
            }

            PrintPlan(song, player, selection);

            return Write(player, Path.Combine(folder, $"{index:00}-{SafeName(libraryName)}{Extension}"),
                song.Duration);
        }
    }

    // "Which stems are vocals" is a rule on the NAME, and this sample's default list is a guess -
    // no export carries every part. A guess is narrowed to the stems this song really has, so it
    // reports nothing; a list the user typed is passed through as written, so a name the song
    // lacks is reported rather than swallowed.
    private string[] VocalStemsOf(SunoSong song)
    {
        if (!options.VocalStemsAreTheDefault)
        {
            return options.VocalStems.ToArray();
        }

        return options.VocalStems.Where(name => song[name] != null).ToArray();
    }

    private void PrintPlan(SunoSong song, MultiTrackPlayer player, SunoStemSelection selection)
    {
        Console.WriteLine();
        Console.WriteLine("    stem                 plays      instrument            gain   why");
        Console.WriteLine("    ---------------------------------------------------------------------------");

        foreach (var track in player.Tracks)
        {
            var stem = song[track.Name];
            var midi = track.ActiveSource == TrackSource.Midi;

            var instrument = !midi
                ? "-"
                : Instrument(track.Name, stem);

            Console.WriteLine(
                $"    {Fit(track.Name, 20)} {(midi ? "MIDI " : "audio"),-9}  {Fit(instrument, 20)}  " +
                $"{(midi ? track.MidiSourceGain.ToString("0.000", CultureInfo.InvariantCulture) : "-"),6}   " +
                $"{Reason(stem, track, selection)}");
        }
    }

    private string Instrument(string stemName, SunoStem stem)
    {
        foreach (var instrument in options.StemInstruments)
        {
            if (string.Equals(instrument.StemName, stemName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(instrument.Path);
            }
        }

        return stem != null && stem.IsPercussion ? "percussion kit" : $"program {stem?.GmProgram}";
    }

    private static string Reason(SunoStem stem, PlayerTrack track, SunoStemSelection selection)
    {
        if (track.ActiveSource == TrackSource.Midi)
        {
            return "selected";
        }

        if (stem == null)
        {
            return "no stem of that name";
        }

        if (!track.HasMidiSource)
        {
            return stem.HasMidi ? "no instrument for it" : "no transcription";
        }

        if (selection.Names(stem.Name) == selection.NamesAreExcluded)
        {
            return selection.NamesAreExcluded ? "kept as the recording" : "not among the named stems";
        }

        if (stem.NoteCount < selection.MinimumNoteCount)
        {
            return $"only {stem.NoteCount} note(s)";
        }

        return $"coverage {stem.MidiCoverage:0.000}";
    }

    private bool Write(MultiTrackPlayer player, string path, TimeSpan duration)
    {
        var format = options.SixteenBit
            ? new WaveFormat(options.SampleRate, 16, 2)
            : WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, 2);

        var rendering = Stopwatch.StartNew();
        try
        {
            player.RenderToFile(path, format);
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            Console.WriteLine($"    could not write {path}: {error.Message}");
            return false;
        }

        rendering.Stop();

        var times = rendering.Elapsed.TotalSeconds > 0.0
            ? duration.TotalSeconds / rendering.Elapsed.TotalSeconds
            : 0.0;

        Console.WriteLine();
        Console.WriteLine(
            $"    wrote {Path.GetFileName(path)}  ({format.BitsPerSample}-bit, {format.SampleRate} Hz, " +
            $"{new FileInfo(path).Length / (1024 * 1024)} MB) in {rendering.Elapsed.TotalSeconds:0.0} s " +
            $"({times:0.#}x real time)");

        return true;
    }

    private static void ReportProblems(string what, IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
        {
            return;
        }

        Console.WriteLine($"    {problems.Count} thing(s) {what} could not honour:");
        foreach (var problem in problems)
        {
            Console.WriteLine($"      - {problem}");
        }
    }

    private static string Minutes(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private static string Fit(string text, int width)
    {
        if (text == null)
        {
            text = string.Empty;
        }

        return text.Length > width ? text.Substring(0, width) : text.PadRight(width);
    }

    // A folder or file name from something a user typed or a download was called. Anything that
    // is not plainly safe becomes an underscore rather than being guessed at.
    private static string SafeName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            safe[i] = char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '_'
                ? character
                : invalid.Contains(character) ? '_' : character;
        }

        var result = new string(safe).Trim();
        return result.Length == 0 ? "untitled" : result;
    }
}

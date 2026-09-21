using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeBrix.Audio.Playback.Suno;

namespace SunoInstrumentSwap;

/// <summary>
/// What the command line asked for, after it has been read and checked.
/// </summary>
/// <remarks>
/// Everything here comes from the arguments. Nothing about any particular song, instrument or
/// library is built in: the sample knows how to ask, and the user says what to ask about.
/// </remarks>
internal sealed class SwapOptions
{
    /// <summary>The stems zip or the folder holding the same files.</summary>
    internal string SourcePath { get; private set; }

    /// <summary>Where the renders are written. Required, and never inside this repository.</summary>
    internal string OutputFolder { get; private set; }

    /// <summary>Where a zip is extracted. Defaults to a folder beside the renders.</summary>
    internal string CacheFolder { get; private set; }

    /// <summary>The instrument libraries to render the song with, in the order given.</summary>
    internal List<string> LibraryNames { get; } = new List<string>();

    /// <summary>SoundFont files to register as instrument libraries: a name and a <c>.sf2</c> path.</summary>
    internal List<(string Name, string Path)> SoundFonts { get; } = new List<(string, string)>();

    /// <summary>Instruments for one named stem each: a stem name and an instrument file.</summary>
    internal List<(string StemName, string Path)> StemInstruments { get; } = new List<(string, string)>();

    /// <summary>The stem names to keep as the download's audio, whatever their transcription says.</summary>
    internal List<string> VocalStems { get; } = new List<string>();

    /// <summary>
    /// Whether <see cref="VocalStems"/> is this sample's guess rather than something the user
    /// stated. A guess is narrowed to the stems the song really has; a statement is passed
    /// through as written, so that a name the song lacks is reported rather than swallowed.
    /// </summary>
    internal bool VocalStemsAreTheDefault { get; private set; } = true;

    /// <summary>The note-count floor a transcription must clear to be played.</summary>
    internal int MinimumNoteCount { get; private set; } = SunoStemSelection.DefaultMinimumNoteCount;

    /// <summary>The coverage floor a transcription must clear to be played.</summary>
    internal double MinimumCoverage { get; private set; } = SunoStemSelection.DefaultMinimumMidiCoverage;

    /// <summary>The rate every render is produced at. A stems export is 48 kHz throughout.</summary>
    internal int SampleRate { get; private set; } = 48000;

    /// <summary>Whether the renders are 16-bit PCM rather than 32-bit float.</summary>
    internal bool SixteenBit { get; private set; } = true;

    /// <summary>Whether the user asked for the usage text instead of a run.</summary>
    internal bool WantsHelp { get; private set; }

    /// <summary>Reads the command line, or explains what is wrong with it.</summary>
    /// <param name="args">The raw arguments.</param>
    /// <param name="problem">Receives the reason this command line cannot be used, or null.</param>
    /// <returns>The options, or null when <paramref name="problem"/> was set.</returns>
    internal static SwapOptions Parse(string[] args, out string problem)
    {
        var options = new SwapOptions();
        problem = null;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            switch (argument)
            {
                case "--help":
                case "-h":
                    options.WantsHelp = true;
                    return options;

                case "--output":
                    if (!TryTake(args, ref i, "--output", out var output, out problem)) { return null; }
                    options.OutputFolder = output;
                    break;

                case "--cache":
                    if (!TryTake(args, ref i, "--cache", out var cache, out problem)) { return null; }
                    options.CacheFolder = cache;
                    break;

                case "--library":
                    if (!TryTake(args, ref i, "--library", out var library, out problem)) { return null; }
                    options.LibraryNames.Add(library);
                    break;

                case "--soundfont":
                    if (!TryTake(args, ref i, "--soundfont", out var soundFont, out problem)) { return null; }
                    if (!TrySplitPair(soundFont, "--soundfont", "<name>=<path to a .sf2>",
                            out var soundFontName, out var soundFontPath, out problem))
                    {
                        return null;
                    }

                    options.SoundFonts.Add((soundFontName, soundFontPath));
                    options.LibraryNames.Add(soundFontName);
                    break;

                case "--stem-instrument":
                    if (!TryTake(args, ref i, "--stem-instrument", out var stemInstrument, out problem)) { return null; }
                    if (!TrySplitPair(stemInstrument, "--stem-instrument", "<stem name>=<path to an instrument file>",
                            out var stemName, out var instrumentPath, out problem))
                    {
                        return null;
                    }

                    options.StemInstruments.Add((stemName, instrumentPath));
                    break;

                case "--vocal-stems":
                    if (!TryTake(args, ref i, "--vocal-stems", out var vocals, out problem)) { return null; }
                    options.VocalStems.Clear();
                    options.VocalStemsAreTheDefault = false;
                    foreach (var name in vocals.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        options.VocalStems.Add(name.Trim());
                    }

                    break;

                case "--min-notes":
                    if (!TryTake(args, ref i, "--min-notes", out var notes, out problem)) { return null; }
                    if (!int.TryParse(notes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var noteFloor)
                        || noteFloor < 0)
                    {
                        problem = $"--min-notes needs a number of notes, not '{notes}'.";
                        return null;
                    }

                    options.MinimumNoteCount = noteFloor;
                    break;

                case "--min-coverage":
                    if (!TryTake(args, ref i, "--min-coverage", out var coverage, out problem)) { return null; }
                    if (!double.TryParse(coverage, NumberStyles.Float, CultureInfo.InvariantCulture, out var coverageFloor)
                        || coverageFloor < 0.0 || coverageFloor > 1.0)
                    {
                        problem = $"--min-coverage needs a fraction from 0 to 1, not '{coverage}'.";
                        return null;
                    }

                    options.MinimumCoverage = coverageFloor;
                    break;

                case "--rate":
                    if (!TryTake(args, ref i, "--rate", out var rate, out problem)) { return null; }
                    if (!int.TryParse(rate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate)
                        || sampleRate <= 0)
                    {
                        problem = $"--rate needs a sample rate in Hz, not '{rate}'.";
                        return null;
                    }

                    options.SampleRate = sampleRate;
                    break;

                case "--float":
                    options.SixteenBit = false;
                    break;

                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        problem = $"'{argument}' is not an option this sample knows.";
                        return null;
                    }

                    if (options.SourcePath != null)
                    {
                        problem = "Give exactly one stems zip or folder.";
                        return null;
                    }

                    options.SourcePath = argument;
                    break;
            }
        }

        return Check(options, out problem) ? options : null;
    }

    /// <summary>The usage text, which is also the documentation a run prints on a bad argument.</summary>
    /// <returns>Every line of it.</returns>
    internal static string Usage() =>
        """
        SunoInstrumentSwap - render a stems export as it was downloaded, and again with its
        instruments swapped for an instrument library's.

          SunoInstrumentSwap <stems zip or folder> --output <folder> [options]

        REQUIRED
          <stems zip or folder>        the download, as the zip or as the extracted folder
          --output <folder>            where the renders go. It must be OUTSIDE this repository.

        OPTIONS
          --library <name>             an instrument library to render with, by its REGISTERED
                                       name; repeatable. Defaults to ModestSynthGm, which this
                                       sample registers for you and which needs no files.
          --soundfont <name>=<path>    register a .sf2 as an instrument library under <name> and
                                       render with it too; repeatable.
          --stem-instrument <stem>=<path>
                                       play ONE named stem with one instrument file - a
                                       .dspreset, .dslibrary, .dsbundle, a Decent Sampler library
                                       folder, a .sfz or a .sf2; repeatable.
          --vocal-stems <a,b>          which stems keep the download's audio whatever their
                                       transcription says. Default: Vocals,Backing Vocals.
          --min-notes <n>              the note-count floor a transcription must clear.
          --min-coverage <x>           the coverage floor a transcription must clear, 0 to 1.
          --rate <hz>                  the rate every render is produced at. Default 48000,
                                       because a stems export is 48 kHz throughout.
          --float                      write 32-bit float instead of 16-bit PCM.
          --cache <folder>             where a zip is extracted. Default: a folder beside the
                                       renders. Also outside this repository.
          --help                       this text.

        EXAMPLE
          SunoInstrumentSwap "~/Downloads/My Song Stems.zip" --output ~/renders \
              --library ModestSynthGm \
              --soundfont MyBank=~/SoundFonts/bank.sf2 \
              --stem-instrument "Synth=~/packs/Grand/Grand.dspreset"
        """;

    private static bool Check(SwapOptions options, out string problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(options.SourcePath))
        {
            problem = "Name the stems zip or the folder holding the stems.";
            return false;
        }

        if (!File.Exists(options.SourcePath) && !Directory.Exists(options.SourcePath))
        {
            problem = $"There is nothing at '{options.SourcePath}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.OutputFolder))
        {
            problem = "--output is required: this sample never writes into the repository, so it " +
                "has no sensible default to fall back on.";
            return false;
        }

        options.SourcePath = Path.GetFullPath(options.SourcePath);
        options.OutputFolder = Path.GetFullPath(options.OutputFolder);
        options.CacheFolder = string.IsNullOrWhiteSpace(options.CacheFolder)
            ? Path.Combine(options.OutputFolder, "stems-cache")
            : Path.GetFullPath(options.CacheFolder);

        if (options.LibraryNames.Count == 0)
        {
            options.LibraryNames.Add("ModestSynthGm");
        }

        if (options.VocalStems.Count == 0)
        {
            options.VocalStems.Add("Vocals");
            options.VocalStems.Add("Backing Vocals");
        }

        foreach (var soundFont in options.SoundFonts)
        {
            if (!File.Exists(soundFont.Path))
            {
                problem = $"There is no SoundFont at '{soundFont.Path}'.";
                return false;
            }
        }

        foreach (var instrument in options.StemInstruments)
        {
            if (!File.Exists(instrument.Path) && !Directory.Exists(instrument.Path))
            {
                problem = $"There is no instrument at '{instrument.Path}'.";
                return false;
            }
        }

        return true;
    }

    private static bool TryTake(string[] args, ref int index, string name, out string value, out string problem)
    {
        problem = null;
        value = null;

        if (index + 1 >= args.Length)
        {
            problem = $"{name} needs a value after it.";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TrySplitPair(
        string argument, string name, string shape, out string left, out string right, out string problem)
    {
        problem = null;
        left = null;
        right = null;

        var separator = argument.IndexOf('=');
        if (separator <= 0 || separator == argument.Length - 1)
        {
            problem = $"{name} takes {shape}; '{argument}' is not that shape.";
            return false;
        }

        left = argument.Substring(0, separator).Trim();
        right = argument.Substring(separator + 1).Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            problem = $"{name} takes {shape}; '{argument}' is not that shape.";
            return false;
        }

        return true;
    }
}

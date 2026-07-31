using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Tools.SfzOpcodeSurvey;

// Measures which SFZ opcodes real libraries actually use, so the scope of SFZ support is decided by
// counting rather than by guessing. See README.txt for what this is and how to run it.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("usage: sfz_opcode_survey <corpus-directory> [output-directory]");
            Console.WriteLine();
            Console.WriteLine("  Each immediate subdirectory of <corpus-directory> is treated as one LIBRARY.");
            Console.WriteLine("  Every .sfz under it is parsed, recursively.");
            return args.Length == 0 ? 1 : 0;
        }

        var corpusRoot = Path.GetFullPath(args[0]);
        if (!Directory.Exists(corpusRoot))
        {
            Console.Error.WriteLine($"error: corpus directory '{corpusRoot}' does not exist.");
            return 1;
        }

        var outputDirectory = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
        Directory.CreateDirectory(outputDirectory);

        var libraries = Survey(corpusRoot);
        if (libraries.Count == 0)
        {
            Console.Error.WriteLine($"error: no libraries with .sfz files found under '{corpusRoot}'.");
            return 1;
        }

        var report = new Report(libraries);
        Write(Path.Combine(outputDirectory, "opcodes.md"), report.BuildOpcodeTable());
        Write(Path.Combine(outputDirectory, "coverage.md"), report.BuildCoverageCurve());
        Write(Path.Combine(outputDirectory, "libraries.md"), report.BuildPerLibraryBreakdown());

        Console.WriteLine();
        Console.WriteLine(report.Summary());
        return 0;
    }

    private static void Write(string path, string content)
    {
        File.WriteAllText(path, content);
        Console.WriteLine($"wrote {path}");
    }

    private static List<Library> Survey(string corpusRoot)
    {
        var libraries = new List<Library>();

        foreach (var directory in Directory.GetDirectories(corpusRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var files = Directory.GetFiles(directory, "*.sfz", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                continue;
            }

            var library = new Library(Path.GetFileName(directory));

            // Parse everything first, and note which files are pulled in by another file's #include.
            //
            // This matters more than it looks. A file meant only to be included is written assuming
            // the root file's #define variables are in scope; parsed standalone, its opcode names
            // keep their $variables and every one looks like a brand new opcode. Left unfixed that
            // was 56% of the distinct opcode count in this corpus - pure noise. Counting only ROOT
            // files (those nothing else includes) removes it, and removes double-counting too.
            var parsedFiles = new List<(string Path, SfzFile File)>();
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var parsed = SfzParser.ParseFile(file);
                    parsedFiles.Add((Path.GetFullPath(file), parsed));
                    foreach (var includedFile in parsed.IncludedFiles)
                    {
                        included.Add(Path.GetFullPath(includedFile));
                    }
                }
                catch (Exception e)
                {
                    library.FailedFiles.Add($"{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
                }
            }

            foreach (var (path, parsed) in parsedFiles)
            {
                if (included.Contains(path))
                {
                    library.IncludedFileCount++;
                    continue;
                }

                library.FileCount++;
                library.RegionCount += parsed.Regions.Count();
                library.ProblemCount += parsed.Problems.Count;

                foreach (var section in parsed.Sections)
                {
                    foreach (var opcode in section.Opcodes)
                    {
                        var canonical = Canonical(opcode);
                        library.Occurrences.TryGetValue(canonical, out var count);
                        library.Occurrences[canonical] = count + 1;
                    }
                }
            }

            if (library.FileCount > 0)
            {
                libraries.Add(library);
                Console.WriteLine(
                    $"  {library.Name,-42} {library.FileCount,5} files  " +
                    $"{library.RegionCount,6} regions  {library.Occurrences.Count,4} distinct opcodes");
            }
        }

        return libraries;
    }

    // The unit an implementer actually implements. An indexed opcode is one feature however many
    // numbers it appears with, so volume_oncc11 and volume_oncc74 both canonicalise to volume_onccN.
    private static string Canonical(SfzOpcode opcode)
    {
        if (opcode.Index == null)
        {
            return opcode.Name;
        }

        if (opcode.Modulation != null)
        {
            return opcode.BaseName == opcode.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')
                ? opcode.BaseName + "N"
                : opcode.BaseName + "_" + opcode.Modulation + "N";
        }

        return opcode.BaseName + "_N";
    }
}

internal sealed class Library
{
    internal Library(string name) => Name = name;

    internal string Name { get; }
    internal int FileCount { get; set; }
    internal int IncludedFileCount { get; set; }
    internal int RegionCount { get; set; }
    internal int ProblemCount { get; set; }
    internal List<string> FailedFiles { get; } = [];
    internal Dictionary<string, int> Occurrences { get; } = new(StringComparer.Ordinal);
}

internal sealed class Report
{
    private readonly List<Library> _libraries;
    private readonly List<OpcodeStat> _ranked;

    internal Report(List<Library> libraries)
    {
        _libraries = libraries;

        var stats = new Dictionary<string, OpcodeStat>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            foreach (var pair in library.Occurrences)
            {
                if (!stats.TryGetValue(pair.Key, out var stat))
                {
                    stat = new OpcodeStat(pair.Key);
                    stats[pair.Key] = stat;
                }

                stat.LibraryCount++;
                stat.Occurrences += pair.Value;
                stat.Libraries.Add(library.Name);
            }
        }

        // THE COUNTING RULE: rank by how many LIBRARIES use an opcode, not by how many times it
        // occurs. One sprawling library with 10,000 regions would otherwise decide the whole ranking.
        // Raw occurrences are reported alongside, never used for the ordering.
        _ranked = stats.Values
            .OrderByDescending(s => s.LibraryCount)
            .ThenByDescending(s => s.Occurrences)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    internal string Summary()
    {
        var total = _libraries.Sum(l => l.RegionCount);
        return $"{_libraries.Count} libraries, {_libraries.Sum(l => l.FileCount)} .sfz files, " +
               $"{total} regions, {_ranked.Count} distinct opcodes.";
    }

    internal string BuildOpcodeTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SFZ opcode survey — every opcode found");
        sb.AppendLine();
        sb.AppendLine(Preamble());
        sb.AppendLine();
        sb.AppendLine("Ranked by the number of LIBRARIES using the opcode. Raw occurrence counts are shown");
        sb.AppendLine("alongside but never used for the ordering: an opcode used once in a library counts the");
        sb.AppendLine("same as one used ten thousand times, so a single sprawling library cannot decide the rank.");
        sb.AppendLine();
        sb.AppendLine("`N` in a name stands for an index — `volume_onccN` covers `volume_oncc11`, `volume_oncc74`");
        sb.AppendLine("and every other CC. That is the unit somebody actually implements.");
        sb.AppendLine();
        sb.AppendLine("| Rank | Opcode | Libraries | % of libraries | Occurrences |");
        sb.AppendLine("|-----:|--------|----------:|---------------:|------------:|");

        for (var i = 0; i < _ranked.Count; i++)
        {
            var stat = _ranked[i];
            var percent = 100.0 * stat.LibraryCount / _libraries.Count;
            sb.AppendLine(
                $"| {i + 1} | `{stat.Name}` | {stat.LibraryCount} | {percent.ToString("F1", CultureInfo.InvariantCulture)}% | {stat.Occurrences} |");
        }

        return sb.ToString();
    }

    internal string BuildCoverageCurve()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SFZ opcode survey — the coverage curve");
        sb.AppendLine();
        sb.AppendLine(Preamble());
        sb.AppendLine();
        sb.AppendLine("For each N: if the top N opcodes from `opcodes.md` were implemented, how many libraries");
        sb.AppendLine("would load with ZERO unimplemented opcodes. \"Fully supported\" is strict — every opcode the");
        sb.AppendLine("library uses must be implemented, not most of them. This is the number that answers");
        sb.AppendLine("\"what does near-full SFZ support actually require\", and it is counted rather than guessed.");
        sb.AppendLine();
        sb.AppendLine("| Top N opcodes | Libraries fully supported | % of libraries |");
        sb.AppendLine("|--------------:|--------------------------:|---------------:|");

        var implemented = new HashSet<string>(StringComparer.Ordinal);
        var previous = -1;

        for (var n = 1; n <= _ranked.Count; n++)
        {
            implemented.Add(_ranked[n - 1].Name);
            var supported = _libraries.Count(l => l.Occurrences.Keys.All(implemented.Contains));

            // One row per change of value, plus the final row: the interesting points are where the
            // curve steps, not 400 identical rows.
            if (supported != previous || n == _ranked.Count)
            {
                var percent = 100.0 * supported / _libraries.Count;
                sb.AppendLine($"| {n} | {supported} | {percent.ToString("F1", CultureInfo.InvariantCulture)}% |");
                previous = supported;
            }
        }

        sb.AppendLine();
        sb.AppendLine("## What each library still needs");
        sb.AppendLine();
        sb.AppendLine("Libraries that are close — an outlier one or two opcodes away from working is visible here");
        sb.AppendLine("rather than averaged away.");
        sb.AppendLine();

        var top = new HashSet<string>(_ranked.Take(50).Select(s => s.Name), StringComparer.Ordinal);
        sb.AppendLine("Taking the top 50 opcodes as implemented:");
        sb.AppendLine();
        sb.AppendLine("| Library | Unimplemented opcodes it uses | Which |");
        sb.AppendLine("|---------|------------------------------:|-------|");

        foreach (var library in _libraries.OrderBy(l => l.Occurrences.Keys.Count(k => !top.Contains(k))))
        {
            var missing = library.Occurrences.Keys.Where(k => !top.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var which = missing.Count == 0
                ? "—"
                : string.Join(", ", missing.Take(12).Select(m => "`" + m + "`")) + (missing.Count > 12 ? ", …" : "");
            sb.AppendLine($"| {library.Name} | {missing.Count} | {which} |");
        }

        return sb.ToString();
    }

    internal string BuildPerLibraryBreakdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SFZ opcode survey — per-library breakdown");
        sb.AppendLine();
        sb.AppendLine(Preamble());
        sb.AppendLine();
        sb.AppendLine("| Library | Root .sfz | Included | Regions | Distinct opcodes | Parse problems | Failed files |");
        sb.AppendLine("|---------|----------:|---------:|--------:|-----------------:|---------------:|-------------:|");

        foreach (var library in _libraries)
        {
            sb.AppendLine(
                $"| {library.Name} | {library.FileCount} | {library.IncludedFileCount} | {library.RegionCount} | " +
                $"{library.Occurrences.Count} | {library.ProblemCount} | {library.FailedFiles.Count} |");
        }

        sb.AppendLine();
        sb.AppendLine("`Parse problems` counts things the parser noted and carried on past — a missing `#include`,");
        sb.AppendLine("an opcode outside any header. None of them stop a file loading. `Failed files` counts files");
        sb.AppendLine("that could not be read at all, and should be zero.");

        foreach (var library in _libraries.Where(l => l.FailedFiles.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"### {library.Name} — files that failed");
            sb.AppendLine();
            foreach (var failure in library.FailedFiles)
            {
                sb.AppendLine($"- {failure}");
            }
        }

        return sb.ToString();
    }

    private string Preamble() =>
        $"Generated by `tools/sfz_opcode_survey` over {_libraries.Count} libraries, " +
        $"{_libraries.Sum(l => l.FileCount)} `.sfz` files, {_libraries.Sum(l => l.RegionCount)} regions.";

    private sealed class OpcodeStat
    {
        internal OpcodeStat(string name) => Name = name;

        internal string Name { get; }
        internal int LibraryCount { get; set; }
        internal int Occurrences { get; set; }
        internal HashSet<string> Libraries { get; } = new(StringComparer.Ordinal);
    }
}

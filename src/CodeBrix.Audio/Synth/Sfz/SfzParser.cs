using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// Reads SFZ files into their structure - headers, opcodes, <c>#define</c> variables and
/// <c>#include</c> files. It extracts what a file says; it does not render it.
/// </summary>
/// <remarks>
/// <para>
/// Two rules govern this parser, and they matter more than the number of opcodes it recognises:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>An unknown opcode is never fatal.</b> SFZ files routinely carry opcodes aimed at other players.
/// A file must load with whatever is understood, so unrecognised names are recorded and carried, not
/// rejected.
/// </description></item>
/// <item><description>
/// <b>What was skipped is reported.</b> Every opcode the caller does not implement can be listed from
/// the parsed result, which turns "this library sounds wrong" into a list of exactly which opcodes to
/// implement next.
/// </description></item>
/// </list>
/// <para>
/// The parser is deliberately permissive about layout, because real files are: opcodes and headers
/// share lines freely, values contain spaces (a sample path is the common case), and comments start
/// at <c>//</c>.
/// </para>
/// <para>This is CodeBrix code written from the SFZ specification; it is not part of the MeltySynth port.</para>
/// </remarks>
public static class SfzParser
{
    /// <summary>How deep <c>#include</c> may nest before the parser assumes a cycle and stops.</summary>
    public const int MaxIncludeDepth = 16;

    /// <summary>Parses an SFZ file from disk, following <c>#include</c> relative to its folder.</summary>
    /// <param name="path">Path to a <c>.sfz</c> file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static SfzFile ParseFile(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The SFZ file '{path}' was not found.", path);
        }

        var result = new SfzFile { Path = path };
        var text = ReadAllTextTolerant(path);
        var root = Path.GetDirectoryName(Path.GetFullPath(path));
        ParseInto(result, text, path, root, root, depth: 0);
        return result;
    }

    /// <summary>
    /// Parses SFZ text that is already in memory.
    /// </summary>
    /// <param name="text">The SFZ text.</param>
    /// <param name="baseDirectory">
    /// Folder that <c>#include</c> and <c>default_path</c> are relative to. When null, includes are
    /// recorded as problems rather than followed.
    /// </param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static SfzFile ParseText(string text, string baseDirectory = null)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var result = new SfzFile();
        ParseInto(result, text, sourceFile: null, baseDirectory, baseDirectory, depth: 0);
        return result;
    }

    private static void ParseInto(
        SfzFile result, string text, string sourceFile, string currentDirectory, string rootDirectory, int depth)
    {
        var lines = text.Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = StripComment(lines[lineIndex]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var lineNumber = lineIndex + 1;
            ParseLine(result, line, lineNumber, sourceFile, currentDirectory, rootDirectory, depth);
        }
    }

    // Handles a directive starting at `start`. Returns the index just past it, so the caller can
    // carry on scanning the rest of the line.
    private static int HandleDirective(
        SfzFile result, string line, int start, int lineNumber, string sourceFile,
        string currentDirectory, string rootDirectory, int depth)
    {
        if (StartsWithAt(line, start, "#define"))
        {
            // #define $VarName value  — two whitespace-delimited tokens.
            var i = start + "#define".Length;
            var name = ReadToken(line, ref i);
            var value = ReadToken(line, ref i);

            if (name.Length > 0 && value.Length > 0)
            {
                result.AddDefine(name.TrimStart('$'), value);
            }
            else
            {
                result.AddProblem($"{Where(sourceFile, lineNumber)}: malformed #define.");
            }

            return i;
        }

        if (StartsWithAt(line, start, "#include"))
        {
            var i = start + "#include".Length;
            var included = ReadQuotedOrToken(line, ref i);

            if (string.IsNullOrEmpty(included))
            {
                result.AddProblem($"{Where(sourceFile, lineNumber)}: malformed #include.");
                return i;
            }

            included = Substitute(result, included);

            if (depth >= MaxIncludeDepth)
            {
                result.AddProblem(
                    $"{Where(sourceFile, lineNumber)}: #include nested deeper than {MaxIncludeDepth}; " +
                    "assuming a cycle and not following it.");
                return i;
            }

            if (rootDirectory == null && currentDirectory == null)
            {
                result.AddProblem($"{Where(sourceFile, lineNumber)}: #include '{included}' not followed (no base directory).");
                return i;
            }

            // ARIA and sfizz resolve #include against the directory of the ROOT .sfz file, not the
            // file containing the directive, and real libraries are written that way - DrumGizmo's
            // kits include "../Data/x.txt" from files that are already inside Data/. Resolving
            // relative to the including file instead silently loses every region in such a library.
            // The including file's own directory is kept as a fallback for files written the other way.
            var resolved = ResolveRelative(rootDirectory, included);
            if (resolved == null || !File.Exists(resolved))
            {
                resolved = ResolveRelative(currentDirectory, included);
            }

            if (resolved == null || !File.Exists(resolved))
            {
                // Windows-authored libraries write #include paths with the wrong case as freely as
                // they do sample paths; on a case-sensitive file system that must not silently drop
                // every region the include carries. Same fallback the sample loader uses.
                var normalized = included.Replace('\\', '/');
                resolved = rootDirectory != null
                    ? SfzInstrument.ResolveCaseInsensitive(rootDirectory, normalized)
                    : null;
                if (resolved == null && currentDirectory != null && currentDirectory != rootDirectory)
                {
                    resolved = SfzInstrument.ResolveCaseInsensitive(currentDirectory, normalized);
                }
            }

            if (resolved == null || !File.Exists(resolved))
            {
                result.AddProblem($"{Where(sourceFile, lineNumber)}: #include '{included}' was not found.");
                return i;
            }

            result.AddIncludedFile(resolved);
            ParseInto(
                result,
                ReadAllTextTolerant(resolved),
                resolved,
                Path.GetDirectoryName(resolved),
                rootDirectory,
                depth + 1);
            return i;
        }

        // Some files carry other preprocessor-looking tokens; note them and skip just that token.
        var skip = start;
        var unknown = ReadToken(line, ref skip);
        result.AddProblem($"{Where(sourceFile, lineNumber)}: unrecognised directive '{unknown}'.");
        return skip;
    }

    private static bool StartsWithAt(string line, int index, string value) =>
        index + value.Length <= line.Length &&
        string.Compare(line, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static string ReadToken(string line, ref int i)
    {
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        var start = i;
        while (i < line.Length && !char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        return line.Substring(start, i - start);
    }

    private static string ReadQuotedOrToken(string line, ref int i)
    {
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        if (i < line.Length && line[i] == '"')
        {
            var close = line.IndexOf('"', i + 1);
            if (close < 0)
            {
                i = line.Length;
                return null;
            }

            var quoted = line.Substring(i + 1, close - i - 1);
            i = close + 1;
            return quoted;
        }

        return ReadToken(line, ref i);
    }

    // One line can hold several headers, opcodes AND directives, in any mixture:
    //   <region> #define $KEY 21 lokey=21 hikey=22 #include "Data/sample.txt"
    // is a real line from a real library. Treating directives as line-leading only turns the rest of
    // such a line into garbage opcodes named "#define $KEY 21 lokey", which is both wrong and, in a
    // survey, badly misleading.
    private static void ParseLine(
        SfzFile result, string line, int lineNumber, string sourceFile,
        string currentDirectory, string rootDirectory, int depth)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            if (line[i] == '#')
            {
                i = HandleDirective(
                    result, line, i, lineNumber, sourceFile, currentDirectory, rootDirectory, depth);
                continue;
            }

            if (line[i] == '<')
            {
                var close = line.IndexOf('>', i);
                if (close < 0)
                {
                    result.AddProblem($"{Where(sourceFile, lineNumber)}: unterminated header.");
                    return;
                }

                var name = line.Substring(i + 1, close - i - 1).Trim().ToLowerInvariant();
                result.AddSection(new SfzSection(HeaderKindOf(name), name, lineNumber, sourceFile));
                i = close + 1;
                continue;
            }

            // name=value, where the value runs to the next "name=" or to end of line. Sample paths
            // contain spaces, so splitting on whitespace alone would truncate them.
            var equals = line.IndexOf('=', i);
            if (equals < 0)
            {
                var stray = line.Substring(i).Trim();
                if (stray.Length > 0)
                {
                    result.AddProblem($"{Where(sourceFile, lineNumber)}: ignored stray text '{stray}'.");
                }
                return;
            }

            // $variables appear in opcode NAMES as well as values - amplitude_oncc$ch_hh and
            // amp_velcurve_$v11h are both real. Substituting only in values leaves the name as
            // written, which invents a distinct "opcode" per variable and wrecks any counting.
            var opcodeName = Substitute(result, line.Substring(i, equals - i).Trim()).ToLowerInvariant();
            var valueStart = equals + 1;
            var valueEnd = FindValueEnd(line, valueStart);
            var value = line.Substring(valueStart, valueEnd - valueStart).Trim();

            if (opcodeName.Length == 0)
            {
                result.AddProblem($"{Where(sourceFile, lineNumber)}: opcode with an empty name.");
            }
            else
            {
                var section = result.CurrentSection;
                if (section == null)
                {
                    result.AddProblem(
                        $"{Where(sourceFile, lineNumber)}: opcode '{opcodeName}' appears before any header; ignored.");
                }
                else
                {
                    section.Add(new SfzOpcode(opcodeName, Substitute(result, value), lineNumber));
                }
            }

            i = valueEnd;
        }
    }

    // A value ends where the next opcode begins. Scan forward for "<identifier>=" and back up to the
    // whitespace before it; everything up to there belongs to this value.
    private static int FindValueEnd(string line, int start)
    {
        for (var i = start; i < line.Length; i++)
        {
            if (line[i] == '<')
            {
                return i;
            }

            // A directive ends the value too: "<region> ... hikey=22 #include "x.txt"" must not
            // swallow the #include into hikey's value.
            if (line[i] == '#' && i > start && char.IsWhiteSpace(line[i - 1]))
            {
                return i;
            }

            if (line[i] != '=')
            {
                continue;
            }

            var nameStart = i;
            while (nameStart > start && !char.IsWhiteSpace(line[nameStart - 1]))
            {
                nameStart--;
            }

            if (nameStart > start)
            {
                return nameStart;
            }
        }

        return line.Length;
    }

    private static string Substitute(SfzFile file, string value)
    {
        if (value.IndexOf('$') < 0 || file.Defines.Count == 0)
        {
            return value;
        }

        // Longest names first: with $KEY and $KEY2 both defined, replacing $KEY first would turn
        // $KEY2 into the $KEY value with a stray 2 appended - silent value corruption.
        var builder = new StringBuilder(value);
        foreach (var pair in file.Defines.OrderByDescending(p => p.Key.Length))
        {
            builder.Replace("$" + pair.Key, pair.Value);
        }

        return builder.ToString();
    }

    private static SfzHeaderKind HeaderKindOf(string name)
    {
        switch (name)
        {
            case "region": return SfzHeaderKind.Region;
            case "group": return SfzHeaderKind.Group;
            case "global": return SfzHeaderKind.Global;
            case "master": return SfzHeaderKind.Master;
            case "control": return SfzHeaderKind.Control;
            case "curve": return SfzHeaderKind.Curve;
            case "effect": return SfzHeaderKind.Effect;
            case "sample": return SfzHeaderKind.Sample;
            case "midi": return SfzHeaderKind.Midi;
            default: return SfzHeaderKind.Unknown;
        }
    }

    private static string StripComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line.Substring(0, comment);
    }

    private static string ExtractQuoted(string text)
    {
        var first = text.IndexOf('"');
        if (first < 0)
        {
            return text.Length == 0 ? null : text;
        }

        var last = text.IndexOf('"', first + 1);
        return last < 0 ? null : text.Substring(first + 1, last - first - 1);
    }

    // SFZ files are written on Windows and use backslashes; they must still resolve elsewhere.
    private static string ResolveRelative(string baseDirectory, string relative)
    {
        try
        {
            var normalized = relative.Replace('\\', Path.DirectorySeparatorChar)
                                     .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(baseDirectory, normalized));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Real libraries are not consistently UTF-8; a stray byte in a comment must not fail the parse.
    // A UTF-8 byte-order mark is stripped explicitly: decoded, it becomes U+FEFF glued to the first
    // token, which would silently swallow a file's first header (and with it, default_path).
    private static string ReadAllTextTolerant(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes)
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static string Where(string sourceFile, int lineNumber) =>
        sourceFile == null ? $"line {lineNumber}" : $"{Path.GetFileName(sourceFile)}:{lineNumber}";
}

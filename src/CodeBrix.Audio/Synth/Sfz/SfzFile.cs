using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// A parsed SFZ file: its sections in file order, plus the <c>#define</c> variables and any problems
/// noticed while reading it.
/// </summary>
/// <remarks>
/// This is the structural result of parsing only - no rendering, and no opcode is interpreted beyond
/// splitting its name. Unknown opcodes are recorded rather than rejected, which is what lets this type
/// double as a survey tool over libraries written for other players.
/// </remarks>
public sealed class SfzFile
{
    private readonly List<SfzSection> _sections = [];
    private readonly Dictionary<string, string> _defines = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];
    private readonly List<string> _includedFiles = [];

    /// <summary>The path the file was read from, or <see langword="null"/> when parsed from text.</summary>
    public string Path { get; internal set; }

    /// <summary>Every section, in the order the headers appeared.</summary>
    public IReadOnlyList<SfzSection> Sections => _sections;

    /// <summary>The <c>#define</c> variables in scope at the end of parsing, keyed without the <c>$</c>.</summary>
    public IReadOnlyDictionary<string, string> Defines => _defines;

    /// <summary>Files pulled in by <c>#include</c>, in the order they were read.</summary>
    public IReadOnlyList<string> IncludedFiles => _includedFiles;

    /// <summary>
    /// Anything that looked wrong while reading: a missing include, an opcode outside any header, a
    /// malformed line. Never fatal - a file that reports problems has still been parsed.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The <c>&lt;region&gt;</c> sections - the ones that produce sound.</summary>
    public IEnumerable<SfzSection> Regions => _sections.Where(s => s.Kind == SfzHeaderKind.Region);

    /// <summary>The value of <c>default_path</c> if a <c>&lt;control&gt;</c> section set one.</summary>
    public string DefaultPath =>
        _sections.Where(s => s.Kind == SfzHeaderKind.Control)
                 .Select(s => s.Find("default_path"))
                 .LastOrDefault(o => o != null)?.Value;

    /// <summary>Every distinct opcode name used anywhere in the file, lower-cased.</summary>
    /// <returns>The opcode names, in no particular order.</returns>
    public IEnumerable<string> DistinctOpcodeNames() =>
        _sections.SelectMany(s => s.Opcodes).Select(o => o.Name).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Every distinct opcode name with its numeric index folded away, so <c>volume_oncc74</c> and
    /// <c>volume_oncc11</c> both count once as <c>volume</c>.
    /// </summary>
    /// <returns>The base names, in no particular order.</returns>
    public IEnumerable<string> DistinctBaseNames() =>
        _sections.SelectMany(s => s.Opcodes).Select(o => o.BaseName).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a region's opcodes against the scopes it inherits from, nearest first: the region
    /// itself, then its group, then master, then global.
    /// </summary>
    /// <param name="region">A section from <see cref="Regions"/>.</param>
    /// <returns>The effective opcodes for that region, keyed by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="region"/> is not a region of this file.</exception>
    public IReadOnlyDictionary<string, SfzOpcode> Resolve(SfzSection region)
    {
        if (region == null)
        {
            throw new ArgumentNullException(nameof(region));
        }

        var index = _sections.IndexOf(region);
        if (index < 0 || region.Kind != SfzHeaderKind.Region)
        {
            throw new ArgumentException("The section is not a region of this file.", nameof(region));
        }

        var resolved = new Dictionary<string, SfzOpcode>(StringComparer.OrdinalIgnoreCase);

        // Walk backwards collecting the enclosing scopes, then apply them outermost-first so that
        // the nearer scope overwrites the farther one.
        var scopes = new List<SfzSection>();
        var seenGroup = false;
        var seenMaster = false;

        for (var i = index - 1; i >= 0; i--)
        {
            var section = _sections[i];
            switch (section.Kind)
            {
                case SfzHeaderKind.Group when !seenGroup:
                    seenGroup = true;
                    scopes.Add(section);
                    break;
                case SfzHeaderKind.Master when !seenMaster:
                    seenMaster = true;
                    scopes.Add(section);
                    break;
                case SfzHeaderKind.Global:
                    scopes.Add(section);
                    i = 0; // global is the outermost scope; nothing before it applies
                    break;
            }
        }

        scopes.Reverse();
        scopes.Add(region);

        foreach (var scope in scopes)
        {
            foreach (var opcode in scope.Opcodes)
            {
                resolved[opcode.Name] = opcode;
            }
        }

        return resolved;
    }

    internal void AddSection(SfzSection section) => _sections.Add(section);

    internal void AddDefine(string name, string value) => _defines[name] = value;

    internal void AddProblem(string problem) => _problems.Add(problem);

    internal void AddIncludedFile(string path) => _includedFiles.Add(path);

    internal SfzSection CurrentSection => _sections.Count == 0 ? null : _sections[^1];

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Path ?? "<text>"}: {_sections.Count} sections, {Regions.Count()} regions";
}

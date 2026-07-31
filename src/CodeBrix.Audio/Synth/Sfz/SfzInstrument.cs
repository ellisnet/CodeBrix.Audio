using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// A playable SFZ instrument: the typed regions, their decoded samples, the modulation curves, and the
/// initial controller state, loaded once and shared by any number of synthesizers.
/// </summary>
/// <remarks>
/// <para>
/// This is the SFZ counterpart of <see cref="CodeBrix.Audio.Synth.SoundFont"/>: load it once - ideally
/// through an <see cref="SfzInstrumentCache"/> - and hand the same instance to every
/// <see cref="SfzSynthesizer"/> that plays it. The instrument is immutable after loading and safe to
/// share across threads; all playback state lives in the synthesizer.
/// </para>
/// <para>
/// Samples are decoded eagerly at load, so memory follows the library: a small instrument costs
/// megabytes, a large sampled piano costs what its samples cost as 32-bit float. Sample files referenced
/// by more than one region are decoded once.
/// </para>
/// <para>
/// Loading is deliberately tolerant, matching the parser: a region whose sample file is missing or
/// undecodable is kept but silent, and the failure is recorded in <see cref="Problems"/>. Opcodes the
/// engine does not implement are reported in <see cref="UnsupportedOpcodes"/> (and once per name to the
/// Debug listener), never treated as errors.
/// </para>
/// </remarks>
public sealed class SfzInstrument
{
    private readonly List<SfzRegion> _regions = new List<SfzRegion>();
    private readonly Dictionary<SfzRegion, SfzSampleData> _samplesByRegion = new Dictionary<SfzRegion, SfzSampleData>();
    private readonly Dictionary<int, SfzCurve> _fileCurves = new Dictionary<int, SfzCurve>();
    private readonly Dictionary<int, float> _initialControllers = new Dictionary<int, float>();
    private readonly Dictionary<int, string> _controllerLabels = new Dictionary<int, string>();
    private readonly List<string> _problems = new List<string>();
    private readonly SortedSet<string> _unsupportedOpcodes = new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Loads an SFZ instrument from a file, decoding every referenced sample.
    /// </summary>
    /// <param name="path">Path to the <c>.sfz</c> file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public SfzInstrument(string path) : this(SfzParser.ParseFile(path), null)
    {
    }

    /// <summary>
    /// Builds an instrument from an already-parsed file. Sample paths resolve against
    /// <paramref name="sampleDirectory"/> when given, otherwise against the directory of the parsed
    /// file's <see cref="SfzFile.Path"/>.
    /// </summary>
    /// <param name="file">The parsed SFZ structure.</param>
    /// <param name="sampleDirectory">
    /// The directory sample paths are relative to, for files parsed from text. Optional when the file
    /// was parsed from disk.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentException">No sample directory is available.</exception>
    public SfzInstrument(SfzFile file, string sampleDirectory)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        Path = file.Path;
        Name = file.Path == null
            ? "sfz"
            : System.IO.Path.GetFileNameWithoutExtension(file.Path);

        var baseDirectory = sampleDirectory;
        if (baseDirectory == null && file.Path != null)
        {
            baseDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file.Path));
        }

        if (baseDirectory == null)
        {
            throw new ArgumentException(
                "A sample directory is required for an SFZ parsed from text.", nameof(sampleDirectory));
        }

        foreach (var problem in file.Problems)
        {
            _problems.Add(problem);
        }

        CollectControlState(file);
        CollectCurves(file);
        CollectUnsupportedOpcodes(file);

        var defaultPath = file.DefaultPath;
        var samplesByFile = new Dictionary<string, SfzSampleData>(StringComparer.Ordinal);

        foreach (var section in file.Regions)
        {
            var region = SfzRegion.FromResolved(section, file.Resolve(section));
            region.Index = _regions.Count;
            _regions.Add(region);

            if (region.IsDisabled || region.Sample == null)
            {
                continue;
            }

            if (region.Sample.StartsWith("*", StringComparison.Ordinal))
            {
                // Built-in generator samples (*sine, *noise) are an ARIA extension this engine
                // does not provide.
                _problems.Add($"{Name}: generator sample not supported: {region.Sample} (line {region.LineNumber})");
                continue;
            }

            var resolved = ResolveSamplePath(baseDirectory, defaultPath, region.Sample);
            if (resolved == null)
            {
                _problems.Add($"{Name}: sample not found: {region.Sample} (line {region.LineNumber})");
                continue;
            }

            if (!samplesByFile.TryGetValue(resolved, out var sample))
            {
                try
                {
                    sample = SfzSampleData.Load(resolved);
                }
                catch (Exception exception)
                {
                    _problems.Add($"{Name}: sample failed to decode: {region.Sample} ({exception.Message})");
                    continue;
                }

                samplesByFile[resolved] = sample;
            }

            _samplesByRegion[region] = sample;
            ResolveLoopDefaults(region, sample);
        }
    }

    /// <summary>The path the instrument was loaded from, or <see langword="null"/> for text parses.</summary>
    public string Path { get; }

    /// <summary>The instrument's display name - the file name without its extension.</summary>
    public string Name { get; }

    /// <summary>Every region of the instrument, in file order, including disabled or sample-less ones.</summary>
    public IReadOnlyList<SfzRegion> Regions => _regions;

    /// <summary>
    /// Initial controller values from <c>set_ccN</c> and <c>set_hd_ccN</c>, normalized to 0..1, applied
    /// by a synthesizer at reset.
    /// </summary>
    public IReadOnlyDictionary<int, float> InitialControllers => _initialControllers;

    /// <summary>Controller display names from <c>label_ccN</c>.</summary>
    public IReadOnlyDictionary<int, string> ControllerLabels => _controllerLabels;

    /// <summary>
    /// Everything that went wrong while loading, none of it fatal: parse problems, missing sample
    /// files, samples that failed to decode. An instrument with problems still plays what loaded.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// The canonical names of opcodes the file uses that this engine does not implement, sorted. The
    /// same list is written to the Debug listener at load, once per name - the field diagnostic for
    /// "this library does not sound right".
    /// </summary>
    public IReadOnlyCollection<string> UnsupportedOpcodes => _unsupportedOpcodes;

    /// <summary>
    /// Resolves a curve index: curves defined by the file's <c>&lt;curve&gt;</c> headers first, then the
    /// built-in curves 0-6, then linear for anything undefined.
    /// </summary>
    /// <param name="index">The curve index from a modulation.</param>
    /// <returns>The curve. Never null.</returns>
    public SfzCurve GetCurve(int index)
    {
        if (_fileCurves.TryGetValue(index, out var fileCurve))
        {
            return fileCurve;
        }

        switch (index)
        {
            case 1: return SfzCurve.Bipolar;
            case 2: return SfzCurve.Inverted;
            case 3: return SfzCurve.BipolarInverted;
            case 4: return SfzCurve.Concave;
            case 5: return SfzCurve.PowerIn;
            case 6: return SfzCurve.PowerOut;
            default: return SfzCurve.Linear;
        }
    }

    /// <summary>The decoded sample for a region, or null when the region has none.</summary>
    internal SfzSampleData GetSampleData(SfzRegion region) =>
        _samplesByRegion.TryGetValue(region, out var sample) ? sample : null;

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: {_regions.Count} regions";

    private void CollectControlState(SfzFile file)
    {
        foreach (var section in file.Sections)
        {
            if (section.Kind != SfzHeaderKind.Control)
            {
                continue;
            }

            foreach (var opcode in section.Opcodes)
            {
                if (opcode.Index == null)
                {
                    continue;
                }

                var cc = opcode.Index.Value;
                if (cc < 0 || cc > 127)
                {
                    continue;
                }

                switch (opcode.BaseName)
                {
                    case "set_hd_cc" when opcode.Modulation == null:
                    case "set_hd" when opcode.Modulation == "cc":
                        _initialControllers[cc] = Math.Clamp(opcode.AsFloat(), 0f, 1f);
                        break;

                    case "set_cc" when opcode.Modulation == null:
                    case "set" when opcode.Modulation == "cc":
                        _initialControllers[cc] = Math.Clamp(opcode.AsInt() / 127f, 0f, 1f);
                        break;

                    case "label_cc" when opcode.Modulation == null:
                    case "label" when opcode.Modulation == "cc":
                        _controllerLabels[cc] = opcode.Value;
                        break;
                }
            }
        }
    }

    private void CollectCurves(SfzFile file)
    {
        foreach (var section in file.Sections)
        {
            if (section.Kind != SfzHeaderKind.Curve)
            {
                continue;
            }

            var indexOpcode = section.Find("curve_index");
            if (indexOpcode == null)
            {
                _problems.Add($"{Name}: <curve> without curve_index (line {section.LineNumber})");
                continue;
            }

            var vertices = new Dictionary<int, float>();
            foreach (var opcode in section.Opcodes)
            {
                if (opcode.BaseName == "v" && opcode.Index.HasValue)
                {
                    vertices[opcode.Index.Value] = opcode.AsFloat();
                }
            }

            _fileCurves[indexOpcode.AsInt()] = SfzCurve.FromVertices(vertices);
        }
    }

    private void CollectUnsupportedOpcodes(SfzFile file)
    {
        foreach (var section in file.Sections)
        {
            foreach (var opcode in section.Opcodes)
            {
                if (SfzSupportedOpcodes.IsSupported(opcode))
                {
                    continue;
                }

                var canonical = SfzSupportedOpcodes.CanonicalNameOf(opcode);
                if (_unsupportedOpcodes.Add(canonical))
                {
                    Debug.WriteLine($"CodeBrix.Audio SFZ: opcode not implemented: {canonical} ({Name})");
                }
            }
        }
    }

    // Unset loop behaviour follows the sample: files with embedded loop points loop continuously by
    // default, files without play straight through. Unset loop points fall back to the embedded ones,
    // then to the whole sample.
    private static void ResolveLoopDefaults(SfzRegion region, SfzSampleData sample)
    {
        if (region.LoopMode == null)
        {
            region.LoopMode = sample.HasEmbeddedLoop ? SfzLoopMode.Continuous : SfzLoopMode.NoLoop;
        }

        if (region.LoopMode == SfzLoopMode.Continuous || region.LoopMode == SfzLoopMode.Sustain)
        {
            region.LoopStart ??= sample.EmbeddedLoopStart ?? 0;
            region.LoopEnd ??= sample.EmbeddedLoopEnd ?? (sample.Frames - 1);
        }
    }

    // Sample opcodes are written with Windows separators and, on a case-sensitive file system, often
    // with the wrong case - libraries are authored on Windows. Resolve exactly first, then walk the
    // path case-insensitively so those libraries load on Linux too.
    private static string ResolveSamplePath(string baseDirectory, string defaultPath, string sample)
    {
        var relative = sample.Replace('\\', '/').Trim();
        if (!string.IsNullOrEmpty(defaultPath))
        {
            var prefix = defaultPath.Replace('\\', '/').Trim();
            relative = prefix.Length == 0 || prefix.EndsWith("/", StringComparison.Ordinal)
                ? prefix + relative
                : prefix + "/" + relative;
        }

        var exact = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, relative));
        if (File.Exists(exact))
        {
            return exact;
        }

        return ResolveCaseInsensitive(baseDirectory, relative);
    }

    // Shared with SfzParser's #include resolution, which faces the same Windows-authored paths.
    internal static string ResolveCaseInsensitive(string baseDirectory, string relative)
    {
        var current = baseDirectory;
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = System.IO.Path.GetDirectoryName(current);
                if (current == null)
                {
                    return null;
                }
                continue;
            }

            var isLast = i == segments.Length - 1;
            var candidate = System.IO.Path.Combine(current, segment);

            if (isLast ? File.Exists(candidate) : Directory.Exists(candidate))
            {
                current = candidate;
                continue;
            }

            string match = null;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (string.Equals(System.IO.Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        match = entry;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            if (match == null)
            {
                return null;
            }

            current = match;
        }

        return File.Exists(current) ? current : null;
    }
}

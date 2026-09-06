using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// A parsed <c>.dspreset</c> document: the whole <c>&lt;DecentSampler&gt;</c> element and everything
/// beneath it, with nothing interpreted and nothing discarded.
/// </summary>
/// <remarks>
/// <para>
/// This is the structural result of parsing only. No file is opened, no sample is decoded and no
/// attribute is resolved against the level above it: for a playable instrument, load a
/// <see cref="DecentSamplerInstrument"/>, which builds on this. For the effective per-zone values, call
/// <see cref="ResolveGroups"/>.
/// </para>
/// <para>
/// A preset that reports problems has still been parsed. Unknown attributes and unknown child elements
/// are kept on the element they were written on, and each distinct name is listed once in
/// <see cref="Problems"/> - the same tolerance the SFZ parser has, and the reason this type doubles as
/// the survey tool's reader.
/// </para>
/// </remarks>
public sealed class DecentSamplerPreset : DecentSamplerElement
{
    private readonly List<string> _problems = [];

    /// <summary>The path the preset was read from, or null when it was parsed from text or a stream.</summary>
    public string Path { get; internal set; }

    /// <summary>
    /// A short name for the preset: the file name without its extension, or <c>dspreset</c> when it
    /// came from text.
    /// </summary>
    public string Name { get; internal set; } = "dspreset";

    /// <summary>
    /// The lowest version of the reference player the preset needs (<c>minVersion</c>), as written.
    /// </summary>
    public string MinVersion { get; internal set; }

    /// <summary>The preset format revision the author wrote (<c>pluginVersion</c>), as written.</summary>
    public string PluginVersion { get; internal set; }

    /// <summary>The <c>&lt;groups&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerGroupsElement Groups { get; internal set; }

    /// <summary>The instrument-level <c>&lt;effects&gt;</c> chain, or null when the preset has none.</summary>
    public DecentSamplerEffectsElement Effects { get; internal set; }

    /// <summary>The <c>&lt;buses&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerBusesElement Buses { get; internal set; }

    /// <summary>The <c>&lt;midi&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerMidiElement Midi { get; internal set; }

    /// <summary>The <c>&lt;modulators&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerModulatorsElement Modulators { get; internal set; }

    /// <summary>The <c>&lt;noteSequences&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerNoteSequencesElement NoteSequences { get; internal set; }

    /// <summary>The <c>&lt;arpeggiator&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerArpeggiator Arpeggiator { get; internal set; }

    /// <summary>The <c>&lt;tags&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerTagsElement Tags { get; internal set; }

    /// <summary>The <c>&lt;ui&gt;</c> element, or null when the preset has none.</summary>
    public DecentSamplerUi Ui { get; internal set; }

    /// <summary>
    /// Everything that looked wrong while reading, none of it fatal: unknown elements and attributes,
    /// values that could not be read, halves of a controller range with no partner. Each distinct
    /// unknown name appears once.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Every unknown attribute found anywhere in the preset, in document order.</summary>
    /// <returns>The attributes.</returns>
    public IEnumerable<DecentSamplerUnknownAttribute> AllUnknownAttributes() =>
        Descendants().SelectMany(element => element.UnknownAttributes);

    /// <summary>Every unknown child element found anywhere in the preset, in document order.</summary>
    /// <returns>The elements.</returns>
    public IEnumerable<DecentSamplerUnknownElement> AllUnknownElements() =>
        Descendants().SelectMany(element => element.UnknownElements);

    /// <summary>
    /// Resolves the inheritance chain <c>&lt;groups&gt;</c> to <c>&lt;group&gt;</c> to
    /// <c>&lt;sample&gt;</c> or <c>&lt;oscillator&gt;</c> into runtime groups and zones whose values are
    /// effective: the nearest written value, or the documented default.
    /// </summary>
    /// <remarks>
    /// No file is touched. The result is a fresh snapshot each call, so a caller may keep it, mutate it
    /// or throw it away without affecting the preset.
    /// </remarks>
    /// <returns>The groups, in document order. Empty when the preset declares none.</returns>
    public IReadOnlyList<DecentSamplerGroup> ResolveGroups() => DecentSamplerGroup.Resolve(this);

    internal void AddProblem(string problem) => _problems.Add(problem);

    internal void AddProblems(IEnumerable<string> problems) => _problems.AddRange(problems);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Name}: {Groups?.Groups.Count ?? 0} groups, {Problems.Count} problems";
}

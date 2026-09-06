using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

/// <summary>
/// One entry of a library's preset browsing menu: either a named category holding more entries, or one
/// preset file.
/// </summary>
/// <remarks>
/// The menu describes how a library wants its presets grouped, independently of where the files sit on
/// disk. A preset that is not mentioned still exists; the reference player lists it at the top level.
/// </remarks>
public sealed class DecentSamplerPresetMenuEntry
{
    private readonly List<DecentSamplerPresetMenuEntry> _children = [];

    private DecentSamplerPresetMenuEntry(string name, string file, bool isMenu)
    {
        Name = name;
        File = file;
        IsMenu = isMenu;
    }

    /// <summary>The category's display name, or null for a preset entry.</summary>
    public string Name { get; }

    /// <summary>
    /// The preset file, relative to the library's top-level folder, or null for a category.
    /// </summary>
    public string File { get; }

    /// <summary>Whether this entry is a category rather than a preset.</summary>
    public bool IsMenu { get; }

    /// <summary>The entries inside this category, in document order. Empty for a preset entry.</summary>
    public IReadOnlyList<DecentSamplerPresetMenuEntry> Children => _children;

    internal List<DecentSamplerPresetMenuEntry> MutableChildren => _children;

    internal static DecentSamplerPresetMenuEntry CreateMenu(string name) =>
        new DecentSamplerPresetMenuEntry(name, null, true);

    internal static DecentSamplerPresetMenuEntry CreatePreset(string file) =>
        new DecentSamplerPresetMenuEntry(null, file, false);

    /// <inheritdoc/>
    public override string ToString() => IsMenu ? $"menu \"{Name}\" ({_children.Count})" : $"preset {File}";
}

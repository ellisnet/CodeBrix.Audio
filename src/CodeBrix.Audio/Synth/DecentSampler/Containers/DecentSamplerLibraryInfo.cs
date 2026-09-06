using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

/// <summary>
/// The contents of a library's optional <c>DSLibraryInfo.xml</c> sidecar: its display name, version,
/// cover art, store product identifier and preset browsing menu.
/// </summary>
/// <remarks>
/// <para>
/// The file is optional, everything in it is optional, and a library without one behaves exactly as it
/// always has. It sits at the top of a library folder, beside the <c>Presets/</c> and <c>Samples/</c>
/// folders rather than inside any preset.
/// </para>
/// <para>
/// A <see cref="ProductId"/> ties the library to a purchase through the reference player's own store.
/// This engine reads it, reports it, and does nothing else with it: there is no activation here, and a
/// store-tied library plays exactly as any other does as far as the files on disk allow.
/// </para>
/// </remarks>
public sealed class DecentSamplerLibraryInfo
{
    private readonly List<string> _problems = [];
    private readonly List<DecentSamplerPresetMenuEntry> _presetMenu = [];

    private DecentSamplerLibraryInfo()
    {
    }

    /// <summary>The path the sidecar was read from, or null when it was read from a container.</summary>
    public string Path { get; private set; }

    /// <summary>The library's display name, or null when it names none.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The store product identifier, or null. Its presence means the library is distributed through
    /// the reference player's store; this engine never activates anything.
    /// </summary>
    public string ProductId { get; private set; }

    /// <summary>Whether the library declares a store product identifier.</summary>
    public bool IsStoreTied => !string.IsNullOrEmpty(ProductId);

    /// <summary>The library's own version string, or null when it declares none.</summary>
    public string Version { get; private set; }

    /// <summary>The cover art image, relative to the sidecar's folder, or null.</summary>
    public string CoverArt { get; private set; }

    /// <summary>The top level of the preset browsing menu, in document order. Empty when none.</summary>
    public IReadOnlyList<DecentSamplerPresetMenuEntry> PresetMenu => _presetMenu;

    /// <summary>Anything that looked wrong while reading the sidecar. Never fatal.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Reads a <c>DSLibraryInfo.xml</c> from a file.</summary>
    /// <param name="path">The sidecar file.</param>
    /// <returns>The library information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DecentSamplerParseException">The file is not well-formed XML.</exception>
    public static DecentSamplerLibraryInfo Load(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The library information file does not exist.", path);
        }

        using (var stream = File.OpenRead(path))
        {
            var info = Load(stream, path);
            return info;
        }
    }

    /// <summary>Reads a <c>DSLibraryInfo.xml</c> from a stream.</summary>
    /// <param name="stream">The stream, positioned at the start of the document.</param>
    /// <param name="path">The path the stream came from, used in problem messages. Optional.</param>
    /// <returns>The library information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="DecentSamplerParseException">The stream is not well-formed XML.</exception>
    public static DecentSamplerLibraryInfo Load(Stream stream, string path = null)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        XDocument document;
        try
        {
            document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new DecentSamplerParseException(
                "The library information file is not well-formed XML.", path,
                exception.LineNumber, exception.LinePosition, exception);
        }

        var info = new DecentSamplerLibraryInfo { Path = path };
        var root = document.Root;

        if (root == null)
        {
            info._problems.Add("DSLibraryInfo.xml is empty");
            return info;
        }

        if (root.Name.LocalName != "DecentSamplerLibraryInfo")
        {
            info._problems.Add(
                $"DSLibraryInfo.xml: the root element is <{root.Name.LocalName}>, " +
                "not <DecentSamplerLibraryInfo>; it is read as one anyway");
        }

        info.Name = Trimmed(root, "name");
        info.ProductId = Trimmed(root, "productId");
        info.Version = Trimmed(root, "version");
        info.CoverArt = Trimmed(root, "coverArt");

        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration &&
                !DecentSamplerSupportedFeatures.IsAttribute("DecentSamplerLibraryInfo", attribute.Name.LocalName))
            {
                info._problems.Add(
                    $"DSLibraryInfo.xml: attribute not recognised: DecentSamplerLibraryInfo@{attribute.Name.LocalName}");
            }
        }

        if (info.IsStoreTied)
        {
            info._problems.Add(
                $"DSLibraryInfo.xml declares productId '{info.ProductId}': this library is distributed " +
                "through the Decent Samples store. CodeBrix.Audio reads the files as they are and never " +
                "activates a product.");
        }

        foreach (var child in root.Elements())
        {
            if (child.Name.LocalName == "presetMenu")
            {
                ReadMenuChildren(child, info._presetMenu, info);
            }
            else
            {
                info._problems.Add(
                    $"DSLibraryInfo.xml: element not recognised: <{child.Name.LocalName}> " +
                    "under <DecentSamplerLibraryInfo>");
            }
        }

        return info;
    }

    private static void ReadMenuChildren(
        XElement element, List<DecentSamplerPresetMenuEntry> target, DecentSamplerLibraryInfo info)
    {
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "menu":
                {
                    var entry = DecentSamplerPresetMenuEntry.CreateMenu(Trimmed(child, "name"));
                    ReadMenuChildren(child, entry.MutableChildren, info);
                    target.Add(entry);
                    break;
                }

                case "preset":
                {
                    var file = Trimmed(child, "file");
                    if (file == null)
                    {
                        info._problems.Add("DSLibraryInfo.xml: <preset> has no file attribute; it is skipped");
                        break;
                    }

                    target.Add(DecentSamplerPresetMenuEntry.CreatePreset(file));
                    break;
                }

                default:
                    info._problems.Add(
                        $"DSLibraryInfo.xml: element not recognised: <{child.Name.LocalName}> " +
                        $"under <{element.Name.LocalName}>");
                    break;
            }
        }
    }

    private static string Trimmed(XElement element, string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        return attribute?.Value.Trim();
    }

    /// <inheritdoc/>
    public override string ToString() =>
        (Name ?? "DSLibraryInfo") + (Version == null ? string.Empty : " " + Version);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

/// <summary>
/// A preset that lives in a folder, with its <c>Samples/</c> and <c>Resources/</c> folders beside it.
/// </summary>
public sealed class DecentSamplerFolderContainer : DecentSamplerContainer
{
    /// <summary>Opens a folder as a container.</summary>
    /// <param name="baseDirectory">The folder a preset's relative paths are resolved against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseDirectory"/> is null.</exception>
    public DecentSamplerFolderContainer(string baseDirectory)
    {
        if (baseDirectory == null)
        {
            throw new ArgumentNullException(nameof(baseDirectory));
        }

        BaseDirectory = Path.GetFullPath(baseDirectory);
    }

    /// <summary>The folder relative paths are resolved against.</summary>
    public string BaseDirectory { get; }

    /// <inheritdoc/>
    public override string Description => BaseDirectory;

    /// <inheritdoc/>
    public override bool IsArchive => false;

    /// <inheritdoc/>
    public override bool TryResolve(string relativePath, out string key)
    {
        key = null;

        var segments = NormalizeSegments(relativePath);
        if (segments == null || segments.Count == 0)
        {
            return false;
        }

        var joined = string.Join("/", segments);
        var exact = Path.GetFullPath(Path.Combine(BaseDirectory, joined.Replace('/', Path.DirectorySeparatorChar)));

        if (File.Exists(exact))
        {
            key = exact;
            return true;
        }

        // Libraries authored on a case-insensitive file system routinely disagree with themselves
        // about capitalisation, so walk the path a segment at a time and match without case.
        var resolved = CodeBrix.Audio.Synth.Sfz.SfzInstrument.ResolveCaseInsensitive(BaseDirectory, joined);
        if (resolved == null)
        {
            return false;
        }

        key = resolved;
        return true;
    }

    /// <inheritdoc/>
    public override Stream OpenFile(string key) => File.OpenRead(key);

    /// <inheritdoc/>
    public override long SizeOf(string key)
    {
        try
        {
            return new FileInfo(key).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public override string CacheKeyFor(string key) => key;

    /// <inheritdoc/>
    public override IReadOnlyList<string> FindPresets()
    {
        try
        {
            return Directory
                .EnumerateFiles(BaseDirectory, "*.dspreset", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Looks for a <c>DSLibraryInfo.xml</c> beside the preset and in the folders above it, stopping
    /// after the given number of levels.
    /// </summary>
    /// <param name="levels">How many parent folders to try. Three covers a <c>Presets/</c> layout.</param>
    /// <returns>The full path of the sidecar, or null when none was found.</returns>
    public string FindLibraryInfo(int levels = 3)
    {
        var directory = BaseDirectory;

        for (var level = 0; level <= levels && directory != null; level++)
        {
            var candidate = Path.Combine(directory, "DSLibraryInfo.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var resolved = CodeBrix.Audio.Synth.Sfz.SfzInstrument.ResolveCaseInsensitive(
                directory, "DSLibraryInfo.xml");
            if (resolved != null)
            {
                return resolved;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        // A folder holds nothing open.
    }
}

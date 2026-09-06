using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

/// <summary>
/// Where a preset's files come from: a folder on disk, or a <c>.dslibrary</c> or <c>.dsbundle</c>
/// archive read in place.
/// </summary>
/// <remarks>
/// <para>
/// Every path a preset writes - a sample, an impulse response, a wavetable, an image - is relative to
/// the preset itself, and every one of them is resolved through this abstraction, so the two container
/// kinds behave alike from the engine's point of view.
/// </para>
/// <para>
/// Resolution is case-insensitive on purpose. Libraries are authored on Windows and macOS, where the
/// file system does not care, and they routinely disagree with themselves about capitalisation; on a
/// case-sensitive file system the exact match is tried first and a case-insensitive walk second, the
/// same rule the SFZ engine follows.
/// </para>
/// </remarks>
public abstract class DecentSamplerContainer : IDisposable
{
    /// <summary>
    /// Opens whatever a path points at: a <c>.dspreset</c> file (its folder becomes the container), a
    /// folder, or a <c>.dslibrary</c> or <c>.dsbundle</c> archive.
    /// </summary>
    /// <param name="path">The preset, folder or archive.</param>
    /// <returns>The container. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at that path.</exception>
    /// <exception cref="InvalidDataException">An archive is not a readable zip.</exception>
    public static DecentSamplerContainer Open(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (Directory.Exists(path))
        {
            return new DecentSamplerFolderContainer(path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Nothing exists at that path.", path);
        }

        var extension = Path.GetExtension(path);

        if (string.Equals(extension, ".dslibrary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dsbundle", StringComparison.OrdinalIgnoreCase))
        {
            return new DecentSamplerArchiveContainer(path);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return new DecentSamplerFolderContainer(directory ?? ".");
    }

    /// <summary>A short description of where the files come from, used in problem messages.</summary>
    public abstract string Description { get; }

    /// <summary>
    /// Whether the container is an archive read in place rather than a folder on disk.
    /// </summary>
    public abstract bool IsArchive { get; }

    /// <summary>
    /// Resolves a path written by a preset - relative to the preset's own folder - to a key this
    /// container can open.
    /// </summary>
    /// <param name="relativePath">
    /// The path as the preset wrote it. Backslashes are accepted and treated as separators.
    /// </param>
    /// <param name="key">The resolved key, or null when nothing matched.</param>
    /// <returns><see langword="true"/> when the file exists in this container.</returns>
    public abstract bool TryResolve(string relativePath, out string key);

    /// <summary>Opens a resolved file for reading.</summary>
    /// <param name="key">A key from <see cref="TryResolve"/>.</param>
    /// <returns>A readable, seekable stream the caller owns.</returns>
    public abstract Stream OpenFile(string key);

    // Opens a resolved file for STREAMING playback: a stream that may be re-read and seeked over the
    // life of an instrument, rather than the one-shot fully-buffered stream OpenFile hands out. A
    // folder container's answer is a plain FileStream; an archive's is a re-openable entry view that
    // never holds the whole entry in memory. The default is OpenFile, so a container that has nothing
    // better to offer still streams correctly.
    internal virtual Stream OpenStreamingFile(string key) => OpenFile(key);

    /// <summary>The number of bytes a resolved file occupies before decoding.</summary>
    /// <param name="key">A key from <see cref="TryResolve"/>.</param>
    /// <returns>The size in bytes, or 0 when it is not known.</returns>
    public abstract long SizeOf(string key);

    /// <summary>
    /// A key that identifies a file uniquely across every container, so two zones over one file share
    /// one decode.
    /// </summary>
    /// <param name="key">A key from <see cref="TryResolve"/>.</param>
    /// <returns>The cache key.</returns>
    public abstract string CacheKeyFor(string key);

    /// <summary>Every preset this container holds, in a stable order.</summary>
    /// <returns>The preset keys, usable with <see cref="OpenFile"/>.</returns>
    public abstract IReadOnlyList<string> FindPresets();

    /// <summary>Releases whatever the container holds open.</summary>
    public abstract void Dispose();

    /// <summary>
    /// Turns a path written by a preset into forward-slash form with <c>.</c> and <c>..</c> segments
    /// resolved.
    /// </summary>
    /// <param name="relativePath">The path as written.</param>
    /// <returns>The normalized segments, or null when the path climbs above its own root.</returns>
    protected static IReadOnlyList<string> NormalizeSegments(string relativePath)
    {
        if (relativePath == null)
        {
            return null;
        }

        var segments = new List<string>();

        foreach (var raw in relativePath.Replace('\\', '/').Split('/'))
        {
            var segment = raw.Trim();

            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments;
    }
}

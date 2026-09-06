using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

/// <summary>
/// A <c>.dslibrary</c> or <c>.dsbundle</c> archive, read in place: the preset and every sample come out
/// of the zip without being unpacked to disk first.
/// </summary>
/// <remarks>
/// <para>
/// An entry is copied into memory when it is opened, because a deflated zip entry is not seekable and
/// every audio reader in this library needs to seek. That means one sample's compressed bytes are held
/// briefly during its decode and released afterwards; the decoded audio itself is what stays, shared
/// through the instrument's sample cache exactly as for a folder library.
/// </para>
/// <para>
/// Nothing is written to a cache folder. Extraction was the alternative, and it was rejected: it turns
/// loading a library into a disk-space decision, needs invalidation when the archive changes, and buys
/// nothing while sample decoding is eager. When streaming playback arrives it may need a real file
/// behind a zone, and that is the point at which a per-library cache folder earns its place -
/// <see cref="DecentSamplerLoadOptions.CacheFolder"/> already carries where it would go.
/// </para>
/// </remarks>
public sealed class DecentSamplerArchiveContainer : DecentSamplerContainer
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _entriesByFoldedName = new(StringComparer.Ordinal);
    private readonly object _gate = new object();
    private Stream _stream;
    private bool _disposed;

    /// <summary>Opens an archive.</summary>
    /// <param name="archivePath">The <c>.dslibrary</c> or <c>.dsbundle</c> file.</param>
    /// <param name="baseEntryDirectory">
    /// The directory inside the archive that relative paths are resolved against, with forward slashes
    /// and no trailing slash. Empty means the archive root.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="archivePath"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The archive does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable zip archive.</exception>
    public DecentSamplerArchiveContainer(string archivePath, string baseEntryDirectory = "")
    {
        if (archivePath == null)
        {
            throw new ArgumentNullException(nameof(archivePath));
        }

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The container does not exist.", archivePath);
        }

        ArchivePath = Path.GetFullPath(archivePath);
        BaseEntryDirectory = (baseEntryDirectory ?? string.Empty).Replace('\\', '/').Trim('/');

        _stream = File.OpenRead(ArchivePath);

        try
        {
            _archive = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception)
        {
            _stream.Dispose();
            _stream = null;
            throw;
        }

        foreach (var entry in _archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var name = entry.FullName.Replace('\\', '/');
            _entries[name] = entry;
            _entriesByFoldedName.TryAdd(name.ToLowerInvariant(), name);
        }
    }

    /// <summary>The archive file this container reads.</summary>
    public string ArchivePath { get; }

    /// <summary>
    /// The directory inside the archive that relative paths are resolved against, with forward slashes
    /// and no trailing slash. Empty means the archive root.
    /// </summary>
    public string BaseEntryDirectory { get; }

    /// <inheritdoc/>
    public override string Description =>
        BaseEntryDirectory.Length == 0 ? ArchivePath : ArchivePath + "!" + BaseEntryDirectory;

    /// <inheritdoc/>
    public override bool IsArchive => true;

    /// <summary>
    /// A container that reads the same archive but resolves relative paths against a different folder
    /// inside it - the folder one particular preset lives in.
    /// </summary>
    /// <param name="presetEntryName">The full entry name of a preset in this archive.</param>
    /// <returns>A new container. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="presetEntryName"/> is null.</exception>
    public DecentSamplerArchiveContainer ForPreset(string presetEntryName)
    {
        if (presetEntryName == null)
        {
            throw new ArgumentNullException(nameof(presetEntryName));
        }

        var slash = presetEntryName.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : presetEntryName.Substring(0, slash);
        return new DecentSamplerArchiveContainer(ArchivePath, directory);
    }

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
        var candidate = BaseEntryDirectory.Length == 0 ? joined : BaseEntryDirectory + "/" + joined;

        if (_entries.ContainsKey(candidate))
        {
            key = candidate;
            return true;
        }

        if (_entriesByFoldedName.TryGetValue(candidate.ToLowerInvariant(), out var folded))
        {
            key = folded;
            return true;
        }

        // A library may write a path that is already relative to the archive root rather than to the
        // preset's own folder; the reference player finds those too.
        if (_entries.ContainsKey(joined))
        {
            key = joined;
            return true;
        }

        if (_entriesByFoldedName.TryGetValue(joined.ToLowerInvariant(), out var rootFolded))
        {
            key = rootFolded;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override Stream OpenFile(string key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(key, out var entry))
            {
                throw new FileNotFoundException("The archive has no such entry.", key);
            }

            // A deflated entry is forward-only; every audio reader here needs to seek, so the entry is
            // copied into memory and the copy is what the caller reads.
            var buffer = new MemoryStream(checked((int)Math.Max(entry.Length, 0)));

            using (var source = entry.Open())
            {
                source.CopyTo(buffer);
            }

            buffer.Position = 0;
            return buffer;
        }
    }

    // Streaming reads the entry in place, through its own handle on the archive file, so a large
    // sample never lands in memory whole and two readers never share one decompressor.
    internal override Stream OpenStreamingFile(string key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_entries.ContainsKey(key))
            {
                throw new FileNotFoundException("The archive has no such entry.", key);
            }
        }

        return new DecentSamplerArchiveEntryStream(ArchivePath, key);
    }

    /// <inheritdoc/>
    public override long SizeOf(string key) => _entries.TryGetValue(key, out var entry) ? entry.Length : 0;

    /// <inheritdoc/>
    public override string CacheKeyFor(string key) => ArchivePath + "!" + key;

    /// <inheritdoc/>
    public override IReadOnlyList<string> FindPresets() =>
        _entries.Keys
            .Where(name => name.EndsWith(".dspreset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The entry name of the archive's <c>DSLibraryInfo.xml</c>, or null when it has none.</summary>
    /// <returns>The entry name.</returns>
    public string FindLibraryInfo() =>
        _entries.Keys
            .Where(name => name.EndsWith("DSLibraryInfo.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.Length)
            .ThenBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <inheritdoc/>
    public override void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _archive?.Dispose();
            _stream?.Dispose();
            _stream = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DecentSamplerArchiveContainer));
        }
    }
}

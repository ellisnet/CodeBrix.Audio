using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// Loads SoundFonts once and shares them, keyed by file path.
/// </summary>
/// <remarks>
/// <para>
/// SoundFont files run to tens of megabytes, and a <see cref="SoundFont"/> holds all of its sample data
/// in memory. Loading one per track - or worse, per note - is the mistake this type exists to prevent.
/// A <see cref="SoundFont"/> is immutable once parsed and is safe to share across any number of
/// synthesizers and players, so one instance per file is all a process ever needs.
/// </para>
/// <para>
/// The cache is thread-safe. Concurrent requests for the same path load it once; the losers of the race
/// wait and receive the same instance.
/// </para>
/// <para>
/// Disposing the cache drops its references so the SoundFonts can be collected. It does NOT invalidate
/// instances already handed out - a player still holding one keeps working. <see cref="SoundFont"/> owns
/// no unmanaged resources, so there is nothing to leak either way.
/// </para>
/// </remarks>
public sealed class SoundFontCache : IDisposable
{
    private readonly object _lock = new object();
    private readonly Dictionary<string, SoundFont> _byPath =
        new Dictionary<string, SoundFont>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>The number of SoundFonts currently held.</summary>
    public int Count
    {
        get { lock (_lock) { return _byPath.Count; } }
    }

    /// <summary>
    /// Returns the SoundFont for the given file, loading it on first request and returning the same
    /// instance on every request after that.
    /// </summary>
    /// <param name="path">Path to a <c>.sf2</c> file.</param>
    /// <returns>The parsed SoundFont.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid SoundFont.</exception>
    public SoundFont Get(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var key = Path.GetFullPath(path);

        lock (_lock)
        {
            ThrowIfDisposed();

            if (_byPath.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // Loading inside the lock is deliberate: a SoundFont is large and slow to parse, and
            // letting two threads race to load the same file would double the peak memory for no gain.
            var soundFont = new SoundFont(key);
            _byPath[key] = soundFont;
            return soundFont;
        }
    }

    /// <summary>
    /// Adds an already-loaded SoundFont under a caller-chosen key, for SoundFonts that did not come
    /// from a file (loaded from a stream, an embedded resource, or a downloaded asset).
    /// </summary>
    /// <param name="key">The key to store it under. Compared case-insensitively, like a path.</param>
    /// <param name="soundFont">The SoundFont to share.</param>
    /// <returns>
    /// The instance now held for <paramref name="key"/>: <paramref name="soundFont"/> if it was added,
    /// or the existing instance if one was already present.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="soundFont"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public SoundFont GetOrAdd(string key, SoundFont soundFont)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        lock (_lock)
        {
            ThrowIfDisposed();

            if (_byPath.TryGetValue(key, out var existing))
            {
                return existing;
            }

            _byPath[key] = soundFont;
            return soundFont;
        }
    }

    /// <summary>Whether a SoundFont is already held for the given path or key.</summary>
    /// <param name="pathOrKey">The path or key to look for.</param>
    /// <returns><see langword="true"/> if it is cached.</returns>
    public bool Contains(string pathOrKey)
    {
        if (pathOrKey == null)
        {
            return false;
        }

        lock (_lock)
        {
            return _byPath.ContainsKey(pathOrKey) || _byPath.ContainsKey(SafeFullPath(pathOrKey));
        }
    }

    /// <summary>Drops every cached SoundFont. Instances already handed out keep working.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _byPath.Clear();
        }
    }

    /// <summary>Drops every cached SoundFont and blocks further use of this cache.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _byPath.Clear();
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // Not a usable path - it was a GetOrAdd key, which Contains already checked verbatim.
            return path;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SoundFontCache));
        }
    }
}

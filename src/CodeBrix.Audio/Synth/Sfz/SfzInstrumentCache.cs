using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// Loads SFZ instruments once and shares them, keyed by file path.
/// </summary>
/// <remarks>
/// <para>
/// The SFZ counterpart of <see cref="CodeBrix.Audio.Synth.SoundFontCache"/>, and it exists for the same
/// reason: an <see cref="SfzInstrument"/> holds every decoded sample in memory, so loading one per track
/// - or per note - is the mistake this type prevents. An instrument is immutable once loaded and safe to
/// share across any number of synthesizers and players.
/// </para>
/// <para>
/// The cache is thread-safe. Concurrent requests for the same path load it once; the losers of the race
/// wait and receive the same instance. Disposing the cache drops its references so the instruments can
/// be collected; instances already handed out keep working.
/// </para>
/// </remarks>
public sealed class SfzInstrumentCache : IDisposable
{
    private readonly object _lock = new object();
    private readonly Dictionary<string, SfzInstrument> _byPath =
        new Dictionary<string, SfzInstrument>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>The number of instruments currently held.</summary>
    public int Count
    {
        get { lock (_lock) { return _byPath.Count; } }
    }

    /// <summary>
    /// Returns the instrument for the given file, loading it on first request and returning the same
    /// instance on every request after that.
    /// </summary>
    /// <param name="path">Path to a <c>.sfz</c> file.</param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public SfzInstrument Get(string path)
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

            // Loading inside the lock is deliberate, exactly as in SoundFontCache: decoding a library's
            // samples is large and slow, and racing threads would double the peak memory for no gain.
            var instrument = new SfzInstrument(key);
            _byPath[key] = instrument;
            return instrument;
        }
    }

    /// <summary>
    /// Adds an already-loaded instrument under a caller-chosen key.
    /// </summary>
    /// <param name="key">The key to store it under. Compared case-insensitively, like a path.</param>
    /// <param name="instrument">The instrument to share.</param>
    /// <returns>
    /// The instance now held for <paramref name="key"/>: <paramref name="instrument"/> if it was added,
    /// or the existing instance if one was already present.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="instrument"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public SfzInstrument GetOrAdd(string key, SfzInstrument instrument)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        lock (_lock)
        {
            ThrowIfDisposed();

            if (_byPath.TryGetValue(key, out var existing))
            {
                return existing;
            }

            _byPath[key] = instrument;
            return instrument;
        }
    }

    /// <summary>Whether an instrument is already held for the given path or key.</summary>
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

    /// <summary>Drops every cached instrument. Instances already handed out keep working.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _byPath.Clear();
        }
    }

    /// <summary>Drops every cached instrument and blocks further use of this cache.</summary>
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
            throw new ObjectDisposedException(nameof(SfzInstrumentCache));
        }
    }
}

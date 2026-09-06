using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler.Samples;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Loads Decent Sampler instruments once and shares them, keyed by file path.
/// </summary>
/// <remarks>
/// <para>
/// The Decent Sampler counterpart of <see cref="CodeBrix.Audio.Synth.Sfz.SfzInstrumentCache"/>, and it
/// exists for the same reason: an instrument holds every decoded sample in memory, so loading one per
/// track - or per note - is the mistake this type prevents. An instrument is immutable once loaded and
/// safe to share across any number of players.
/// </para>
/// <para>
/// The cache is thread-safe. Concurrent requests for the same path load it once; the losers of the
/// race wait and receive the same instance. Disposing the cache disposes the instruments it holds,
/// releasing their audio, so dispose it only when nothing is playing from them.
/// </para>
/// <para>
/// It also shares SAMPLE DATA between the instruments it loads, keyed by the file each one resolved to.
/// A library whose presets differ only in their interface - a common shape - then costs its recordings
/// once however many of its presets are open, and a streamed sample's preload head is shared the same
/// way. Pass <see langword="false"/> to <see cref="DecentSamplerInstrumentCache(bool)"/> to give every
/// instrument its own audio instead.
/// </para>
/// </remarks>
public sealed class DecentSamplerInstrumentCache : IDisposable
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, DecentSamplerInstrument> _byPath =
        new Dictionary<string, DecentSamplerInstrument>(StringComparer.OrdinalIgnoreCase);
    private readonly DecentSamplerSampleCache _sampleCache;
    private bool _disposed;

    /// <summary>Creates a cache that shares sample data between the instruments it loads.</summary>
    public DecentSamplerInstrumentCache()
        : this(shareSampleData: true)
    {
    }

    /// <summary>Creates a cache.</summary>
    /// <param name="shareSampleData">
    /// Whether instruments loaded through this cache share one decode of each sample file. True by
    /// default. False gives every instrument its own audio, which is what a caller wants only when it
    /// intends to dispose instruments independently of the cache.
    /// </param>
    public DecentSamplerInstrumentCache(bool shareSampleData)
    {
        _sampleCache = shareSampleData ? new DecentSamplerSampleCache() : null;
    }

    /// <summary>Whether instruments loaded through this cache share one decode of each sample file.</summary>
    public bool SharesSampleData => _sampleCache != null;

    /// <summary>
    /// How much audio the shared sample data costs, in bytes, or zero when sharing is off. A streamed
    /// sample counts only its preload head.
    /// </summary>
    public long SharedSampleByteCount => _sampleCache?.DecodedByteCount ?? 0;

    /// <summary>How many distinct sample files the shared sample data holds, or zero when sharing is off.</summary>
    public int SharedSampleCount => _sampleCache?.Count ?? 0;

    /// <summary>The number of instruments currently held.</summary>
    public int Count
    {
        get { lock (_gate) { return _byPath.Count; } }
    }

    /// <summary>
    /// Returns the instrument for a path, loading it on first request and returning the same instance
    /// on every request after that.
    /// </summary>
    /// <param name="path">A <c>.dspreset</c>, <c>.dslibrary</c>, <c>.dsbundle</c> or library folder.</param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at that path, or it holds no preset.</exception>
    public DecentSamplerInstrument Get(string path) => Get(path, null);

    /// <summary>
    /// Returns the instrument for a path, loading it on first request with the given options and
    /// returning the same instance on every request after that.
    /// </summary>
    /// <param name="path">A <c>.dspreset</c>, <c>.dslibrary</c>, <c>.dsbundle</c> or library folder.</param>
    /// <param name="options">
    /// How to load it, or null for the defaults. Only used on the first request for a path; a later
    /// request with different options still receives the instance already loaded.
    /// </param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at that path, or it holds no preset.</exception>
    public DecentSamplerInstrument Get(string path, DecentSamplerLoadOptions options)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var key = SafeFullPath(path);
        if (options?.PresetName != null)
        {
            key += "|" + options.PresetName;
        }

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_byPath.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // Loading inside the lock is deliberate, exactly as in SfzInstrumentCache: decoding a
            // library's samples is large and slow, and racing threads would double the peak memory.
            var instrument = DecentSamplerInstrument.Load(path, options, _sampleCache);
            _byPath[key] = instrument;
            return instrument;
        }
    }

    /// <summary>Adds an already-loaded instrument under a caller-chosen key.</summary>
    /// <param name="key">The key to store it under. Compared case-insensitively, like a path.</param>
    /// <param name="instrument">The instrument to share.</param>
    /// <returns>
    /// The instance now held for <paramref name="key"/>: <paramref name="instrument"/> if it was added,
    /// or the existing instance if one was already present.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="instrument"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public DecentSamplerInstrument GetOrAdd(string key, DecentSamplerInstrument instrument)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        lock (_gate)
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

        lock (_gate)
        {
            return _byPath.ContainsKey(pathOrKey) || _byPath.ContainsKey(SafeFullPath(pathOrKey));
        }
    }

    /// <summary>Disposes every cached instrument and empties the cache.</summary>
    /// <remarks>
    /// The shared sample data survives, because a caller that clears the cache to reload a library
    /// usually wants the audio it already has. Dispose the cache to release that too.
    /// </remarks>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var instrument in _byPath.Values)
            {
                instrument.Dispose();
            }

            _byPath.Clear();
        }
    }

    /// <summary>Disposes every cached instrument and blocks further use of this cache.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var instrument in _byPath.Values)
            {
                instrument.Dispose();
            }

            _byPath.Clear();
            _sampleCache?.Dispose();
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

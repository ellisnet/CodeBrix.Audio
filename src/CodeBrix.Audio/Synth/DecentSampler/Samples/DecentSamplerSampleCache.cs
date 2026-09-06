using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Samples;

/// <summary>
/// The decoded samples of one instrument, shared between the zones that reference the same file.
/// </summary>
/// <remarks>
/// A sampled library maps one file across several zones as a matter of course - a velocity split, a
/// round robin, a release trigger over the same recording. Keyed by the container's own cache key, the
/// file is decoded once however many zones want it, which is the difference between a piano costing its
/// samples and costing them several times over.
/// </remarks>
internal sealed class DecentSamplerSampleCache : IDisposable
{
    private readonly Dictionary<string, ISampleSource> _sources = new(StringComparer.Ordinal);
    private readonly object _gate = new object();
    private bool _disposed;

    /// <summary>How many distinct files have been decoded.</summary>
    public int Count
    {
        get { lock (_gate) { return _sources.Count; } }
    }

    /// <summary>The total decoded audio held, in bytes.</summary>
    public long DecodedByteCount
    {
        get
        {
            lock (_gate)
            {
                var total = 0L;
                foreach (var source in _sources.Values)
                {
                    total += source.DecodedByteCount;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// Returns the source for a key, creating it on first request and returning the same instance after
    /// that.
    /// </summary>
    /// <param name="key">The container's cache key for the file.</param>
    /// <param name="factory">Decodes the file. Called at most once per key.</param>
    /// <returns>The shared source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public ISampleSource GetOrAdd(string key, Func<ISampleSource> factory)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_sources.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // Decoding inside the lock is deliberate, as in SfzInstrumentCache: racing threads would
            // double the peak memory of a large library for no gain.
            var created = factory();
            _sources[key] = created;
            return created;
        }
    }

    /// <summary>Whether a file has already been decoded.</summary>
    /// <param name="key">The container's cache key for the file.</param>
    /// <returns><see langword="true"/> when it is held.</returns>
    public bool Contains(string key)
    {
        if (key == null)
        {
            return false;
        }

        lock (_gate)
        {
            return _sources.ContainsKey(key);
        }
    }

    /// <summary>Releases every decoded sample.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var source in _sources.Values)
            {
                source.Dispose();
            }

            _sources.Clear();
        }
    }
}

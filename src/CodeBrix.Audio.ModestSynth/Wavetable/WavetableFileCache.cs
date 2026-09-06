using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodeBrix.Audio.ModestSynth.Wavetable;

/// <summary>
/// Keeps decoded wavetables so that a file is read, downmixed and band-limited ONCE however many
/// voices, groups or instruments play it.
/// </summary>
/// <remarks>
/// <para>
/// Building a table's band-limited copies costs real time and several megabytes (about four and a
/// half times the frame data). A preset that puts one wavetable on a sixteen-voice group would pay
/// that sixteen times without this; with it, every voice shares one immutable
/// <see cref="WavetableFile" />.
/// </para>
/// <para>
/// Entries live until <see cref="Clear" /> is called. Nothing is evicted on a timer or a memory
/// pressure signal, because a synthesizer that quietly re-decodes a table mid-performance is worse
/// than one that holds on to it; call <see cref="Clear" /> when an instrument is unloaded and the
/// tables are no longer wanted.
/// </para>
/// <para>
/// FAILURES ARE NOT CACHED. A path that could not be read is retried next time, so a file that
/// appears after the first attempt is picked up. The cache is keyed on the FULL path and the frame
/// size asked for, so the same file loaded at two frame sizes is two entries - unless the file
/// carries a <c>clm&#160;</c> chunk, in which case both entries hold identical frames.
/// </para>
/// <para>
/// Every member is thread-safe. None of them belongs on the audio thread: they open files.
/// </para>
/// </remarks>
public static class WavetableFileCache
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, WavetableFile> Entries =
        new Dictionary<string, WavetableFile>(StringComparer.Ordinal);

    /// <summary>How many tables are currently held.</summary>
    public static int Count
    {
        get { lock (Gate) { return Entries.Count; } }
    }

    /// <summary>
    /// Roughly how much memory the held tables occupy, in bytes.
    /// </summary>
    public static long ApproximateSizeInBytes
    {
        get
        {
            lock (Gate)
            {
                long total = 0L;
                foreach (KeyValuePair<string, WavetableFile> entry in Entries)
                {
                    total += entry.Value.ApproximateSizeInBytes;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// Returns the table for a path, loading and caching it the first time.
    /// </summary>
    /// <param name="path">The .wav file to read.</param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk; 0 for
    /// <see cref="WavetableFile.DefaultFrameSize" />.
    /// </param>
    /// <param name="file">On success, the table; otherwise null.</param>
    /// <param name="problem">On failure, one line saying what went wrong; otherwise null.</param>
    /// <returns><see langword="true" /> when a table came back.</returns>
    public static bool TryGetOrLoad(string path, int frameSize, out WavetableFile file, out string problem)
    {
        file = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "No file reference: <oscillator> @wavetableFile is not set; " +
                      "the wavetable oscillator falls back to a sine.";
            return false;
        }

        string key = BuildKey(path, frameSize);
        if (key != null)
        {
            lock (Gate)
            {
                if (Entries.TryGetValue(key, out WavetableFile cached))
                {
                    file = cached;
                    return true;
                }
            }
        }

        if (!WavetableFile.TryLoad(path, frameSize, out WavetableFile loaded, out problem))
        {
            return false;
        }

        if (key != null)
        {
            lock (Gate)
            {
                if (Entries.TryGetValue(key, out WavetableFile raced))
                {
                    file = raced;
                    return true;
                }

                Entries[key] = loaded;
            }
        }

        file = loaded;
        return true;
    }

    /// <summary>
    /// Returns the table held under a caller-chosen key, loading it from a stream the first time.
    /// </summary>
    /// <param name="cacheKey">
    /// A key that identifies the bytes - a container's own cache key for an archive entry, for
    /// instance. Must not be blank; two different files must never share one.
    /// </param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk; 0 for
    /// <see cref="WavetableFile.DefaultFrameSize" />.
    /// </param>
    /// <param name="openStream">
    /// Opens the .wav bytes. Called only when the table is not already held, and the stream it
    /// returns is disposed here. Returning null is reported as a missing file.
    /// </param>
    /// <param name="file">On success, the table; otherwise null.</param>
    /// <param name="problem">On failure, one line saying what went wrong; otherwise null.</param>
    /// <returns><see langword="true" /> when a table came back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="openStream" /> is null.</exception>
    /// <remarks>
    /// This is the entry point for a wavetable that is not a file on disk - one inside a
    /// <c>.dslibrary</c> archive, say. It shares the same store as the path form, so a table is
    /// still decoded once however many voices play it.
    /// </remarks>
    public static bool TryGetOrLoad(
        string cacheKey,
        int frameSize,
        Func<Stream> openStream,
        out WavetableFile file,
        out string problem)
    {
        if (openStream == null) { throw new ArgumentNullException(nameof(openStream)); }

        file = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            problem = "No file reference: <oscillator> @wavetableFile is not set; " +
                      "the wavetable oscillator falls back to a sine.";
            return false;
        }

        string key = cacheKey + "|" +
                     WavetableFile.ClampFrameSize(frameSize).ToString(CultureInfo.InvariantCulture);

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out WavetableFile cached))
            {
                file = cached;
                return true;
            }
        }

        Stream stream;
        try
        {
            stream = openStream();
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                                   ex is InvalidOperationException || ex is ArgumentException ||
                                   ex is NotSupportedException || ex is UnauthorizedAccessException)
        {
            problem = "Unreadable file reference: <oscillator> @wavetableFile=\"" + cacheKey + "\" (" +
                      ex.Message + "); the wavetable oscillator falls back to a sine.";
            return false;
        }

        if (stream == null)
        {
            problem = "Missing file reference: <oscillator> @wavetableFile=\"" + cacheKey +
                      "\"; the wavetable oscillator falls back to a sine.";
            return false;
        }

        WavetableFile loaded;
        using (stream)
        {
            if (!WavetableFile.TryLoad(stream, cacheKey, frameSize, out loaded, out problem))
            {
                return false;
            }
        }

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out WavetableFile raced))
            {
                file = raced;
                return true;
            }

            Entries[key] = loaded;
        }

        file = loaded;
        return true;
    }

    /// <summary>
    /// Returns the table for a path, or null when it could not be loaded.
    /// </summary>
    /// <param name="path">The .wav file to read.</param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk; 0 for
    /// <see cref="WavetableFile.DefaultFrameSize" />.
    /// </param>
    /// <returns>
    /// The table, or null. Use <see cref="TryGetOrLoad(string, int, out WavetableFile, out string)" />
    /// when you need to say why.
    /// </returns>
    public static WavetableFile GetOrLoad(string path, int frameSize)
    {
        TryGetOrLoad(path, frameSize, out WavetableFile file, out _);
        return file;
    }

    /// <summary>
    /// Drops every held table. Anything already playing keeps the table it was given.
    /// </summary>
    public static void Clear()
    {
        lock (Gate) { Entries.Clear(); }
    }

    private static string BuildKey(string path, int frameSize)
    {
        try
        {
            return Path.GetFullPath(path) + "|" +
                   WavetableFile.ClampFrameSize(frameSize).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException ||
                                   ex is PathTooLongException || ex is IOException ||
                                   ex is UnauthorizedAccessException)
        {
            // An unusable path is not cacheable; TryLoad will report it properly in a moment.
            return null;
        }
    }
}

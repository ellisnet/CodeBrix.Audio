using System.IO;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// Where a loaded song's files come from: a folder of extracted files, or a zip read in place. A
/// key identifies one file - a full path for a folder, an entry name for a zip.
/// </summary>
internal abstract class SunoContentStore
{
    /// <summary>Which of the two shapes this store reads.</summary>
    internal abstract SunoSourceKind Kind { get; }

    /// <summary>Where extracted entries are written, or null when nothing is written to disk.</summary>
    internal abstract string CacheFolder { get; }

    /// <summary>Whether <see cref="GetFilePath"/> can answer.</summary>
    internal abstract bool CanProvideFilePath { get; }

    /// <summary>Every key this store holds, in the order the source listed them.</summary>
    internal abstract string[] Keys { get; }

    /// <summary>The uncompressed size of one file, in bytes.</summary>
    internal abstract long GetLength(string key);

    /// <summary>
    /// Opens a file for reading forward only, without materialising it. This is what the header
    /// probe uses, so that finding out how long a stem is does not cost an extraction.
    /// </summary>
    internal abstract Stream OpenSequential(string key);

    /// <summary>
    /// Opens a file as a seekable stream, materialising it first if the source cannot seek. The
    /// caller owns the stream and must dispose it.
    /// </summary>
    internal abstract Stream OpenSeekable(string key);

    /// <summary>
    /// The path of a file on disk, materialising it first if it is not there yet.
    /// </summary>
    internal abstract string GetFilePath(string key);

    /// <summary>Reads a whole file into memory. Used for MIDI, which is small.</summary>
    internal virtual byte[] ReadAllBytes(string key)
    {
        using var source = OpenSequential(key);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Drops whatever this store has materialised.</summary>
    internal virtual void ClearCache()
    {
    }
}

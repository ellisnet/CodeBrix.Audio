namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// How the entries of a stems zip are made available to the audio readers, which need a seekable
/// stream and cannot decode straight from a compressed entry.
/// </summary>
public enum SunoZipExtraction
{
    /// <summary>
    /// Extract each entry to a file in a cache folder the first time it is asked for, and reuse
    /// that file afterwards - including across processes and across later loads of the same zip.
    /// This is the default, and the only mode in which a stem can hand out a file path.
    /// </summary>
    CacheFolder = 0,

    /// <summary>
    /// Decompress each entry into memory the first time it is asked for and keep the bytes for the
    /// lifetime of the song. Nothing is written to disk, and no stem can hand out a file path. A
    /// four-minute stems export holds several hundred megabytes of WAV, so choose this only when
    /// writing to disk is not an option.
    /// </summary>
    Memory = 1,
}

using System.Threading;

namespace CodeBrix.Audio.Synth.DecentSampler.Samples;

// One sample file that has not been OPENED yet, because the instrument was loaded with
// DecodeSamples = false.
//
// The point is that a browser, a validity check or a corpus test can load a whole library - every path
// resolved, every group and zone built, every binding wired - without decoding a byte of audio, and
// still PLAY, because the first note that wants a file asks for it. The asking is a flag set on the
// audio thread; a worker does the decode; the note that asked is silent, and the instrument says so
// once. The next note has its audio.
//
// One instance per distinct file, shared by every zone that points at it, so a velocity split of eight
// zones over one recording asks once.
internal sealed class DeferredSample
{
    private ISampleSource _source;
    private int _requested;
    private int _failed;

    public DeferredSample(string cacheKey, string containerKey, string path, bool stream, int preloadFrames)
    {
        CacheKey = cacheKey;
        ContainerKey = containerKey;
        Path = path;
        Stream = stream;
        PreloadFrames = preloadFrames;
    }

    /// <summary>The cache key the decoded file will be shared under.</summary>
    public string CacheKey { get; }

    /// <summary>The container key the file is opened with.</summary>
    public string ContainerKey { get; }

    /// <summary>The path the preset wrote, for problem messages.</summary>
    public string Path { get; }

    /// <summary>
    /// Whether the memory policy decided this file streams. Deferring a load does not change what the
    /// policy chose - it only postpones acting on it - so a library loaded lazily still streams the
    /// samples that were too big to hold.
    /// </summary>
    public bool Stream { get; }

    /// <summary>The preload head a streamed file will keep, in frames.</summary>
    public int PreloadFrames { get; }

    /// <summary>The decoded audio once a worker has produced it, otherwise null.</summary>
    public ISampleSource Source => Volatile.Read(ref _source);

    /// <summary>Whether the decode was tried and failed, so nothing should try again.</summary>
    public bool Failed => Volatile.Read(ref _failed) != 0;

    /// <summary>
    /// Asks for the file. Callable from the audio thread: one interlocked write, no allocation.
    /// </summary>
    /// <returns><see langword="true"/> when this call is the one that raised the request.</returns>
    public bool Request() => Interlocked.Exchange(ref _requested, 1) == 0;

    /// <summary>Whether a decode has been asked for and not yet done.</summary>
    public bool IsPending =>
        Volatile.Read(ref _requested) != 0 && Volatile.Read(ref _source) == null && !Failed;

    /// <summary>Publishes the decoded audio.</summary>
    /// <param name="source">The decoded source.</param>
    public void Complete(ISampleSource source) => Volatile.Write(ref _source, source);

    /// <summary>Records that the file could not be decoded, so nothing tries again.</summary>
    public void Fail() => Volatile.Write(ref _failed, 1);
}

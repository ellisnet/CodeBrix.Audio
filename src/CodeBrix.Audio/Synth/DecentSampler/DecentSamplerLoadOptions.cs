using System;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// How a <see cref="DecentSamplerInstrument"/> is loaded: when samples are decoded, where a cache
/// folder may go, and the thresholds that decide memory against streaming.
/// </summary>
/// <remarks>
/// The defaults suit a desktop application loading one library at a time: samples decode eagerly, so
/// the first note is never late, and the streaming thresholds are the ones the plan settled on -
/// 8 MB of decoded audio per sample, 1 GB per instrument. A survey of nine Pianobook libraries decoded
/// to 4.7 GB of 32-bit float, one of them 2.6 GB on its own, which is why the thresholds exist.
/// </remarks>
public sealed class DecentSamplerLoadOptions
{
    /// <summary>The default decoded size above which one sample streams instead: 8 MB.</summary>
    public const long DefaultStreamingSampleThresholdBytes = 8L * 1024 * 1024;

    /// <summary>The default decoded size above which an instrument starts streaming: 1 GB.</summary>
    public const long DefaultInstrumentMemoryBudgetBytes = 1024L * 1024 * 1024;

    /// <summary>The default preload head of a streamed sample: 65,536 frames.</summary>
    public const int DefaultStreamingPreloadFrames = 65536;

    private long _streamingSampleThresholdBytes = DefaultStreamingSampleThresholdBytes;
    private long _instrumentMemoryBudgetBytes = DefaultInstrumentMemoryBudgetBytes;
    private int _streamingPreloadFrames = DefaultStreamingPreloadFrames;

    /// <summary>
    /// Whether every sample is decoded while the instrument loads. Default true.
    /// </summary>
    /// <remarks>
    /// With this off, no audio file is DECODED at all: the instrument still resolves every path, still
    /// reports a missing file, and still gives the whole object model - which is what a survey tool, a
    /// preset browser or a validity check wants, and what keeps a corpus test out of gigabytes of RAM.
    /// The instrument still plays: the first note that wants a sample asks for it, a worker decodes it,
    /// that one note is silent and the instrument reports it once, and every note after it sounds.
    /// A streamed sample is unaffected - streaming never decodes the file whole in the first place.
    /// </remarks>
    public bool DecodeSamples { get; set; } = true;

    /// <summary>
    /// A folder the engine may use for per-library working files, or null for none. Default null.
    /// </summary>
    /// <remarks>
    /// Nothing is written here: archives are read in place, and streaming reads an archive entry in
    /// place too, through its own handle on the archive file. The setting is kept for a consumer that
    /// wants working files somewhere particular if a later phase ever needs them.
    /// </remarks>
    public string CacheFolder { get; set; }

    /// <summary>
    /// The decoded size above which one sample is streamed rather than held in memory, in bytes.
    /// Default 8 MB.
    /// </summary>
    /// <remarks>
    /// Stored now and honoured by the streaming phase. A zone whose <c>playbackMode</c> says
    /// <c>memory</c> or <c>disk_streaming</c> outright is not subject to it; only <c>auto</c> is.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long StreamingSampleThresholdBytes
    {
        get => _streamingSampleThresholdBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _streamingSampleThresholdBytes = value;
        }
    }

    /// <summary>
    /// The total decoded size above which an instrument starts streaming the rest, in bytes.
    /// Default 1 GB.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long InstrumentMemoryBudgetBytes
    {
        get => _instrumentMemoryBudgetBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _instrumentMemoryBudgetBytes = value;
        }
    }

    /// <summary>
    /// How many frames of a streamed sample are decoded into RAM and kept, so that a note starting at
    /// the beginning of the file sounds without waiting for the reader. Default 65,536 - about 1.5
    /// seconds at 44.1 kHz, three quarters of a second at 96 kHz.
    /// </summary>
    /// <remarks>
    /// The head also covers the loop when the loop starts inside it, which is what keeps a sustained
    /// loop from seeking on every turn. Raising it trades RAM for fewer disk reads; a stereo head of
    /// 65,536 frames costs 512 kB per streamed sample.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int StreamingPreloadFrames
    {
        get => _streamingPreloadFrames;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _streamingPreloadFrames = value;
        }
    }

    /// <summary>
    /// Overrides every <c>playbackMode</c> in the preset, or null to honour what the preset says.
    /// Default null.
    /// </summary>
    public DecentSamplerPlaybackMode? PlaybackModeOverride { get; set; }

    /// <summary>
    /// The preset to load when a container holds more than one, matched against the file name without
    /// its extension and without case. Null takes the first. Default null.
    /// </summary>
    public string PresetName { get; set; }

    /// <summary>A copy of these options, so a caller can hand the same instance to several loads.</summary>
    /// <returns>The copy.</returns>
    public DecentSamplerLoadOptions Clone() =>
        new DecentSamplerLoadOptions
        {
            DecodeSamples = DecodeSamples,
            CacheFolder = CacheFolder,
            StreamingSampleThresholdBytes = StreamingSampleThresholdBytes,
            InstrumentMemoryBudgetBytes = InstrumentMemoryBudgetBytes,
            StreamingPreloadFrames = StreamingPreloadFrames,
            PlaybackModeOverride = PlaybackModeOverride,
            PresetName = PresetName,
        };

    /// <summary>
    /// The folder working files would go in: <see cref="CacheFolder"/> when set, otherwise a
    /// <c>CodeBrix.Audio/DecentSampler</c> folder under the user's local application data.
    /// </summary>
    /// <returns>The folder path. Not created.</returns>
    public string ResolveCacheFolder() =>
        CacheFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeBrix.Audio", "DecentSampler");
}

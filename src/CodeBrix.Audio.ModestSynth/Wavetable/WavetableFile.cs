using System;
using System.IO;
using CodeBrix.Audio.ModestSynth.Wavetable.Internal;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.ModestSynth.Wavetable;

/// <summary>
/// A decoded multi-frame wavetable: the frames a <c>.wav</c> file holds, cut to the frame size the
/// file declares or the one you asked for, together with the band-limited copies the oscillator
/// plays from.
/// </summary>
/// <remarks>
/// <para>
/// A wavetable file is one long recording that is really a series of single cycles laid end to
/// end - 128 frames of 2048 samples is 262,144 samples of audio and 128 different timbres.
/// <see cref="WavetableOscillator" /> cycles through one frame per period at the pitch of the note,
/// and moving its position walks the table.
/// </para>
/// <para>
/// DECODE ONCE, PLAY MANY. Building the band-limited copies costs a fraction of a second and
/// several megabytes, so a table is meant to be loaded once through
/// <see cref="WavetableFileCache" /> and shared by every voice that plays it. Nothing here is
/// mutable after construction, so sharing one across voices and across threads is safe.
/// </para>
/// <para>
/// WHAT IT ACCEPTS. Any .wav file CodeBrix.Audio can read: 8, 16, 24 or 32-bit PCM and 32 or
/// 64-bit IEEE float, at any sample rate, mono or multi-channel. A multi-channel file KEEPS ITS
/// FIRST CHANNEL and discards the rest, because an oscillator is one mono voice. MEASURED (round 3,
/// item 44): the reference plays a stereo wavetable as its LEFT channel on both outputs and drops
/// the right one silently - it does not average them. The file's own sample rate is irrelevant to
/// playback - a frame is one cycle whatever rate it was recorded at - and is kept only as
/// information.
/// </para>
/// <para>
/// FRAME SIZE. A Serum-compatible file carries a <c>clm&#160;</c> RIFF chunk naming its frame
/// size, and that WINS over anything you pass in - MEASURED (round 3, item 44) in both directions,
/// including a file whose chunk lies about its size. Without the chunk the size you pass is used,
/// and 2048 is the documented default with neither. Samples left over after the last whole frame are
/// dropped.
/// </para>
/// </remarks>
public sealed class WavetableFile
{
    /// <summary>The frame size the format falls back to when nothing declares one.</summary>
    public const int DefaultFrameSize = 2048;

    /// <summary>The smallest frame size that can be played.</summary>
    public const int MinimumFrameSize = 2;

    /// <summary>The largest frame size that will be accepted from a file or an attribute.</summary>
    public const int MaximumFrameSize = 65536;

    /// <summary>
    /// The most samples that will be read out of one file - 16 mega-samples, which is 8,192 frames
    /// of 2,048. Anything larger is reported rather than loaded.
    /// </summary>
    /// <remarks>
    /// The limit exists because the band-limited copies cost about four and a half times the frame
    /// data: a table at this ceiling would occupy roughly 288 MB. A Serum wavetable is typically
    /// 256 frames of 2,048, which is a five-hundredth of it.
    /// </remarks>
    public const int MaximumSampleCount = 16 * 1024 * 1024;

    private readonly float[] samples;
    private readonly WavetableMipMap mipMap;

    private WavetableFile(string sourcePath, float[] samples, int frameSize, int frameCount,
        bool hasClmChunk, int declaredFrameSize, int sourceSampleRate, int sourceChannels)
    {
        SourcePath = sourcePath;
        this.samples = samples;
        FrameSize = frameSize;
        FrameCount = frameCount;
        HasClmChunk = hasClmChunk;
        DeclaredFrameSize = declaredFrameSize;
        SourceSampleRate = sourceSampleRate;
        SourceChannels = sourceChannels;
        mipMap = WavetableMipMap.Build(samples, frameSize, frameCount);
    }

    /// <summary>
    /// Where the table came from - the full path of the .wav file, or the name given to
    /// <see cref="FromSamples" />. Never null.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>The samples in one frame - one cycle of the waveform.</summary>
    public int FrameSize { get; }

    /// <summary>How many whole frames the table holds. Always at least one.</summary>
    public int FrameCount { get; }

    /// <summary>The samples actually used - <see cref="FrameCount" /> times <see cref="FrameSize" />.</summary>
    public int SampleCount => FrameCount * FrameSize;

    /// <summary>
    /// Whether the file carried a Serum-compatible <c>clm&#160;</c> RIFF chunk naming its frame
    /// size. When it did, that size was used and any <c>wavetableFrameSize</c> attribute ignored.
    /// </summary>
    public bool HasClmChunk { get; }

    /// <summary>
    /// The frame size the <c>clm&#160;</c> chunk declared, or 0 when there was no usable chunk.
    /// </summary>
    public int DeclaredFrameSize { get; }

    /// <summary>
    /// The sample rate the file was recorded at, in Hz, or 0 for a table built with
    /// <see cref="FromSamples" />. It has no effect on playback.
    /// </summary>
    public int SourceSampleRate { get; }

    /// <summary>
    /// How many channels the file held before the downmix, or 1 for a table built with
    /// <see cref="FromSamples" />.
    /// </summary>
    public int SourceChannels { get; }

    /// <summary>
    /// Roughly how much memory this table occupies: the frames plus the band-limited copies, which
    /// together come to about four and a half times the frame data.
    /// </summary>
    /// <remarks>
    /// The full-bandwidth mip level IS the frames themselves whenever the frame size is a power of
    /// two, so it is counted once, not twice.
    /// </remarks>
    public long ApproximateSizeInBytes
    {
        get
        {
            long total = mipMap.ByteCount;
            if (!ReferenceEquals(mipMap.LevelData(0), samples))
            {
                total += (long)samples.Length * sizeof(float);
            }

            return total;
        }
    }

    /// <summary>
    /// The raw samples of one frame, exactly as the file held them after the channel downmix.
    /// </summary>
    /// <param name="index">The frame, from 0 to <see cref="FrameCount" /> minus one.</param>
    /// <returns>A view over the frame; no copy is made.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the table.</exception>
    public ReadOnlySpan<float> GetFrame(int index)
    {
        if (index < 0 || index >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index,
                "The table holds frames 0 to " + (FrameCount - 1) + ".");
        }

        return new ReadOnlySpan<float>(samples, index * FrameSize, FrameSize);
    }

    /// <summary>
    /// Builds a table from samples you already have, without a file - a wavetable written in code.
    /// </summary>
    /// <param name="samples">Every frame's samples, concatenated. At least one whole frame's worth.</param>
    /// <param name="frameSize">The samples per frame. Clamped to the supported range.</param>
    /// <param name="name">
    /// What to report as <see cref="SourcePath" />; a blank name becomes <c>"(samples)"</c>.
    /// </param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="samples" /> does not hold at least one whole frame.
    /// </exception>
    public static WavetableFile FromSamples(ReadOnlySpan<float> samples, int frameSize, string name)
    {
        int size = ClampFrameSize(frameSize);
        int frames = samples.Length / size;

        if (frames < 1)
        {
            throw new ArgumentException(
                "A wavetable needs at least one whole frame: " + samples.Length + " samples is fewer than " +
                size + ".", nameof(samples));
        }

        float[] copy = samples.Slice(0, frames * size).ToArray();
        string source = string.IsNullOrWhiteSpace(name) ? "(samples)" : name;
        return new WavetableFile(source, copy, size, frames, false, 0, 0, 1);
    }

    /// <summary>
    /// Loads a wavetable from a .wav file, reporting what went wrong instead of throwing.
    /// </summary>
    /// <param name="path">
    /// The file to read. Relative paths are resolved against the current directory, so a preset's
    /// <c>wavetableFile</c> should be combined with the preset's own folder before it gets here.
    /// </param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk. Pass 0 or anything
    /// below <see cref="MinimumFrameSize" /> for <see cref="DefaultFrameSize" />.
    /// </param>
    /// <param name="file">On success, the loaded table; otherwise null.</param>
    /// <param name="problem">
    /// On failure, one human-readable line naming the element, the attribute and the value, in the
    /// style the reference player's preset validator uses; otherwise null.
    /// </param>
    /// <returns><see langword="true" /> when a table was loaded.</returns>
    /// <remarks>
    /// This reads a file and allocates, so it belongs to loading, never to a render callback.
    /// </remarks>
    public static bool TryLoad(string path, int frameSize, out WavetableFile file, out string problem)
    {
        file = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "No file reference: <oscillator> @wavetableFile is not set; " +
                      "the wavetable oscillator falls back to a sine.";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException ||
                                   ex is PathTooLongException || ex is IOException ||
                                   ex is UnauthorizedAccessException)
        {
            problem = "Unusable file reference: <oscillator> @wavetableFile=\"" + path + "\" (" +
                      ex.Message + "); the wavetable oscillator falls back to a sine.";
            return false;
        }

        if (!File.Exists(full))
        {
            problem = "Missing file reference: <oscillator> @wavetableFile=\"" + path +
                      "\" (looked for \"" + full + "\"); the wavetable oscillator falls back to a sine.";
            return false;
        }

        try
        {
            using (WaveFileReader reader = new WaveFileReader(full))
            {
                return TryRead(reader, full, path, frameSize, out file, out problem);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is FormatException ||
                                   ex is InvalidDataException || ex is InvalidOperationException ||
                                   ex is ArgumentException || ex is NotSupportedException ||
                                   ex is UnauthorizedAccessException)
        {
            problem = "Unreadable file reference: <oscillator> @wavetableFile=\"" + path + "\" (" +
                      ex.Message + "); the wavetable oscillator falls back to a sine.";
            return false;
        }
    }

    /// <summary>
    /// Loads a wavetable from a stream of .wav bytes, reporting what went wrong instead of throwing.
    /// </summary>
    /// <param name="stream">
    /// The .wav bytes. The stream must be readable and seekable, and the caller keeps ownership of
    /// it - this method does not dispose it.
    /// </param>
    /// <param name="sourceName">
    /// What to report as <see cref="SourcePath" /> and in any problem line - a container entry name,
    /// for instance. A blank name becomes <c>"(stream)"</c>.
    /// </param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk. Pass 0 or anything
    /// below <see cref="MinimumFrameSize" /> for <see cref="DefaultFrameSize" />.
    /// </param>
    /// <param name="file">On success, the loaded table; otherwise null.</param>
    /// <param name="problem">On failure, one human-readable line; otherwise null.</param>
    /// <returns><see langword="true" /> when a table was loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> is null.</exception>
    /// <remarks>
    /// This is how a wavetable inside a <c>.dslibrary</c> archive is read: the container hands out a
    /// stream over the zip entry and there is no file on disk to point at. It decodes and allocates,
    /// so it belongs to loading, never to a render callback.
    /// </remarks>
    public static bool TryLoad(
        Stream stream, string sourceName, int frameSize, out WavetableFile file, out string problem)
    {
        if (stream == null) { throw new ArgumentNullException(nameof(stream)); }

        file = null;
        problem = null;

        string name = string.IsNullOrWhiteSpace(sourceName) ? "(stream)" : sourceName;

        try
        {
            using (WaveFileReader reader = new WaveFileReader(stream))
            {
                return TryRead(reader, name, name, frameSize, out file, out problem);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is FormatException ||
                                   ex is InvalidDataException || ex is InvalidOperationException ||
                                   ex is ArgumentException || ex is NotSupportedException ||
                                   ex is UnauthorizedAccessException)
        {
            problem = "Unreadable file reference: <oscillator> @wavetableFile=\"" + name + "\" (" +
                      ex.Message + "); the wavetable oscillator falls back to a sine.";
            return false;
        }
    }

    private static bool TryRead(
        WaveFileReader reader,
        string sourcePath,
        string reportedPath,
        int frameSize,
        out WavetableFile file,
        out string problem)
    {
        file = null;
        problem = null;

        int requested = ClampFrameSize(frameSize);

        bool hasClm = false;
        int declared = 0;
        RiffChunk chunk = reader.Chunks.Find(ClmChunk.ChunkId);
        if (chunk != null && chunk.Length > 0 && chunk.Length <= ClmChunk.MaximumPayloadBytes)
        {
            hasClm = ClmChunk.TryParseFrameSize(reader.Chunks.GetData(chunk), MinimumFrameSize,
                MaximumFrameSize, out declared);
        }

        int size = hasClm ? declared : requested;
        int channels = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;

        float[] mono = ReadMono(reader, channels, out string readProblem);
        if (mono == null)
        {
            problem = "Unreadable file reference: <oscillator> @wavetableFile=\"" + reportedPath + "\" (" +
                      readProblem + "); the wavetable oscillator falls back to a sine.";
            return false;
        }

        int frames = mono.Length / size;
        if (frames < 1)
        {
            problem = "Unusable wavetable: <oscillator> @wavetableFile=\"" + reportedPath + "\" holds " +
                      mono.Length + " samples, fewer than one frame of " + size +
                      "; the wavetable oscillator falls back to a sine.";
            return false;
        }

        if (frames * size != mono.Length)
        {
            Array.Resize(ref mono, frames * size);
        }

        file = new WavetableFile(sourcePath, mono, size, frames, hasClm, hasClm ? declared : 0,
            sampleRate, channels);
        return true;
    }

    internal WavetableMipMap MipMap => mipMap;

    internal static int ClampFrameSize(int frameSize)
    {
        if (frameSize < MinimumFrameSize) { return DefaultFrameSize; }
        return frameSize > MaximumFrameSize ? MaximumFrameSize : frameSize;
    }

    private static float[] ReadMono(WaveFileReader reader, int channels, out string problem)
    {
        problem = null;

        if (channels < 1)
        {
            problem = "the file declares " + channels + " channels";
            return null;
        }

        long frames = reader.Length / reader.WaveFormat.BlockAlign;
        if (frames < 1)
        {
            problem = "the file holds no audio";
            return null;
        }

        if (frames > MaximumSampleCount)
        {
            problem = "the file holds " + frames + " samples, more than the " + MaximumSampleCount +
                      " a wavetable may hold";
            return null;
        }

        ISampleProvider provider = reader.ToSampleProvider();
        float[] mono = new float[(int)frames];
        float[] block = new float[4096 * channels];
        int written = 0;

        while (written < mono.Length)
        {
            int read = provider.Read(block);
            if (read <= 0) { break; }

            // MEASURED (round 3, item 44): the reference reads a stereo wavetable's LEFT channel and
            // discards the right silently, rather than averaging the two.
            for (int i = 0; i + channels <= read && written < mono.Length; i += channels)
            {
                mono[written++] = block[i];
            }
        }

        if (written < 1)
        {
            problem = "no samples could be decoded";
            return null;
        }

        if (written < mono.Length) { Array.Resize(ref mono, written); }
        return mono;
    }
}

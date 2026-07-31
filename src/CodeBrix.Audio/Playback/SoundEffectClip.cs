using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A short sound decoded once into memory and then played as many times as you like, including
/// many times at once.
/// </summary>
/// <remarks>
/// <para>
/// This is the type to reach for when the same sound fires repeatedly - a footstep, a laser, an
/// explosion. The file is decoded once, at load, into PCM at the shared output device's own
/// format; each <see cref="Play(float)"/> then costs a mixer voice reading from that buffer and
/// nothing else. No decoding happens while the game is running, and overlapping plays are just
/// more voices in the same mix rather than repeated decode work.
/// </para>
/// <para>
/// WAV, MP3, Ogg Vorbis and FLAC all load, and a clip's sample rate does not have to match the
/// output device's - the decode step resamples. That matters in practice because asset packs
/// mix rates freely, and it is the difference between "this pack works" and "these seventeen
/// files throw".
/// </para>
/// <para>
/// Cost to be aware of: decoded PCM is held in memory for the clip's lifetime, at four bytes per
/// sample per channel. That is the right trade for sound effects and the wrong one for a
/// soundtrack - use <see cref="AudioFilePlayer"/> for long audio, which streams it instead.
/// </para>
/// <example>
/// <code>
/// using var laser = SoundEffectClip.Load("laser.ogg");
/// laser.Play();          // fire and forget
/// laser.Play(0.4f);      // again, quieter, while the first is still sounding
/// </code>
/// </example>
/// </remarks>
public sealed class SoundEffectClip : IDisposable
{
    private readonly object lockObject = new object();
    private readonly List<SoundPlayer> activeVoices = new List<SoundPlayer>();

    private float[] samples;
    private AudioFormat format;
    private bool disposed;
    private bool disposeWhenIdle;

    private SoundEffectClip(float[] samples, AudioFormat format, TimeSpan duration)
    {
        this.samples = samples;
        this.format = format;
        Duration = duration;
    }

    /// <summary>Gets the clip's duration.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the sample rate the clip was decoded to - the shared output device's rate.</summary>
    public int SampleRate => format.SampleRate;

    /// <summary>Gets the channel count the clip was decoded to.</summary>
    public int Channels => format.Channels;

    /// <summary>Gets the number of plays of this clip that are still sounding.</summary>
    public int ActiveVoiceCount
    {
        get { lock (lockObject) { return activeVoices.Count; } }
    }

    /// <summary>
    /// Loads a sound effect from a file. The format is detected from the contents, not the
    /// extension.
    /// </summary>
    /// <param name="fileName">A .wav, .mp3, .ogg or .flac file.</param>
    /// <returns>The loaded clip.</returns>
    public static SoundEffectClip Load(string fileName)
    {
        if (fileName == null) throw new ArgumentNullException(nameof(fileName));

        using var stream = File.OpenRead(fileName);
        return Load(stream);
    }

    /// <summary>
    /// Loads a sound effect from the bytes of an audio file.
    /// </summary>
    /// <param name="data">The complete contents of a .wav, .mp3, .ogg or .flac file.</param>
    /// <returns>The loaded clip.</returns>
    public static SoundEffectClip Load(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        using var stream = new MemoryStream(data, false);
        return Load(stream);
    }

    /// <summary>
    /// Loads a sound effect from a stream. The stream is read to the end but not disposed, and
    /// is not referenced after this returns.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the start of an audio file.</param>
    /// <returns>The loaded clip.</returns>
    public static SoundEffectClip Load(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        // If the shared output has not started yet it adopts a rate from the first sound loaded,
        // so offer this file's own rate rather than letting it settle on nothing. Once it is
        // running, this value is ignored and the running rate wins.
        var desiredRate = SharedAudioOutput.SampleRate;
        if (desiredRate <= 0) desiredRate = GetNativeSampleRate(stream);

        // Decode to the device's format - this is where a clip recorded at any rate becomes
        // playable, and why Play costs nothing but mixing. Starting the device here also means
        // the first Play does not pay for it.
        var device = SharedAudioOutput.EnsureStarted(desiredRate);
        var deviceFormat = device.Format;

        AssetDataProvider source;
        try
        {
            source = new AssetDataProvider(device.Engine, deviceFormat, stream);
        }
        catch (NotSupportedException ex)
        {
            // The engine reports a missing codec by the container's format id ("ogg"), which is
            // baffling when the file you handed it was .opus. Say which codec it actually is.
            throw OggCodecSniffer.DescribeUndecodable(OggCodecSniffer.Identify(stream), ex) ?? ex;
        }

        using (source)
        {
            var decoded = ReadAllSamples(source, deviceFormat.Channels);
            var frames = decoded.Length / Math.Max(1, deviceFormat.Channels);
            var duration = deviceFormat.SampleRate > 0
                ? TimeSpan.FromSeconds((double)frames / deviceFormat.SampleRate)
                : TimeSpan.Zero;

            return new SoundEffectClip(decoded, deviceFormat, duration);
        }
    }

    /// <summary>
    /// Loads a sound, plays it once, and releases it when it finishes - the fire-and-forget case.
    /// </summary>
    /// <param name="fileName">A .wav, .mp3, .ogg or .flac file.</param>
    /// <param name="volume">Volume for this play, 0.0 to 1.0.</param>
    /// <remarks>
    /// Convenient for a sound played rarely, where holding the decoded audio would be waste. For a
    /// sound played repeatedly, keep a <see cref="SoundEffectClip"/> instead and call
    /// <see cref="Play(float)"/> on it: this overload decodes the file every time.
    /// </remarks>
    public static void PlayOnce(string fileName, float volume = 1.0f)
    {
        Load(fileName).PlayOnceAndRelease(volume);
    }

    /// <summary>
    /// Loads a sound from a stream, plays it once, and releases it when it finishes.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the start of an audio file.</param>
    /// <param name="volume">Volume for this play, 0.0 to 1.0.</param>
    public static void PlayOnce(Stream stream, float volume = 1.0f)
    {
        Load(stream).PlayOnceAndRelease(volume);
    }

    /// <summary>
    /// Loads a sound from the bytes of an audio file, plays it once, and releases it when it
    /// finishes.
    /// </summary>
    /// <param name="data">The complete contents of an audio file.</param>
    /// <param name="volume">Volume for this play, 0.0 to 1.0.</param>
    public static void PlayOnce(byte[] data, float volume = 1.0f)
    {
        Load(data).PlayOnceAndRelease(volume);
    }

    /// <summary>
    /// Plays the clip once, immediately, without waiting for any previous play to finish.
    /// </summary>
    /// <param name="volume">Volume for this play, 0.0 to 1.0.</param>
    public void Play(float volume = 1.0f)
    {
        lock (lockObject)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (samples.Length == 0) return;

            // Each play gets its own provider so the plays have independent positions; they all
            // read the same decoded buffer, which is never copied.
            var provider = new RawDataProvider(samples, format.SampleRate);
            var voice = new SoundPlayer(SharedAudioOutput.EnsureStarted(format.SampleRate).Engine, format, provider)
            {
                Volume = Math.Clamp(volume, 0f, 1f)
            };

            voice.PlaybackEnded += OnVoiceEnded;

            activeVoices.Add(voice);
            SharedAudioOutput.AddComponentToMixer(voice);
            voice.Play();
        }
    }

    /// <summary>
    /// Starts one play and disposes the clip once it finishes.
    /// </summary>
    /// <param name="volume">Volume for this play, 0.0 to 1.0.</param>
    /// <remarks>
    /// Disposing a clip stops it, so a fire-and-forget caller cannot simply dispose after calling
    /// <see cref="Play(float)"/> - it would cut the sound off. This arms the disposal instead, and
    /// the voice's own completion triggers it.
    /// </remarks>
    private void PlayOnceAndRelease(float volume)
    {
        lock (lockObject)
        {
            disposeWhenIdle = true;
            Play(volume);

            // Nothing to wait for (an empty clip, say): release it now.
            if (activeVoices.Count == 0) Dispose();
        }
    }

    /// <summary>
    /// Stops every play of this clip that is still sounding.
    /// </summary>
    public void StopAll()
    {
        lock (lockObject)
        {
            for (var i = activeVoices.Count - 1; i >= 0; i--) RetireVoice(activeVoices[i]);
        }
    }

    /// <summary>
    /// Stops any plays still sounding and releases the decoded audio.
    /// </summary>
    public void Dispose()
    {
        lock (lockObject)
        {
            if (disposed) return;

            StopAll();
            samples = [];
            disposed = true;
        }
    }

    // Fires on the engine's real-time audio thread, so it does the least possible work: the voice
    // is retired on a later Play or on Dispose rather than being torn down from here.
    private void OnVoiceEnded(object sender, EventArgs e)
    {
        if (sender is not SoundPlayer voice) return;

        lock (lockObject)
        {
            RetireVoice(voice);

            // A fire-and-forget play releases the clip when its last voice ends.
            if (disposeWhenIdle && activeVoices.Count == 0) Dispose();
        }
    }

    private void RetireVoice(SoundPlayer voice)
    {
        voice.PlaybackEnded -= OnVoiceEnded;
        activeVoices.Remove(voice);

        try { SharedAudioOutput.RemoveComponentFromMixer(voice); } catch (Exception) { /* output may be gone */ }
        try { voice.Dispose(); } catch (Exception) { /* also disposes the data provider */ }
    }

    /// <summary>
    /// Reads the file's own sample rate from its header, falling back to a sane default.
    /// </summary>
    private static int GetNativeSampleRate(Stream stream)
    {
        if (!stream.CanSeek) return 48000;

        var position = stream.Position;
        try
        {
            var native = AudioFormat.GetFormatFromStream(stream);
            if (native.HasValue && native.Value.SampleRate > 0) return native.Value.SampleRate;
        }
        catch (Exception)
        {
            // Unreadable header: let the device pick, and the decode will convert to it anyway.
        }
        finally
        {
            try { stream.Position = position; } catch (Exception) { /* non-seekable after all */ }
        }

        return 48000;
    }

    private static float[] ReadAllSamples(AssetDataProvider source, int channels)
    {
        var length = source.Length;
        if (length > 0)
        {
            var exact = new float[length];
            var filled = 0;
            while (filled < exact.Length)
            {
                var read = source.ReadBytes(exact.AsSpan(filled));
                if (read <= 0) break;
                filled += read;
            }

            return filled == exact.Length ? exact : exact[..filled];
        }

        // No declared length: grow as we go, which only happens for sources whose decoder cannot
        // state a length up front.
        var buffer = new float[Math.Max(channels, 1) * 4096];
        var total = 0;
        while (true)
        {
            if (total == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);

            var read = source.ReadBytes(buffer.AsSpan(total));
            if (read <= 0) break;
            total += read;
        }

        return buffer[..total];
    }
}

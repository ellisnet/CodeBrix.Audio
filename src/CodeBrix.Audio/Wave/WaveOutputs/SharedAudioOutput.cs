using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Abstracts.Devices;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// The process-wide audio output that every <see cref="WaveOutEvent"/> shares. It owns a single
/// engine, a single playback device, and that device's master mixer; each <see cref="WaveOutEvent"/>
/// is a <em>voice</em> mixed into that one device rather than a device of its own. This lets an
/// application overlap many sounds (a firefight of laser shots and an explosion) as cheap mixer
/// voices instead of opening a separate hardware device per sound.
/// </summary>
/// <remarks>
/// <para>
/// Almost every consumer can ignore this class entirely: the shared output starts itself the first
/// time a <see cref="WaveOutEvent"/> plays, adopting that first sound's sample rate, and runs at
/// 32-bit float stereo. Playing a single WAV needs no interaction with it.
/// </para>
/// <para>
/// Applications that overlap many sounds (for example a game engine) should call
/// <see cref="Configure"/> once at start-up to pin the output sample rate to the rate their sound
/// effects are authored at, so no source is ever rejected for a sample-rate mismatch. Because the
/// output has no built-in resampler, a source whose sample rate differs from the running output is
/// rejected (see <see cref="WaveOutEvent.Init"/>); pre-convert such sources, or standardise on one
/// rate.
/// </para>
/// </remarks>
public static class SharedAudioOutput
{
    private static readonly object Gate = new object();
    private static readonly List<WaveOutEvent> Players = new List<WaveOutEvent>();
    private static readonly List<ICodecFactory> ExtraCodecFactories = new List<ICodecFactory>();
    private static readonly List<IPacketCodecFactory> ExtraPacketCodecFactories = new List<IPacketCodecFactory>();

    private static MiniAudioEngine _engine;
    private static AudioPlaybackDevice _device;
    private static Timer _sweepTimer;
    private static bool _running;

    private static int _configuredSampleRate;
    private static int _configuredChannels = 2;

    // How often the sweep thread reclaims finished voices and raises their PlaybackStopped. A few
    // tens of milliseconds is imperceptible for a stop notification and keeps the sweep cheap.
    private const int SweepIntervalMilliseconds = 25;

    // The rate the output starts at when packet audio is the first thing to reach it and nothing was
    // configured. 48 kHz is what video containers carry (it is Opus's only rate), so the common case
    // needs no conversion at all.
    private const int DefaultPacketSampleRate = 48000;

    /// <summary>Whether the shared engine and playback device are currently running.</summary>
    public static bool IsRunning
    {
        get { lock (Gate) { return _running; } }
    }

    /// <summary>
    /// The sample rate the shared output is (or will be) running at: the running device's rate when
    /// started, otherwise the rate set by <see cref="Configure"/>, otherwise <c>0</c> (meaning it will
    /// adopt the first sound's rate).
    /// </summary>
    public static int SampleRate
    {
        get { lock (Gate) { return _running ? _device.Format.SampleRate : _configuredSampleRate; } }
    }

    /// <summary>The channel count the shared output runs at (2 = stereo unless reconfigured).</summary>
    public static int Channels
    {
        get { lock (Gate) { return _running ? _device.Format.Channels : _configuredChannels; } }
    }

    /// <summary>
    /// Pins the shared output's format before playback starts. Call once at application start-up (for
    /// example a game pinning 48 kHz stereo to match its sound effects). Has no effect on an
    /// already-running output — call <see cref="Shutdown"/> first to change a running output.
    /// </summary>
    /// <param name="sampleRate">The output sample rate in Hz (for example 44100 or 48000).</param>
    /// <param name="channels">The output channel count: 1 (mono) or 2 (stereo). Defaults to stereo.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> is not positive, or <paramref name="channels"/> is not 1 or 2.
    /// </exception>
    /// <exception cref="InvalidOperationException">The shared output is already running.</exception>
    public static void Configure(int sampleRate, int channels = 2)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }
        if (channels != 1 && channels != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only 1 (mono) or 2 (stereo) channels are supported.");
        }

        lock (Gate)
        {
            if (_running)
            {
                throw new InvalidOperationException(
                    "The shared audio output is already running; call Shutdown() before reconfiguring it.");
            }
            _configuredSampleRate = sampleRate;
            _configuredChannels = channels;
        }
    }

    /// <summary>
    /// Adds an audio codec to the shared output, so that everything playing through it -
    /// <see cref="WaveOutEvent"/>, <see cref="CodeBrix.Audio.Playback.AudioFilePlayer"/>,
    /// <see cref="CodeBrix.Audio.Playback.SoundEffectClip"/> - can decode that format.
    /// </summary>
    /// <param name="factory">The codec factory to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This is the extension point for add-on packages that carry a decoder CodeBrix.Audio does
    /// not ship itself - a separately licensed codec, for instance. Call it once at start-up,
    /// before playing anything:
    /// </para>
    /// <code>
    /// SharedAudioOutput.RegisterCodecFactory(new SomeFormatCodecFactory());
    /// </code>
    /// <para>
    /// The registration is remembered for the lifetime of the process, not just of the current
    /// device: it is re-applied to every engine the shared output starts, so it survives
    /// <see cref="Shutdown"/>. Registering the same factory instance twice is harmless - the
    /// second call is ignored.
    /// </para>
    /// <para>
    /// Codecs registered here are consulted in priority order alongside the built-in ones. A
    /// factory should return null for a stream it cannot handle rather than throwing, so that the
    /// remaining factories still get their turn - several formats share one format identifier
    /// (everything in an Ogg container reports "ogg", whatever codec is inside it), so declining
    /// cleanly is how they coexist.
    /// </para>
    /// </remarks>
    public static void RegisterCodecFactory(ICodecFactory factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (Gate)
        {
            if (ExtraCodecFactories.Contains(factory))
            {
                return;
            }

            ExtraCodecFactories.Add(factory);

            // A device that is already running gets it immediately; otherwise EnsureStarted
            // applies the whole list when it builds the engine.
            if (_running)
            {
                _engine.RegisterCodecFactory(factory);
            }
        }
    }

    /// <summary>
    /// The codec factories added with <see cref="RegisterCodecFactory"/>, in registration order.
    /// </summary>
    /// <remarks>
    /// Does not include the built-in native and managed codecs, which are always present.
    /// </remarks>
    public static IReadOnlyList<ICodecFactory> RegisteredCodecFactories
    {
        get { lock (Gate) { return ExtraCodecFactories.ToArray(); } }
    }

    /// <summary>
    /// Adds a PACKET codec to the shared output, so that <see cref="CodeBrix.Audio.Playback.PacketAudioPlayer"/>
    /// can decode audio a media container delivers as loose packets.
    /// </summary>
    /// <param name="factory">The packet codec factory to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This is the packet-level sibling of <see cref="RegisterCodecFactory"/> and behaves the same
    /// way: call it once at start-up, the registration is remembered for the lifetime of the
    /// PROCESS (so it survives <see cref="Shutdown"/> and is re-applied to every engine the shared
    /// output starts), and registering the same factory instance twice is harmless - the second call
    /// is ignored. Keep one instance per add-on package for that reason.
    /// </para>
    /// <code>
    /// SharedAudioOutput.RegisterPacketCodecFactory(new SomeFormatPacketCodecFactory());
    /// </code>
    /// <para>
    /// The two seams are separate because they answer different questions: a stream factory is asked
    /// "can you open this Ogg file?", a packet factory "can you decode packets of codec 'vorbis'?".
    /// A package that does both registers with both.
    /// </para>
    /// </remarks>
    public static void RegisterPacketCodecFactory(IPacketCodecFactory factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (Gate)
        {
            if (ExtraPacketCodecFactories.Contains(factory))
            {
                return;
            }

            ExtraPacketCodecFactories.Add(factory);

            // A device that is already running gets it immediately; otherwise EnsureStarted
            // applies the whole list when it builds the engine.
            if (_running)
            {
                _engine.RegisterPacketCodecFactory(factory);
            }
        }
    }

    /// <summary>
    /// The packet codec factories added with <see cref="RegisterPacketCodecFactory"/>, in registration
    /// order.
    /// </summary>
    /// <remarks>
    /// Does not include the built-in managed packet codecs, which are always present.
    /// </remarks>
    public static IReadOnlyList<IPacketCodecFactory> RegisteredPacketCodecFactories
    {
        get { lock (Gate) { return ExtraPacketCodecFactories.ToArray(); } }
    }

    /// <summary>
    /// Whether a packet decoder for <paramref name="codecId"/> WOULD be available on the shared
    /// output - without starting the shared output or touching the audio device.
    /// </summary>
    /// <param name="codecId">The codec identifier to ask about, matched case-insensitively.</param>
    /// <returns>
    /// True when a packet codec factory serving that codec is built into this package or has been
    /// added with <see cref="RegisterPacketCodecFactory"/>; false otherwise, including for a null or
    /// empty identifier.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the question <see cref="CreatePacketDecoder"/> cannot answer cheaply:
    /// <see cref="CreatePacketDecoder"/> resolves through the running engine, so merely asking it
    /// opens the audio device. Use this when availability is all that is wanted - deciding whether
    /// to offer a track, choosing between two audio streams in a container, telling a user what is
    /// missing - and <see cref="CreatePacketDecoder"/> only when a decoder is actually going to be
    /// used.
    /// </para>
    /// <para>
    /// IT ANSWERS FOR THE SHARED OUTPUT ONLY. A factory registered directly on some
    /// <see cref="AudioEngine"/> instance with <see cref="AudioEngine.RegisterPacketCodecFactory"/>
    /// is invisible here, because that engine is not the shared output's. Register through
    /// <see cref="RegisterPacketCodecFactory"/> to be seen by both.
    /// </para>
    /// <para>
    /// It is a question about the SEAM, not about a particular track: a factory may still decline a
    /// specific piece of codec-private data (wrong shape, an unsupported profile), which is why
    /// <see cref="CreatePacketDecoder"/> can still throw for a codec this reports as supported.
    /// </para>
    /// </remarks>
    public static bool IsPacketCodecSupported(string codecId)
    {
        if (string.IsNullOrEmpty(codecId))
        {
            return false;
        }

        foreach (var factory in ManagedCodecs.BuiltInPacketCodecFactories)
        {
            if (ServesCodec(factory, codecId))
            {
                return true;
            }
        }

        lock (Gate)
        {
            foreach (var factory in ExtraPacketCodecFactories)
            {
                if (ServesCodec(factory, codecId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Every codec identifier <see cref="IsPacketCodecSupported"/> answers true for: the packet
    /// codecs built into this package first, then those added with
    /// <see cref="RegisterPacketCodecFactory"/> in registration order. Reading it does not start the
    /// shared output.
    /// </summary>
    /// <remarks>
    /// Identifiers are reported exactly as their factories declare them, de-duplicated
    /// case-insensitively, so a codec two factories both serve appears once. Useful for a diagnostic
    /// listing or a message that says what a package could not play and what it can.
    /// </remarks>
    public static IReadOnlyCollection<string> SupportedPacketCodecIds
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new List<string>();

            foreach (var factory in ManagedCodecs.BuiltInPacketCodecFactories)
            {
                CollectCodecIds(factory, seen, ids);
            }

            lock (Gate)
            {
                foreach (var factory in ExtraPacketCodecFactories)
                {
                    CollectCodecIds(factory, seen, ids);
                }
            }

            return ids.AsReadOnly();
        }
    }

    // Whether one factory declares the codec, matched the way the engine's packet registry matches.
    private static bool ServesCodec(IPacketCodecFactory factory, string codecId)
    {
        var supported = factory == null ? null : factory.SupportedCodecIds;
        if (supported == null)
        {
            return false;
        }

        foreach (var id in supported)
        {
            if (string.Equals(id, codecId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Adds one factory's codec identifiers to the running list, skipping ones already there.
    private static void CollectCodecIds(IPacketCodecFactory factory, HashSet<string> seen, List<string> ids)
    {
        var supported = factory == null ? null : factory.SupportedCodecIds;
        if (supported == null)
        {
            return;
        }

        foreach (var id in supported)
        {
            if (!string.IsNullOrEmpty(id) && seen.Add(id))
            {
                ids.Add(id);
            }
        }
    }

    /// <summary>
    /// Creates a decoder for audio that arrives as container packets, using the codecs registered
    /// with the shared output.
    /// </summary>
    /// <param name="codecId">The codec identifier, lowercase - for example "vorbis" or "opus".</param>
    /// <param name="codecPrivate">
    /// The codec's initialisation data exactly as the container carried it: the three Xiph-laced
    /// setup headers for Vorbis, the identification-header bytes for Opus.
    /// </param>
    /// <param name="hint">An optional hint describing the format the caller would like back.</param>
    /// <returns>A decoder for the codec; dispose it when finished with it.</returns>
    /// <exception cref="ArgumentException"><paramref name="codecId"/> is null or empty.</exception>
    /// <exception cref="NotSupportedException">No registered packet codec factory serves that codec.</exception>
    /// <remarks>
    /// <para>
    /// <see cref="CodeBrix.Audio.Playback.PacketAudioPlayer"/> calls this for you; it is public
    /// because an application decoding packets for its own purposes - measuring them, mixing them
    /// itself - needs the same door.
    /// </para>
    /// <para>
    /// The codec registry belongs to the running engine, so this STARTS the shared output if it is
    /// not already running, at 48 kHz unless <see cref="Configure"/> pinned a rate - which means it
    /// opens the audio device. An application that plays audio at another rate should call
    /// <see cref="Configure"/> at start-up.
    /// </para>
    /// <para>
    /// A CALLER THAT ONLY WANTS TO KNOW WHETHER A CODEC IS AVAILABLE MUST NOT ASK THIS. Use
    /// <see cref="IsPacketCodecSupported"/>, which answers the same question about the same
    /// factories and starts nothing.
    /// </para>
    /// </remarks>
    public static IPacketSoundDecoder CreatePacketDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate,
        AudioFormat? hint = null)
    {
        if (string.IsNullOrEmpty(codecId))
        {
            throw new ArgumentException("A codec identifier is required.", nameof(codecId));
        }

        var device = EnsureStarted(DefaultPacketSampleRate);

        try
        {
            return device.Engine.CreatePacketDecoder(codecId, codecPrivate, hint);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(
                $"Audio codec '{codecId}' has no registered packet decoder. Register one with " +
                "SharedAudioOutput.RegisterPacketCodecFactory(...); the decoder for a codec this " +
                "package does not carry lives in an add-on package.", ex);
        }
    }

    /// <summary>
    /// Stops and releases the shared engine, playback device, and sweep timer, and clears any format
    /// set by <see cref="Configure"/>. Any voices still in the mixer are dropped. Useful at application
    /// shutdown and for test isolation; the output restarts automatically (unconfigured) the next time
    /// a <see cref="WaveOutEvent"/> plays.
    /// </summary>
    public static void Shutdown()
    {
        Timer timer = null;
        AudioPlaybackDevice device = null;
        MiniAudioEngine engine = null;

        lock (Gate)
        {
            // Always return to the pristine, unconfigured state (even when the device never started),
            // so a later start adopts the first sound's rate again unless the caller reconfigures.
            _configuredSampleRate = 0;
            _configuredChannels = 2;

            if (_running)
            {
                Players.Clear();
                timer = _sweepTimer;
                device = _device;
                engine = _engine;
                _sweepTimer = null;
                _device = null;
                _engine = null;
                _running = false;
            }
        }

        // Tear down outside the lock; disposing the engine cascades to its active devices.
        if (timer != null)
        {
            timer.Dispose();
        }
        try { if (device != null) { device.Stop(); } } catch (Exception) { /* device may already be stopping */ }
        try { if (device != null) { device.Dispose(); } } catch (Exception) { /* best effort */ }
        try { if (engine != null) { engine.Dispose(); } } catch (Exception) { /* best effort */ }
    }

    // The rate a source of the given rate would play at: the running device rate, else the configured
    // rate, else the source's own rate (which the output will adopt when it starts).
    internal static int EffectiveSampleRate(int sourceSampleRate)
    {
        lock (Gate)
        {
            if (_running)
            {
                return _device.Format.SampleRate;
            }
            return _configuredSampleRate > 0 ? _configuredSampleRate : sourceSampleRate;
        }
    }

    // The channel count a source would be matched to (running device channels, else configured).
    internal static int EffectiveChannels()
    {
        lock (Gate)
        {
            return _running ? _device.Format.Channels : _configuredChannels;
        }
    }

    // Starts the shared engine + playback device if they are not already running, adopting
    // desiredSampleRate when no explicit rate was configured. Returns the running device.
    internal static AudioPlaybackDevice EnsureStarted(int desiredSampleRate)
    {
        lock (Gate)
        {
            if (_running)
            {
                return _device;
            }

            int rate = _configuredSampleRate > 0 ? _configuredSampleRate : desiredSampleRate;
            int channels = _configuredChannels;
            var format = new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = channels,
                Layout = AudioFormat.GetLayoutFromChannels(channels),
                SampleRate = rate,
            };

            var engine = new MiniAudioEngine();
            AudioPlaybackDevice device;
            try
            {
                // The managed Ogg Vorbis and FLAC decoders register BELOW the engine's native
                // factory, so they change nothing where the bundled native library can decode
                // those formats itself. They matter on a platform whose native binary predates
                // Ogg Vorbis support, or has none at all: without them an .ogg would simply fail
                // to open there, which is not a distinction an application should have to know
                // about.
                ManagedCodecs.RegisterAll(engine);

                // Then anything an add-on package registered (see RegisterCodecFactory). The list
                // outlives any single engine, so a codec registered once keeps working across a
                // Shutdown and restart.
                foreach (var factory in ExtraCodecFactories)
                {
                    engine.RegisterCodecFactory(factory);
                }

                // Same again for the packet seam: the built-in managed packet codecs went in with
                // ManagedCodecs.RegisterAll above - from ManagedCodecs.BuiltInPacketCodecFactories,
                // which is also what IsPacketCodecSupported consults, so the probe cannot disagree
                // with what is registered here - and anything an add-on package registered is
                // applied on top of them.
                foreach (var packetFactory in ExtraPacketCodecFactories)
                {
                    engine.RegisterPacketCodecFactory(packetFactory);
                }

                device = engine.InitializePlaybackDevice(null, format);
                device.Start();
            }
            catch (Exception)
            {
                engine.Dispose();
                throw;
            }

            _engine = engine;
            _device = device;
            _sweepTimer = new Timer(Sweep, null, SweepIntervalMilliseconds, SweepIntervalMilliseconds);
            _running = true;
            return _device;
        }
    }

    // Adds a player's voice to the master mixer and registers the player for end-of-stream sweeping.
    internal static void AddPlayer(WaveOutEvent player, SampleSourceVoice voice)
    {
        lock (Gate)
        {
            if (!_running)
            {
                throw new InvalidOperationException("The shared audio output is not running.");
            }
            if (!Players.Contains(player))
            {
                Players.Add(player);
            }
            _device.MasterMixer.AddComponent(voice);
        }
    }

    // Removes a player's voice from the master mixer and unregisters the player.
    internal static void RemovePlayer(WaveOutEvent player, SampleSourceVoice voice)
    {
        lock (Gate)
        {
            Players.Remove(player);
            if (_running && voice != null)
            {
                _device.MasterMixer.RemoveComponent(voice);
            }
        }
    }

    // Adds an arbitrary component (e.g. an engine SoundPlayer used by AudioFilePlayer) to the shared
    // master mixer. The caller manages the component's own lifecycle and end-of-stream handling; it is
    // NOT registered for the WaveOutEvent end-of-stream sweep.
    internal static void AddComponentToMixer(SoundComponent component)
    {
        lock (Gate)
        {
            if (!_running)
            {
                throw new InvalidOperationException("The shared audio output is not running.");
            }
            _device.MasterMixer.AddComponent(component);
        }
    }

    // Removes a component previously added with AddComponentToMixer.
    internal static void RemoveComponentFromMixer(SoundComponent component)
    {
        lock (Gate)
        {
            if (_running && component != null)
            {
                _device.MasterMixer.RemoveComponent(component);
            }
        }
    }

    // Sweep thread: reclaim any voices whose source has ended, off the real-time audio thread.
    private static void Sweep(object state)
    {
        WaveOutEvent[] snapshot;
        lock (Gate)
        {
            if (!_running || Players.Count == 0)
            {
                return;
            }
            snapshot = Players.ToArray();
        }

        foreach (var player in snapshot)
        {
            player.PollEndOfStream();
        }
    }
}

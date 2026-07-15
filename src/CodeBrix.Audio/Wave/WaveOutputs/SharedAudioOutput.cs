using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Abstracts.Devices;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
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

    private static MiniAudioEngine _engine;
    private static AudioPlaybackDevice _device;
    private static Timer _sweepTimer;
    private static bool _running;

    private static int _configuredSampleRate;
    private static int _configuredChannels = 2;

    // How often the sweep thread reclaims finished voices and raises their PlaybackStopped. A few
    // tens of milliseconds is imperceptible for a stop notification and keeps the sweep cheap.
    private const int SweepIntervalMilliseconds = 25;

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

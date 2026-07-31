using System;
using System.IO;
using System.Threading;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Wave;
using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A long-running player for MIDI music rendered through a SoundFont or an SFZ instrument, with the
/// same transport controls as <see cref="AudioFilePlayer"/>: play/pause/stop, volume, looping, seek to
/// a timecode, and a readable position and duration.
/// </summary>
/// <remarks>
/// <para>
/// This is the counterpart to <see cref="AudioFilePlayer"/> for music that is synthesized rather than
/// decoded. Load an instrument - a <c>.sf2</c> SoundFont or a <c>.sfz</c> instrument - and a MIDI
/// sequence, then drive it exactly as you would a file player. The format decides which synthesizer
/// renders; the transport is identical either way. The music mixes into the process-wide
/// <see cref="SharedAudioOutput"/> alongside everything else.
/// </para>
/// <para>
/// Instruments are large. Load them through a <see cref="SoundFontCache"/> or
/// <see cref="SfzInstrumentCache"/> and share one instance across every player rather than reloading
/// per track - see the <c>Load</c> overloads that take an instrument instance.
/// </para>
/// <para>
/// The synthesizer is created at the output device's sample rate, so nothing is resampled. Rendering
/// happens on the engine's real-time audio thread while transport calls arrive from yours; the two are
/// serialized internally, because the underlying synthesizer is not thread-safe.
/// <see cref="PlaybackEnded"/> is raised when a non-looping sequence reaches its end, on the
/// <see cref="SynchronizationContext"/> captured at load if there is one.
/// </para>
/// <para>Dispose when finished.</para>
/// </remarks>
public sealed class MidiMusicPlayer : IDisposable
{
    private readonly object _lock = new object();

    private IMidiSynthesizer _synthesizer;
    private MidiSynthDataProvider _provider;
    private SoundPlayer _player;
    private SynchronizationContext _syncContext;
    private MidiSequence _sequence;
    private float _volume = 1.0f;
    private bool _isLooping;
    private bool _disposed;

    /// <summary>
    /// Raised when a non-looping sequence reaches its end. Not raised for <see cref="Stop"/>, and not
    /// raised while <see cref="IsLooping"/> is set. Raised on the <see cref="SynchronizationContext"/>
    /// captured when the sequence was loaded, if there is one; otherwise on a background thread.
    /// </summary>
    public event EventHandler PlaybackEnded;

    /// <summary>Whether a SoundFont and sequence are loaded and ready to play.</summary>
    public bool IsLoaded
    {
        get { lock (_lock) { return _player != null; } }
    }

    /// <summary>The current playback position. <see cref="TimeSpan.Zero"/> if nothing is loaded.</summary>
    public TimeSpan Position
    {
        get { lock (_lock) { return _provider == null ? TimeSpan.Zero : _provider.CurrentTime; } }
    }

    /// <summary>The total length of the loaded sequence. <see cref="TimeSpan.Zero"/> if nothing is loaded.</summary>
    public TimeSpan Duration
    {
        get { lock (_lock) { return _sequence == null ? TimeSpan.Zero : _sequence.Length; } }
    }

    /// <summary>The current playback state (Stopped / Playing / Paused).</summary>
    public PlaybackState PlaybackState
    {
        get { lock (_lock) { return _player == null ? PlaybackState.Stopped : Map(_player.State); } }
    }

    /// <summary>Playback volume, where 1.0 is unity gain. Persists across loads.</summary>
    public float Volume
    {
        get { lock (_lock) { return _volume; } }
        set
        {
            lock (_lock)
            {
                _volume = value;
                if (_player != null)
                {
                    _player.Volume = value;
                }
            }
        }
    }

    /// <summary>
    /// Whether the sequence repeats. Loop points come from the sequence itself (see
    /// <see cref="MidiSequenceLoopType"/>); a sequence with no loop point repeats from the start.
    /// Persists across loads.
    /// </summary>
    public bool IsLooping
    {
        get { lock (_lock) { return _isLooping; } }
        set
        {
            lock (_lock)
            {
                _isLooping = value;
                _provider?.SetLooping(value);
            }
        }
    }

    /// <summary>The number of voices currently sounding. Useful for diagnostics and polyphony tuning.</summary>
    public int ActiveVoiceCount
    {
        get { lock (_lock) { return _synthesizer == null ? 0 : _synthesizer.ActiveVoiceCount; } }
    }

    /// <summary>
    /// Loads an instrument and a MIDI file by path, positioned at the start and stopped. The
    /// instrument's extension decides the synthesizer: <c>.sfz</c> loads an SFZ instrument, anything
    /// else a SoundFont.
    /// </summary>
    /// <param name="instrumentPath">Path to a <c>.sf2</c> or <c>.sfz</c> file.</param>
    /// <param name="midiFilePath">Path to a Standard MIDI File.</param>
    /// <exception cref="ArgumentNullException">Either path is null.</exception>
    public void Load(string instrumentPath, string midiFilePath)
    {
        if (instrumentPath == null)
        {
            throw new ArgumentNullException(nameof(instrumentPath));
        }

        if (midiFilePath == null)
        {
            throw new ArgumentNullException(nameof(midiFilePath));
        }

        if (string.Equals(Path.GetExtension(instrumentPath), ".sfz", StringComparison.OrdinalIgnoreCase))
        {
            Load(new SfzInstrument(instrumentPath), new MidiSequence(midiFilePath));
        }
        else
        {
            Load(new SoundFont(instrumentPath), new MidiSequence(midiFilePath));
        }
    }

    /// <summary>
    /// Loads a shared SoundFont and a MIDI sequence. This is the overload to prefer: the SoundFont can
    /// come from a <see cref="SoundFontCache"/> and be shared across every player in the process.
    /// </summary>
    /// <param name="soundFont">The SoundFont to render with.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Load(SoundFont soundFont, MidiSequence sequence)
    {
        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        LoadCore(rate => new SoundFontSynthesizer(soundFont, rate), sequence);
    }

    /// <summary>
    /// Loads a shared SFZ instrument and a MIDI sequence. This is the overload to prefer for SFZ: the
    /// instrument can come from an <see cref="SfzInstrumentCache"/> and be shared across every player
    /// in the process.
    /// </summary>
    /// <param name="instrument">The SFZ instrument to render with.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Load(SfzInstrument instrument, MidiSequence sequence)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        LoadCore(rate => new SfzSynthesizer(instrument, rate), sequence);
    }

    private void LoadCore(Func<int, IMidiSynthesizer> createSynthesizer, MidiSequence sequence)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            TearDown();

            // Start the shared device first, then build the synthesizer at whatever rate it settled on.
            // Matching the device rate exactly is what keeps this path resampler-free.
            var device = SharedAudioOutput.EnsureStarted(48000);
            var deviceRate = SharedAudioOutput.SampleRate;

            IMidiSynthesizer synthesizer = null;
            MidiSynthDataProvider provider = null;
            SoundPlayer player = null;
            try
            {
                synthesizer = createSynthesizer(deviceRate);
                provider = new MidiSynthDataProvider(synthesizer);
                provider.Start(sequence, _isLooping);

                player = new SoundPlayer(device.Engine, device.Format, provider)
                {
                    Volume = _volume,

                    // Looping is the sequencer's job, not the engine's: a MIDI sequence loops at its own
                    // loop point, which is rarely the end of the stream.
                    IsLooping = false,
                };
                player.PlaybackEnded += OnEnginePlaybackEnded;
                SharedAudioOutput.AddComponentToMixer(player);
            }
            catch (Exception)
            {
                if (player != null)
                {
                    try { player.Dispose(); } catch (Exception) { /* best effort */ }
                }
                else
                {
                    try { provider?.Dispose(); } catch (Exception) { /* best effort */ }
                }
                throw;
            }

            _synthesizer = synthesizer;
            _provider = provider;
            _player = player;
            _sequence = sequence;

            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
            }
        }
    }

    /// <summary>Starts or resumes playback from the current position.</summary>
    /// <exception cref="InvalidOperationException">Nothing is loaded.</exception>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();
            _player.Play();
        }
    }

    /// <summary>Pauses playback, keeping the current position.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }
            _player.Pause();
        }
    }

    /// <summary>Stops playback, silences all voices, and rewinds to the start.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }

            _player.Stop();

            // Rewind by restarting the sequence: a stopped MIDI player should be at bar one with no
            // note, controller or pitch-bend state left over from where it was interrupted.
            if (_sequence != null)
            {
                _provider.Start(_sequence, _isLooping);
            }
        }
    }

    /// <summary>
    /// Seeks to a timecode. May be called while playing or stopped.
    /// </summary>
    /// <param name="position">The position to seek to, from the start of the sequence.</param>
    /// <remarks>
    /// Controller state up to <paramref name="position"/> is replayed so the instruments sound correct,
    /// but notes already sounding at that point do not resume - see <see cref="MidiSequencer.Seek"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing is loaded.</exception>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();

            if (position < TimeSpan.Zero)
            {
                position = TimeSpan.Zero;
            }

            _provider.Seek((int)(position.TotalSeconds * _synthesizer.SampleRate));
        }
    }

    /// <summary>Stops playback and releases the synthesizer. The SoundFont itself is not disposed.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TearDown();
        }
    }

    // Fires on the engine's real-time audio thread; marshal off it before raising the public event.
    private void OnEnginePlaybackEnded(object sender, EventArgs e)
    {
        var handler = PlaybackEnded;
        if (handler == null)
        {
            return;
        }

        var context = _syncContext;
        if (context != null)
        {
            context.Post(_ => handler(this, EventArgs.Empty), null);
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }

    // Removes and disposes the current player chain. Callers hold _lock.
    private void TearDown()
    {
        if (_player != null)
        {
            _player.PlaybackEnded -= OnEnginePlaybackEnded;
            try { SharedAudioOutput.RemoveComponentFromMixer(_player); } catch (Exception) { /* output may be torn down */ }
            try { _player.Dispose(); } catch (Exception) { /* also disposes the data provider */ }
            _player = null;
        }

        if (_provider != null)
        {
            try { _provider.Dispose(); } catch (Exception) { /* best effort */ }
            _provider = null;
        }

        _synthesizer = null;
        _sequence = null;
    }

    private static PlaybackState Map(EnginePlaybackState state)
    {
        switch (state)
        {
            case EnginePlaybackState.Playing:
                return PlaybackState.Playing;
            case EnginePlaybackState.Paused:
                return PlaybackState.Paused;
            default:
                return PlaybackState.Stopped;
        }
    }

    private void RequireLoaded()
    {
        if (_player == null)
        {
            throw new InvalidOperationException(
                "No MIDI sequence is loaded. Call Load(...) before using the transport.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MidiMusicPlayer));
        }
    }
}

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
    private MidiSequencer.MessageHook _messageFilter;
    private MidiMessageObserver _messageObserver;
    private float _volume = 1.0f;
    private float _speed = 1.0f;
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
    /// The sequence currently loaded, or <see langword="null"/> if nothing is loaded.
    /// </summary>
    /// <remarks>
    /// Chiefly useful after <see cref="Load(string, string)"/>, which builds the sequence itself and
    /// would otherwise leave the caller without a reference to it.
    /// </remarks>
    public MidiSequence Sequence
    {
        get { lock (_lock) { return _sequence; } }
    }

    /// <summary>
    /// The playback speed multiplier: 1.0 is the sequence's own tempo, 0.5 is half speed, 2.0 is
    /// double speed. Must not be negative. Persists across loads.
    /// </summary>
    /// <remarks>
    /// This scales the tempo without changing pitch - the synthesizer still renders every note at its
    /// written frequency, the sequence just advances more slowly or quickly. A value of 0 freezes the
    /// transport while leaving sounding voices to ring out.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public float Speed
    {
        get { lock (_lock) { return _speed; } }
        set
        {
            if (value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The playback speed must not be negative.");
            }

            lock (_lock)
            {
                _speed = value;
                if (_provider != null)
                {
                    _provider.Speed = value;
                }
            }
        }
    }

    /// <summary>
    /// An observe-only callback raised after each MIDI message is delivered to the synthesizer -
    /// the hook for making something outside the audio react to the music. Persists across loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is almost always the one you want. It cannot change what is played, so there is no way to
    /// silence the music with it. See <see cref="MidiMessageFilter"/> for the hook that CAN.
    /// </para>
    /// <para>
    /// It runs on the real-time AUDIO THREAD - see <see cref="MidiMessageObserver"/> for the rules
    /// that come with that. In particular, do not call back into this player from it.
    /// </para>
    /// </remarks>
    public MidiMessageObserver MidiMessageProcessed
    {
        get { lock (_lock) { return _messageObserver; } }
        set
        {
            lock (_lock)
            {
                _messageObserver = value;
                if (_provider != null)
                {
                    _provider.MessageObserver = value;
                }
            }
        }
    }

    /// <summary>
    /// A hook that REPLACES delivery of each MIDI message to the synthesizer, for transposing,
    /// re-channelling or suppressing messages as they play. Persists across loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// READ THIS BEFORE SETTING IT. While this is non-null the player does NOT deliver messages
    /// itself - your hook owns delivery. A hook that inspects a message and returns without calling
    /// <see cref="IMidiSynthesizer.ProcessMidiMessage"/> on the synthesizer it was handed silences
    /// the music completely, which looks like a bug in the player rather than in the hook. To merely
    /// WATCH messages, use <see cref="MidiMessageProcessed"/>, which cannot do this.
    /// </para>
    /// <para>
    /// The synthesizer passed to the hook is safe to use FROM INSIDE THE HOOK ONLY - the lock that
    /// serializes it against rendering is held for the duration of the call. Never store it and use
    /// it later; use <see cref="SendMidiMessage"/> for that, which takes the lock properly.
    /// </para>
    /// <para>It runs on the real-time audio thread; the same speed and allocation rules apply.</para>
    /// </remarks>
    public MidiSequencer.MessageHook MidiMessageFilter
    {
        get { lock (_lock) { return _messageFilter; } }
        set
        {
            lock (_lock)
            {
                _messageFilter = value;
                if (_provider != null)
                {
                    _provider.MessageFilter = value;
                }
            }
        }
    }

    /// <summary>
    /// Sends a MIDI message to the synthesizer alongside the sequence that is playing - the general
    /// form of the per-channel helpers below.
    /// </summary>
    /// <param name="channel">The channel to send to, 0-15.</param>
    /// <param name="command">The command nibble: 0x80 note-off, 0x90 note-on, 0xB0 control change, 0xC0 program change, 0xE0 pitch bend.</param>
    /// <param name="data1">The first data byte, 0-127.</param>
    /// <param name="data2">The second data byte, 0-127. Ignored by commands that take one byte.</param>
    /// <remarks>
    /// Safe to call from any thread at any time: the call is serialized against the rendering that
    /// happens on the audio thread, which is why this exists rather than a property handing back the
    /// synthesizer itself (an <see cref="IMidiSynthesizer"/> is not thread-safe, and the lock that
    /// makes it safe here is not reachable from outside). Does nothing when nothing is loaded.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SendMidiMessage(int channel, int command, int data1, int data2)
    {
        RequireChannel(channel);

        lock (_lock)
        {
            if (_disposed || _provider == null)
            {
                return;
            }

            _provider.SendMidiMessage(channel, command, data1, data2);
        }
    }

    /// <summary>
    /// Sets one channel's volume, as MIDI control change 7. This is how a layered arrangement is
    /// mixed live - fade a channel in or out and the rest of the sequence plays on unchanged.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="volume">The volume, 0.0 (silent) to 1.0 (full). Clamped.</param>
    /// <remarks>
    /// The sequence's own control-change 7 messages still apply: a track that automates its volume
    /// will overwrite what is set here the next time it does so. For a layer the game controls, use a
    /// channel the sequence does not automate.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetChannelVolume(int channel, float volume)
    {
        var clamped = volume < 0.0f ? 0.0f : volume > 1.0f ? 1.0f : volume;
        SendMidiMessage(channel, 0xB0, 7, (int)(clamped * 127.0f + 0.5f));
    }

    /// <summary>
    /// Sets one channel's stereo position, as MIDI control change 10.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="pan">The position, -1.0 (full left) through 0.0 (centre) to 1.0 (full right). Clamped.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetChannelPan(int channel, float pan)
    {
        var clamped = pan < -1.0f ? -1.0f : pan > 1.0f ? 1.0f : pan;
        SendMidiMessage(channel, 0xB0, 10, (int)((clamped + 1.0f) * 0.5f * 127.0f + 0.5f));
    }

    /// <summary>
    /// Changes the instrument one channel plays, as a MIDI program change.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="program">The program (patch) number, 0-127.</param>
    /// <remarks>
    /// Which instrument a program number selects is the loaded SoundFont's or SFZ instrument's
    /// business, not this player's. As with volume, the sequence's own program changes still apply.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15, or <paramref name="program"/> is outside 0-127.</exception>
    public void SetChannelProgram(int channel, int program)
    {
        if (program < 0 || program > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program, "A MIDI program number must be 0-127.");
        }

        SendMidiMessage(channel, 0xC0, program, 0);
    }

    private static void RequireChannel(int channel)
    {
        if (channel < 0 || channel > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "A MIDI channel must be 0-15.");
        }
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

                // Speed and the two message hooks are properties of the PLAYER, not of any one
                // sequence, so they survive a load - the provider (and the sequencer inside it) is
                // rebuilt here and would otherwise come back at its defaults with the hooks lost.
                provider.Speed = _speed;
                provider.MessageFilter = _messageFilter;
                provider.MessageObserver = _messageObserver;

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

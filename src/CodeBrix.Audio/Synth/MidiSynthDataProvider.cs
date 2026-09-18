using System;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Synth.Internal;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// Feeds a <see cref="MidiSequencer"/> or a <see cref="MidiStreamSequencer"/> into the audio engine
/// as a stereo float source, and owns the thread-safety contract the synthesizer itself does not
/// provide.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IMidiSynthesizer"/> is explicitly not thread-safe: rendering and note events must not
/// overlap. That is a real hazard here, because rendering happens on the engine's real-time audio thread
/// while transport calls (play, seek, stop) arrive from whatever thread the application uses. Every
/// entry point on this type takes the same lock, so the two can never interleave.
/// </para>
/// <para>
/// The synthesizer is constructed at the device's sample rate, so no resampling happens anywhere in this
/// path - the engine takes the samples exactly as they are rendered.
/// </para>
/// <para>
/// A non-looping sequence ends the STREAM, not just the note flow: once the last message has been
/// dispatched and the final voices have finished sounding, <see cref="ReadBytes"/> returns 0. That
/// zero-length read is the engine's one end-of-stream signal - it is what moves the engine player to
/// Stopped and ultimately raises <c>MidiMusicPlayer.PlaybackEnded</c>.
/// </para>
/// <para>
/// A <see cref="MidiStream"/> is played through the same path, by holding whichever sequencer is
/// active as an <see cref="IMidiPlaybackCore"/>. Everything downstream of <c>Start</c> reads the
/// core, so there is one rendering path rather than two. The difference a growing timeline makes is
/// at the end gate: a stream that is STARVED has not ended, so it returns SILENCE of the length
/// asked for and the engine plays on - it is only a COMPLETED stream, drained and rung out, that
/// returns zero.
/// </para>
/// </remarks>
internal sealed class MidiSynthDataProvider : ISoundDataProvider
{
    // A voice the sequence never sends a note-off for (a "stuck" note in a file that relied on the
    // player simply stopping) can sustain indefinitely, and the stream must still end for such a
    // file. Ten seconds of ring-out comfortably exceeds any realistic release tail.
    private const double MaxTailSeconds = 10.0;

    private readonly object _lock = new object();
    private readonly MidiSequencer _sequencer;
    private readonly float[] _left;
    private readonly float[] _right;
    private readonly int _maxTailFrames;

    private MidiStreamSequencer _streamSequencer;
    private IMidiPlaybackCore _core;
    private MidiStream _stream;
    private MidiSequence _sequence;
    private TempoSource _tempoSource;
    private MidiSequencer.MessageHook _messageFilter;
    private MidiMessageObserver _messageObserver;
    private bool _looping;
    private bool _endRaised;
    private int _tailFramesRendered;
    private bool _disposed;

    /// <summary>Creates a provider rendering the given synthesizer at its own sample rate.</summary>
    /// <param name="synthesizer">The synthesizer to render. Constructed at the output device's rate.</param>
    internal MidiSynthDataProvider(IMidiSynthesizer synthesizer)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        _sequencer = new MidiSequencer(synthesizer);
        _core = _sequencer;
        SampleRate = synthesizer.SampleRate;
        _maxTailFrames = (int)(MaxTailSeconds * synthesizer.SampleRate);

        _left = new float[synthesizer.BlockSize];
        _right = new float[synthesizer.BlockSize];
    }

    /// <inheritdoc/>
    public event EventHandler<EventArgs> EndOfStreamReached;

    /// <inheritdoc/>
    public event EventHandler<PositionChangedEventArgs> PositionChanged;

    /// <inheritdoc/>
    public int Position
    {
        get
        {
            lock (_lock)
            {
                return (int)(_core.Position.TotalSeconds * SampleRate);
            }
        }
    }

    /// <inheritdoc/>
    public int Length
    {
        get
        {
            lock (_lock)
            {
                // For a stream this is the horizon, which grows as its producer appends - so the
                // engine's Duration follows the producer, and a seek clamps to what exists.
                return (int)(_core.Length.TotalSeconds * SampleRate);
            }
        }
    }

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <inheritdoc/>
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc/>
    public int SampleRate { get; }

    /// <inheritdoc/>
    public bool IsDisposed
    {
        get { lock (_lock) { return _disposed; } }
    }

    /// <inheritdoc/>
    public SoundFormatInfo FormatInfo => null;

    /// <summary>The sequence currently loaded, or <see langword="null"/>.</summary>
    internal MidiSequence Sequence
    {
        get { lock (_lock) { return _sequence; } }
    }

    /// <summary>The stream currently loaded, or <see langword="null"/>.</summary>
    internal MidiStream Stream
    {
        get { lock (_lock) { return _stream; } }
    }

    /// <summary>
    /// Whether a loaded stream has caught up with its producer. Always <see langword="false"/> for a
    /// sequence, which is all there before it starts.
    /// </summary>
    internal bool IsStarved
    {
        get { lock (_lock) { return _stream != null && _streamSequencer.IsStarved; } }
    }

    /// <summary>The current playback position within the sequence or stream.</summary>
    internal TimeSpan CurrentTime
    {
        get { lock (_lock) { return _core.Position; } }
    }

    /// <summary>
    /// The musical clock this provider publishes tempo and beat position into as it renders, or
    /// <see langword="null"/> for none.
    /// </summary>
    internal TempoSource Tempo
    {
        get { lock (_lock) { return _tempoSource; } }
        set { lock (_lock) { _tempoSource = value; PublishTempo(_tempoSource != null && _tempoSource.IsPlaying); } }
    }

    /// <summary>The playback speed multiplier the sequencer is running at.</summary>
    internal float Speed
    {
        get { lock (_lock) { return _core.Speed; } }

        set
        {
            lock (_lock)
            {
                // Both sequencers are kept at the player's speed, whichever is active: the speed is
                // a property of the player and survives a switch from one to the other.
                _sequencer.Speed = value;
                if (_streamSequencer != null)
                {
                    _streamSequencer.Speed = value;
                }
            }
        }
    }

    /// <summary>
    /// The hook that REPLACES delivery of each MIDI message to the synthesizer, or
    /// <see langword="null"/> for normal delivery.
    /// </summary>
    internal MidiSequencer.MessageHook MessageFilter
    {
        get { lock (_lock) { return _messageFilter; } }
        set { lock (_lock) { _messageFilter = value; RefreshHook(); } }
    }

    /// <summary>
    /// The observe-only callback invoked after each MIDI message has been delivered, or
    /// <see langword="null"/> for none.
    /// </summary>
    internal MidiMessageObserver MessageObserver
    {
        get { lock (_lock) { return _messageObserver; } }
        set { lock (_lock) { _messageObserver = value; RefreshHook(); } }
    }

    /// <summary>
    /// Sends a MIDI message to the synthesizer from an arbitrary thread, serialized against
    /// rendering.
    /// </summary>
    /// <param name="channel">The channel to send to, 0-15.</param>
    /// <param name="command">The message's command nibble.</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte.</param>
    internal void SendMidiMessage(int channel, int command, int data1, int data2)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _core.Synthesizer.ProcessMidiMessage(channel, command, data1, data2);
        }
    }

    // Keeps the sequencer's single hook slot in step with the two things that can want it. When
    // neither is set the slot is cleared, so ordinary playback keeps the sequencer's fast path
    // (it calls the synthesizer directly when no hook is installed).
    // Callers hold _lock.
    private void RefreshHook()
    {
        var hook = _messageFilter == null && _messageObserver == null
            ? null
            : (MidiSequencer.MessageHook)OnSequencerMessage;

        _sequencer.OnSendMessage = hook;
        if (_streamSequencer != null)
        {
            _streamSequencer.OnSendMessage = hook;
        }
    }

    // Runs on the audio thread, inside ReadBytes, with _lock already held by this thread.
    //
    // MidiSequencer's hook REPLACES delivery rather than observing it - when OnSendMessage is set
    // the sequencer does not call ProcessMidiMessage itself. So this method has to deliver the
    // message, and only then tell the observer about it. Getting that backwards silences the music.
    private void OnSequencerMessage(IMidiSynthesizer synthesizer, int channel, int command, int data1, int data2)
    {
        var filter = _messageFilter;
        if (filter != null)
        {
            filter(synthesizer, channel, command, data1, data2);
        }
        else
        {
            synthesizer.ProcessMidiMessage(channel, command, data1, data2);
        }

        _messageObserver?.Invoke(channel, command, data1, data2);
    }

    /// <summary>Starts the given sequence from its beginning.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="loop">Whether the sequence loops at its loop point (or its end).</param>
    internal void Start(MidiSequence sequence, bool loop)
    {
        lock (_lock)
        {
            ReleaseStream();

            _sequence = sequence;
            _looping = loop;
            _endRaised = false;
            _tailFramesRendered = 0;
            _core = _sequencer;
            _sequencer.Play(sequence, loop);
            PublishTempo(_tempoSource != null && _tempoSource.IsPlaying);
        }
    }

    /// <summary>
    /// Starts the given stream from its beginning, which is also how a playing stream is rewound.
    /// </summary>
    /// <param name="stream">The stream to play.</param>
    /// <remarks>
    /// The stream sequencer is built the first time one is asked for, and carries the speed and the
    /// message hook the provider is already running with. A stream never loops (a growing timeline
    /// has no end to loop at), so looping is turned off for as long as one is loaded.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another sequencer is playing the stream.</exception>
    internal void Start(MidiStream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        lock (_lock)
        {
            if (_streamSequencer == null)
            {
                _streamSequencer = new MidiStreamSequencer(_sequencer.Synthesizer);
                _streamSequencer.Speed = _sequencer.Speed;
                _streamSequencer.OnSendMessage = _sequencer.OnSendMessage;
            }

            // A sequence and a stream share one synthesizer, so the sequence path has to let go of
            // it before the stream path takes over.
            _sequencer.Stop();
            _sequence = null;

            _stream = stream;
            _looping = false;
            _endRaised = false;
            _tailFramesRendered = 0;
            _core = _streamSequencer;
            _streamSequencer.Play(stream);
            PublishTempo(_tempoSource != null && _tempoSource.IsPlaying);
        }
    }

    // Lets go of a loaded stream, so nobody is left holding a claim on it. Callers hold _lock.
    private void ReleaseStream()
    {
        if (_stream == null)
        {
            return;
        }

        _stream = null;
        _streamSequencer?.Stop();
    }

    /// <summary>
    /// Reports the sequencer's position as musical time. Callers hold <c>_lock</c>; it allocates
    /// nothing, because it runs on the audio thread once per block.
    /// </summary>
    /// <param name="isPlaying">What to report for <see cref="TempoSource.IsPlaying"/>.</param>
    internal void PublishTempo(bool isPlaying)
    {
        var tempo = _tempoSource;
        if (tempo == null)
        {
            return;
        }

        if (_sequence == null && _stream == null)
        {
            tempo.IsPlaying = isPlaying;
            return;
        }

        tempo.Update(_core.CurrentBeatsPerMinute, _core.CurrentBeatPosition, isPlaying);

        // A sequence never carries a time signature and reports the default of four; a stream
        // reports what its last one declared. This is the field MidiSequence could never fill.
        tempo.BeatsPerBar = _core.BeatsPerBar;
    }

    /// <summary>Stops playback and silences all voices, releasing a loaded stream.</summary>
    internal void StopSequence()
    {
        lock (_lock)
        {
            ReleaseStream();
            _sequencer.Stop();
            _core = _sequencer;
            _endRaised = false;
            _tailFramesRendered = 0;
        }
    }

    /// <summary>Changes whether the loaded sequence loops, restarting it so the sequencer agrees.</summary>
    /// <param name="loop">The new looping state.</param>
    /// <remarks>Ignored while a stream is loaded: a growing timeline has no end to loop at.</remarks>
    internal void SetLooping(bool loop)
    {
        lock (_lock)
        {
            if (_looping == loop || _sequence == null || _stream != null)
            {
                return;
            }

            // MidiSequencer takes `loop` at Play() time and has no setter for it. Restarting at the
            // current position is what makes the change take effect without an audible jump.
            var resumeAt = _sequencer.Position;
            _looping = loop;
            _endRaised = false;
            _tailFramesRendered = 0;
            _sequencer.Play(_sequence, loop);
            _sequencer.Seek(resumeAt);
        }
    }

    /// <inheritdoc/>
    public int ReadBytes(Span<float> buffer)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return 0;
            }

            if (_sequence == null && _stream == null)
            {
                buffer.Clear();
                return buffer.Length;
            }

            // A finished non-looping sequence must end the stream: the engine's player treats a
            // zero-length read as its ONLY end-of-stream signal (see SoundPlayerBase.GenerateAudio),
            // and without one it pulls silence forever - state stuck at Playing, Position counting
            // past Duration, PlaybackEnded never raised. The gate is live sequencer state rather
            // than a latch, so seeking back before the end resumes rendering. The voice check lets
            // the final release tails ring out before the cut; the frame cap bounds that ring-out
            // for a voice whose note-off never comes.
            // For a stream, "ended" means completed AND drained: a starved stream has not ended,
            // so the loop below fills the buffer with silence and the engine plays on.
            if (!_looping && _core.IsEnded &&
                (_core.Synthesizer.ActiveVoiceCount == 0 || _tailFramesRendered >= _maxTailFrames))
            {
                if (!_endRaised)
                {
                    _endRaised = true;
                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                }

                return 0;
            }

            // The engine asks for interleaved stereo; the sequencer renders into separate planes.
            var frames = buffer.Length / 2;
            var written = 0;

            while (written < frames)
            {
                var take = Math.Min(_left.Length, frames - written);

                var left = _left.AsSpan(0, take);
                var right = _right.AsSpan(0, take);
                _core.Render(left, right);

                for (var i = 0; i < take; i++)
                {
                    buffer[(written + i) * 2] = left[i];
                    buffer[(written + i) * 2 + 1] = right[i];
                }

                written += take;
            }

            if (!_looping && _core.IsEnded)
            {
                _tailFramesRendered += frames;
            }

            PublishTempo(true);
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));

            return frames * 2;
        }
    }

    /// <inheritdoc/>
    public void Seek(int offset)
    {
        lock (_lock)
        {
            if (_disposed || (_sequence == null && _stream == null))
            {
                return;
            }

            var seconds = offset <= 0 ? 0d : (double)offset / SampleRate;
            _core.Seek(TimeSpan.FromSeconds(seconds));
            _endRaised = false;
            _tailFramesRendered = 0;
            PublishTempo(_tempoSource != null && _tempoSource.IsPlaying);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseStream();
            _sequencer.Stop();
            _core = _sequencer;
            _sequence = null;
        }
    }
}

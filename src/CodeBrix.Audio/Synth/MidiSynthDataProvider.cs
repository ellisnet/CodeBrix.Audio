using System;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Metadata.Models;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// Feeds a <see cref="MidiSequencer"/> into the audio engine as a stereo float source, and owns the
/// thread-safety contract the synthesizer itself does not provide.
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

    private MidiSequence _sequence;
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
                return (int)(_sequencer.Position.TotalSeconds * SampleRate);
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
                return _sequence == null ? 0 : (int)(_sequence.Length.TotalSeconds * SampleRate);
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

    /// <summary>The current playback position within the sequence.</summary>
    internal TimeSpan CurrentTime
    {
        get { lock (_lock) { return _sequencer.Position; } }
    }

    /// <summary>The playback speed multiplier the sequencer is running at.</summary>
    internal float Speed
    {
        get { lock (_lock) { return _sequencer.Speed; } }
        set { lock (_lock) { _sequencer.Speed = value; } }
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

            _sequencer.Synthesizer.ProcessMidiMessage(channel, command, data1, data2);
        }
    }

    // Keeps the sequencer's single hook slot in step with the two things that can want it. When
    // neither is set the slot is cleared, so ordinary playback keeps the sequencer's fast path
    // (it calls the synthesizer directly when no hook is installed).
    // Callers hold _lock.
    private void RefreshHook()
    {
        _sequencer.OnSendMessage = _messageFilter == null && _messageObserver == null
            ? null
            : OnSequencerMessage;
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
            _sequence = sequence;
            _looping = loop;
            _endRaised = false;
            _tailFramesRendered = 0;
            _sequencer.Play(sequence, loop);
        }
    }

    /// <summary>Stops playback and silences all voices.</summary>
    internal void StopSequence()
    {
        lock (_lock)
        {
            _sequencer.Stop();
            _endRaised = false;
            _tailFramesRendered = 0;
        }
    }

    /// <summary>Changes whether the loaded sequence loops, restarting it so the sequencer agrees.</summary>
    /// <param name="loop">The new looping state.</param>
    internal void SetLooping(bool loop)
    {
        lock (_lock)
        {
            if (_looping == loop || _sequence == null)
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

            if (_sequence == null)
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
            if (!_looping && _sequencer.EndOfSequence &&
                (_sequencer.Synthesizer.ActiveVoiceCount == 0 || _tailFramesRendered >= _maxTailFrames))
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
                _sequencer.Render(left, right);

                for (var i = 0; i < take; i++)
                {
                    buffer[(written + i) * 2] = left[i];
                    buffer[(written + i) * 2 + 1] = right[i];
                }

                written += take;
            }

            if (!_looping && _sequencer.EndOfSequence)
            {
                _tailFramesRendered += frames;
            }

            PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));

            return frames * 2;
        }
    }

    /// <inheritdoc/>
    public void Seek(int offset)
    {
        lock (_lock)
        {
            if (_disposed || _sequence == null)
            {
                return;
            }

            var seconds = offset <= 0 ? 0d : (double)offset / SampleRate;
            _sequencer.Seek(TimeSpan.FromSeconds(seconds));
            _endRaised = false;
            _tailFramesRendered = 0;
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
            _sequencer.Stop();
            _sequence = null;
        }
    }
}

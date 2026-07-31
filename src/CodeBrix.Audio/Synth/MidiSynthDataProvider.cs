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
/// <see cref="SoundFontSynthesizer"/> is explicitly not thread-safe: rendering and note events must not
/// overlap. That is a real hazard here, because rendering happens on the engine's real-time audio thread
/// while transport calls (play, seek, stop) arrive from whatever thread the application uses. Every
/// entry point on this type takes the same lock, so the two can never interleave.
/// </para>
/// <para>
/// The synthesizer is constructed at the device's sample rate, so no resampling happens anywhere in this
/// path - the engine takes the samples exactly as they are rendered.
/// </para>
/// </remarks>
internal sealed class MidiSynthDataProvider : ISoundDataProvider
{
    private readonly object _lock = new object();
    private readonly MidiSequencer _sequencer;
    private readonly float[] _left;
    private readonly float[] _right;

    private MidiSequence _sequence;
    private bool _looping;
    private bool _endRaised;
    private bool _disposed;

    /// <summary>Creates a provider rendering the given synthesizer at its own sample rate.</summary>
    /// <param name="synthesizer">The synthesizer to render. Constructed at the output device's rate.</param>
    internal MidiSynthDataProvider(SoundFontSynthesizer synthesizer)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        _sequencer = new MidiSequencer(synthesizer);
        SampleRate = synthesizer.SampleRate;

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

            // Rendering runs on the real-time audio thread. Raise the completion notification outside
            // the render loop but still under the lock, then let the player marshal it off this thread.
            if (!_looping && !_endRaised && _sequencer.EndOfSequence)
            {
                _endRaised = true;
                EndOfStreamReached?.Invoke(this, EventArgs.Empty);
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

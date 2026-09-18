using System;
using CodeBrix.Audio.Synth.Internal;

namespace CodeBrix.Audio.Synth;

// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
//
// MidiSequencer's counterpart for a timeline that is still being written. The block loop is the
// same loop, in the same order - process the events due at the current time, then move the clock
// on by one synthesizer block - and the tick-to-time walk is the same arithmetic MidiSequence
// performs when it merges a file's tracks, done incrementally as the events are reached instead
// of all at once up front. That is what makes a stream completed before playback render, sample
// for sample, what MidiSequencer renders from stream.ToSequence().

/// <summary>
/// Plays a <see cref="MidiStream"/> through a synthesizer while the stream is still being written.
/// </summary>
/// <remarks>
/// <para>
/// Every block, the sequencer first delivers everything the head has reached - in every state, so
/// the event AT the horizon, which is every bar's last note-off, plays on time - and then moves the
/// head on by one block, never past the horizon. When the head reaches the horizon the stream is
/// STARVED: the head holds its position while the synthesizer keeps rendering, so whatever was
/// sounding rings out and a note whose note-off has not arrived keeps sounding. It resumes once
/// <see cref="MidiStream.Preroll"/> is buffered ahead of the head again, which is also what governs
/// the first start - a resume threshold with hysteresis, not a gate that slides along with the
/// head. Once the stream is completed, playback advances without waiting, drains to the last event,
/// and <see cref="EndOfStream"/> becomes true.
/// </para>
/// <para>
/// This type does not loop: a growing timeline has no end to loop at. To loop a finished piece,
/// play <see cref="MidiStream.ToSequence"/> through <see cref="MidiSequencer"/>.
/// </para>
/// <para>
/// Like <see cref="MidiSequencer"/>, this class does not provide thread safety of its own between
/// rendering and the transport calls: whoever drives it serializes them, exactly as
/// <c>MidiMusicPlayer</c> does. What it does own is the stream's lock, which it takes once per
/// synthesizer block, so a producer may append from any thread at any time.
/// </para>
/// </remarks>
public sealed class MidiStreamSequencer : IAudioRenderer, IMidiPlaybackCore
{
    // No append version can be this, so it means "the horizon time has not been derived yet".
    private const int NoHorizonCached = -1;

    private readonly IMidiSynthesizer synthesizer;

    private MidiStream stream;
    private MidiStream.Walk walk;
    private MidiSequencer.MessageHook onSendMessage;

    private float speed;
    private int blockWrote;
    private TimeSpan currentTime;
    private TimeSpan horizonTime;
    private int horizonVersion = NoHorizonCached;
    private bool starved = true;

    /// <summary>
    /// Initializes a new instance of the sequencer.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to be used by the sequencer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> is null.</exception>
    public MidiStreamSequencer(IMidiSynthesizer synthesizer)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        this.synthesizer = synthesizer;

        speed = 1F;
        walk.Reset();
    }

    /// <summary>
    /// Plays a stream from its beginning, claiming it for this sequencer.
    /// </summary>
    /// <param name="midiStream">The stream to play.</param>
    /// <remarks>
    /// The synthesizer is reset and the head is placed at tick zero. A stream may be played again
    /// from the top any number of times, while it is still being produced or after it has been
    /// completed: playing the stream this sequencer already holds is simply a rewind. Only one
    /// sequencer may hold a stream at a time, and <see cref="Stop"/> releases it for another.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="midiStream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another sequencer is playing the stream.</exception>
    public void Play(MidiStream midiStream)
    {
        if (midiStream == null)
        {
            throw new ArgumentNullException(nameof(midiStream));
        }

        var previous = stream;
        if (previous != null && !ReferenceEquals(previous, midiStream))
        {
            previous.Detach(this);
            stream = null;
        }

        // The claim and this sequencer's own reset happen under ONE acquisition of the stream's
        // lock. Anything less leaves a window in which a producer appending on another thread sees
        // a sequencer that is attached but has not yet placed its head, and classifies an event's
        // lateness against a head that is about to be thrown away.
        lock (midiStream.Gate)
        {
            midiStream.AttachUnderGate(this);

            stream = midiStream;

            blockWrote = synthesizer.BlockSize;
            currentTime = TimeSpan.Zero;
            walk.Reset();
            starved = true;
            horizonVersion = NoHorizonCached;
        }

        synthesizer.Reset();
    }

    /// <summary>
    /// Stops playing and releases the stream, so another sequencer may take it.
    /// </summary>
    public void Stop()
    {
        var current = stream;
        if (current == null)
        {
            Release();
        }
        else
        {
            // Released and reset under ONE acquisition, as Play claims and places under one: a
            // producer appending on another thread sees a sequencer that holds the stream and has
            // a head, or one that holds nothing at all.
            lock (current.Gate)
            {
                current.DetachUnderGate(this);
                Release();
            }
        }

        synthesizer.Reset();
    }

    // Puts the sequencer back where a new one starts. Callers hold the stream's lock if there is
    // a stream.
    private void Release()
    {
        stream = null;
        currentTime = TimeSpan.Zero;
        walk.Reset();
        starved = true;
        horizonVersion = NoHorizonCached;
    }

    /// <summary>
    /// Moves playback to the given position on the timeline.
    /// </summary>
    /// <param name="position">
    /// The position to seek to, from the start of the stream. Negative values are clamped to zero,
    /// and anything beyond the horizon is clamped to the horizon.
    /// </param>
    /// <remarks>
    /// <para>
    /// The controller state leading up to <paramref name="position"/> is replayed into the
    /// synthesizer - program and bank changes, volume, pan, expression, pitch bend - so the
    /// instrument sounds the way it would have if the stream had been played from the start.
    /// Note-on messages are deliberately NOT replayed: re-triggering every note that ever sounded
    /// would be both wrong and deafening.
    /// </para>
    /// <para>
    /// The walk restarts at tick zero to get there, because the tempo map has to be re-derived; it
    /// is cheap. Does nothing when no stream is playing.
    /// </para>
    /// </remarks>
    public void Seek(TimeSpan position)
    {
        var current = stream;
        if (current == null)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        synthesizer.Reset();

        lock (current.Gate)
        {
            var horizon = current.TimeAtTickUnderGate(current.HorizonTicksUnderGate);
            if (position > horizon)
            {
                position = horizon;
            }

            blockWrote = synthesizer.BlockSize;
            currentTime = position;
            walk.Reset();
            starved = true;

            var entries = current.Entries;
            var ticksPerQuarterNote = current.TicksPerQuarterNote;
            var conductorEnd = current.ConductorEndTicks;

            // STRICTLY earlier than the seek point, not "at or earlier". Rendering starts by calling
            // ProcessEvents with currentTime already equal to position, which fires everything written
            // AT position - so consuming those here would fire the controllers twice and, far worse,
            // silently swallow every note-on that lands exactly on the seek point. Seek(TimeSpan.Zero)
            // is the case that made it obvious for the file sequencer: every event of a sequence that
            // starts at tick 0 was eaten, and the sequence played as silence.
            while (walk.Index < entries.Count)
            {
                var entry = entries[walk.Index];

                if (conductorEnd > walk.Tick && conductorEnd < entry.Tick)
                {
                    walk.AdvanceTo(conductorEnd, ticksPerQuarterNote);
                }

                var time = walk.TimeAt(entry.Tick, ticksPerQuarterNote);
                if (time >= position)
                {
                    break;
                }

                if (entry.SplitsTime)
                {
                    walk.AdvanceTo(entry.Tick, ticksPerQuarterNote);
                }

                if (entry.Playable)
                {
                    var message = entry.Message;

                    // 0x90 is Note On and 0x80 is Note Off; everything else is controller state
                    // that has to be reapplied for the stream to sound correct from here.
                    if (message.Command != 0x90 && message.Command != 0x80)
                    {
                        synthesizer.ProcessMidiMessage(message.Channel, message.Command, message.Data1, message.Data2);
                    }
                }
                else if (entry.IsTimeSignature)
                {
                    walk.BeatsPerBar = entry.BeatsPerBar;
                }
                else
                {
                    walk.Tempo = entry.Message.Tempo;
                }

                walk.Index++;
            }
        }
    }

    /// <inheritdoc/>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        var wrote = 0;
        while (wrote < left.Length)
        {
            if (blockWrote == synthesizer.BlockSize)
            {
                ProcessEvents();
                blockWrote = 0;
            }

            var srcRem = synthesizer.BlockSize - blockWrote;
            var dstRem = left.Length - wrote;
            var rem = Math.Min(srcRem, dstRem);

            synthesizer.Render(left.Slice(wrote, rem), right.Slice(wrote, rem));

            blockWrote += rem;
            wrote += rem;
        }
    }

    /// <summary>
    /// Gets the synthesizer used by the sequencer.
    /// </summary>
    public IMidiSynthesizer Synthesizer => synthesizer;

    /// <summary>
    /// Gets the stream being played, or <see langword="null"/> when the sequencer is stopped.
    /// </summary>
    public MidiStream Stream => stream;

    /// <summary>
    /// Gets the current playback position - the head, in time. It holds while the stream is starved.
    /// </summary>
    public TimeSpan Position => currentTime;

    /// <summary>
    /// Gets the current playback position in ticks.
    /// </summary>
    /// <remarks>
    /// Between events the value is derived from the head's time at the tempo in force, so it moves
    /// smoothly rather than jumping from event to event.
    /// </remarks>
    public long PositionTicks
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return 0;
            }

            lock (current.Gate)
            {
                return HeadTicksUnderGate;
            }
        }
    }

    /// <summary>
    /// Gets a value that indicates whether the head has caught up with the producer: the stream is
    /// not complete and there is not enough buffered ahead for playback to advance.
    /// </summary>
    /// <remarks>
    /// A starved stream is still playing - it plays silence, plus the ring-out of whatever was
    /// sounding - and resumes from where it held as soon as a <see cref="MidiStream.Preroll"/> has
    /// been buffered ahead of it. It is <see langword="false"/> when no stream is playing, and
    /// never <see langword="true"/> for a completed stream. It is answered from the state AND from
    /// what is written ahead of the head, so it is right before the first block has been rendered
    /// too: a sequencer that has just been given a stream with enough in it reports
    /// <see langword="false"/>, and one given an empty stream reports <see langword="true"/>.
    /// </remarks>
    public bool IsStarved
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return false;
            }

            lock (current.Gate)
            {
                return starved && !CanResumeUnderGate(current);
            }
        }
    }

    /// <summary>
    /// Gets how far the producer has got, in time - the moment the horizon falls at. It grows as
    /// the producer appends and is final once the stream has been completed, and it is
    /// <see cref="TimeSpan.Zero"/> when no stream is playing.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="MidiSequence.Length"/> for a timeline that has no end yet: it
    /// is what a player reports as its duration, and what a seek past the end clamps to.
    /// </remarks>
    public TimeSpan Length
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return TimeSpan.Zero;
            }

            lock (current.Gate)
            {
                return HorizonTimeUnderGate(current);
            }
        }
    }

    /// <summary>
    /// Gets a value that indicates whether the stream has been completed and every event on it has
    /// been delivered.
    /// </summary>
    /// <remarks>
    /// This value is <see langword="true"/> when no stream is playing, the same way
    /// <see cref="MidiSequencer.EndOfSequence"/> is true before anything has been played.
    /// </remarks>
    public bool EndOfStream
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return true;
            }

            lock (current.Gate)
            {
                return current.IsCompletedUnderGate && walk.Index >= current.Entries.Count;
            }
        }
    }

    /// <summary>
    /// Whether the stream has been completed and every event on it delivered - the seam's name for
    /// <see cref="EndOfStream"/>.
    /// </summary>
    bool IMidiPlaybackCore.IsEnded => EndOfStream;

    /// <summary>
    /// Gets or sets the playback speed.
    /// </summary>
    /// <remarks>
    /// The default value is 1. The tempo will be multiplied by this value. It scales the head's
    /// advance, not the events' positions, so the timeline itself is unaffected.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public float Speed
    {
        get => speed;

        set
        {
            if (value >= 0)
            {
                speed = value;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The playback speed must be a non-negative value.");
            }
        }
    }

    /// <summary>
    /// Gets or sets the method for modifying MIDI messages during playback.
    /// If <c>null</c>, MIDI messages are sent to the synthesizer without any changes.
    /// </summary>
    /// <remarks>
    /// The hook REPLACES delivery: while one is installed the sequencer does not call the
    /// synthesizer itself, exactly as <see cref="MidiSequencer.OnSendMessage"/> behaves. It runs on
    /// the rendering thread, under the stream's lock, so it must not append to the stream.
    /// </remarks>
    public MidiSequencer.MessageHook OnSendMessage
    {
        get => onSendMessage;
        set => onSendMessage = value;
    }

    /// <summary>
    /// Gets the tempo in force at the head, in beats per minute.
    /// </summary>
    public double CurrentBeatsPerMinute => walk.Tempo;

    /// <summary>
    /// Gets the head's position in quarter notes from the start of the stream.
    /// </summary>
    public double CurrentBeatPosition
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return 0;
            }

            lock (current.Gate)
            {
                return (double)HeadTicksUnderGate / current.TicksPerQuarterNote;
            }
        }
    }

    /// <summary>
    /// Gets the number of beats in a bar, from the last time signature the head has reached.
    /// Four until one says otherwise.
    /// </summary>
    public int BeatsPerBar => walk.BeatsPerBar;

    /// <summary>
    /// How many entries of the timeline the walk has consumed - the index a late event has to be
    /// inserted at or after, or it would never be played. Callers hold the stream's lock.
    /// </summary>
    internal int FrontierIndexUnderGate => walk.Index;

    /// <summary>
    /// The tick a late event is clamped to: the head, or the last entry the walk has already
    /// consumed, whichever is further on. Callers hold the stream's lock.
    /// </summary>
    /// <remarks>
    /// The head's tick is interpolated and its division truncates, so it can read a tick or two
    /// short of an entry the walk has in fact consumed. Clamping to the walk's own frontier instead
    /// is what guarantees a straggler is inserted AHEAD of the walk rather than behind it, where it
    /// would never be played.
    /// </remarks>
    internal long LateClampTicksUnderGate
    {
        get
        {
            var clamp = HeadTicksUnderGate;

            var current = stream;
            if (current != null && walk.Index > 0)
            {
                var frontier = current.Entries[walk.Index - 1].Tick;
                if (frontier > clamp)
                {
                    clamp = frontier;
                }
            }

            return clamp;
        }
    }

    /// <summary>
    /// The head's tick, interpolated from its time at the tempo in force. Callers hold the stream's
    /// lock.
    /// </summary>
    internal long HeadTicksUnderGate
    {
        get
        {
            var current = stream;
            if (current == null)
            {
                return 0;
            }

            var ahead = (currentTime - walk.Time).TotalSeconds;
            if (ahead <= 0)
            {
                return walk.Tick;
            }

            return walk.Tick + (long)(ahead / (60.0 / (current.TicksPerQuarterNote * walk.Tempo)));
        }
    }

    // Runs once per synthesizer block, on the rendering thread, under the stream's lock. Allocates
    // nothing: the timeline is a list of structs and the lock is a plain Monitor.
    private void ProcessEvents()
    {
        var current = stream;
        if (current == null)
        {
            return;
        }

        lock (current.Gate)
        {
            var entries = current.Entries;
            var ticksPerQuarterNote = current.TicksPerQuarterNote;
            var conductorEnd = current.ConductorEndTicks;

            // FIRE FIRST, in every state. The event at the horizon is every bar's last note-off,
            // and a rule that advances the head before delivering what the head has already
            // reached holds that note-off back until the next bar arrives - which is exactly what
            // the first form of this one did.
            while (walk.Index < entries.Count)
            {
                var entry = entries[walk.Index];

                if (conductorEnd > walk.Tick && conductorEnd < entry.Tick)
                {
                    walk.AdvanceTo(conductorEnd, ticksPerQuarterNote);
                }

                var time = walk.TimeAt(entry.Tick, ticksPerQuarterNote);
                if (time > currentTime)
                {
                    break;
                }

                if (entry.SplitsTime)
                {
                    walk.AdvanceTo(entry.Tick, ticksPerQuarterNote);
                }

                if (entry.Playable)
                {
                    var message = entry.Message;
                    if (onSendMessage == null)
                    {
                        synthesizer.ProcessMidiMessage(message.Channel, message.Command, message.Data1, message.Data2);
                    }
                    else
                    {
                        onSendMessage(synthesizer, message.Channel, message.Command, message.Data1, message.Data2);
                    }
                }
                else if (entry.IsTimeSignature)
                {
                    walk.BeatsPerBar = entry.BeatsPerBar;
                }
                else
                {
                    walk.Tempo = entry.Message.Tempo;
                }

                walk.Index++;
            }

            // THEN THE CLOCK. A starved head resumes once a pre-roll is buffered ahead of it again
            // - the same threshold that governs the very first start, because a new sequencer
            // begins starved. It is a resume threshold, not a sliding gate: once running, playback
            // carries on even when the gap has fallen below the pre-roll, and stops only at a true
            // underrun.
            var completed = current.IsCompletedUnderGate;

            if (starved && CanResumeUnderGate(current))
            {
                starved = false;
            }

            if (starved)
            {
                // The head holds where it is; the synthesizer still renders, so release tails and
                // any note whose note-off has not arrived keep sounding until the producer says
                // otherwise.
                return;
            }

            var next = currentTime + MidiSequence.GetTimeSpanFromSeconds(
                (double)speed * synthesizer.BlockSize / synthesizer.SampleRate);

            if (!completed)
            {
                // Clamped at the horizon, never past it: the head holds exactly where the producer
                // has got to, so the last thing written plays at its own time and the next thing
                // written follows it without a gap. A completed stream is never clamped and never
                // starved - it drains and ends the way a file does.
                var horizon = HorizonTimeUnderGate(current);
                if (next >= horizon)
                {
                    starved = true;
                    next = currentTime > horizon ? currentTime : horizon;
                }
            }

            currentTime = next;
        }
    }

    // Whether a starved head may move again: a completed stream always may, and otherwise the
    // horizon must be AHEAD of the head - not merely level with it - by at least a pre-roll.
    //
    // The "ahead of the head" half is what makes this a state the head can rest in: without it, a
    // head sitting exactly on the horizon with a zero pre-roll passes the test every block, moves,
    // is clamped straight back and starves again - a flip-flop inside the clamp that cannot be
    // read from outside. With it, the condition says the same thing at any moment, which is what
    // lets IsStarved answer honestly before the first block has been rendered. Callers hold the
    // stream's lock.
    private bool CanResumeUnderGate(MidiStream current)
    {
        if (current.IsCompletedUnderGate)
        {
            return true;
        }

        var horizon = HorizonTimeUnderGate(current);
        return horizon > currentTime && horizon - currentTime >= current.PrerollUnderGate;
    }

    // The time the horizon falls at, derived FROM THIS SEQUENCER'S OWN FRONTIER rather than from
    // tick zero, and re-derived only when the producer has appended something since the last look.
    // The rendering thread's cost per block is therefore a version compare. Callers hold the
    // stream's lock.
    private TimeSpan HorizonTimeUnderGate(MidiStream current)
    {
        var version = current.VersionUnderGate;
        if (horizonVersion != version)
        {
            horizonVersion = version;
            horizonTime = current.TimeAtTickUnderGate(walk, current.HorizonTicksUnderGate);
        }

        return horizonTime;
    }
}

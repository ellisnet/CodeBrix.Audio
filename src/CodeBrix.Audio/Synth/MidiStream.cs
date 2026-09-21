using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.Synth;

// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
//
// The growing counterpart to MidiSequence. A MidiSequence is finished before it is played; a
// MidiStream is played WHILE it is being written, so the events arrive on a producer's thread
// while a MidiStreamSequencer renders from the head on the audio thread. The two share one lock
// and one tick-to-time walk, and the walk is deliberately the same arithmetic MidiSequence's
// MergeTracks performs, so a stream that was completed before playback began renders exactly
// what its ToSequence() renders - see MidiStreamSequencer for the walk itself.

/// <summary>
/// A MIDI timeline that can be played while it is still being written.
/// </summary>
/// <remarks>
/// <para>
/// A producer appends tick-timed events - a bar at a time, a phrase at a time - from any thread.
/// A <see cref="MidiStreamSequencer"/> plays from the HEAD while the tail is still arriving. The
/// HORIZON is the latest tick appended so far; when the head reaches it the stream is STARVED and
/// playback holds its position, rendering the ring-out of whatever was sounding rather than
/// ending. <see cref="Preroll"/> says how much must be buffered ahead of the head before playback
/// advances, at the start and again after every underrun.
/// </para>
/// <para>
/// <see cref="Complete"/> is the producer's "no more": <see cref="Append(MidiEvent)"/> refuses from
/// then on and the sequencer drains to the last event and ends the way a file does. A producer that
/// simply stops without completing leaves playback waiting in silence for ever; that is deliberate,
/// and stopping the player is the only other way out.
/// </para>
/// <para>
/// Everything appended is also kept as an editable <see cref="MidiEventCollection"/> - the
/// RECORDING - at its original tick, so the piece that was heard can be saved as a standard MIDI
/// file through <see cref="ToMidiEventCollection"/>, or turned into an ordinary
/// <see cref="MidiSequence"/> through <see cref="ToSequence"/>.
/// </para>
/// <para>
/// Channels follow the <see cref="CodeBrix.Audio.Midi"/> model throughout: 1 to 16, the way
/// <see cref="MidiFile"/> reads and writes them. The 0-based form belongs to the synthesizer and
/// never appears on this type's surface.
/// </para>
/// <para>
/// Every member is safe to call from any thread. Appending allocates on the caller's thread only;
/// the rendering thread allocates nothing. Do not append from inside a message hook: the hook
/// already runs under this stream's lock, on the audio thread.
/// </para>
/// </remarks>
public sealed class MidiStream
{
    /// <summary>The resolution a stream takes when none is asked for.</summary>
    internal const int DefaultTicksPerQuarterNote = 480;

    /// <summary>The tempo in force until a tempo event says otherwise, as MidiSequence assumes.</summary>
    internal const double DefaultTempo = 120.0;

    /// <summary>The beats per bar reported until a time signature says otherwise.</summary>
    internal const int DefaultBeatsPerBar = 4;

    /// <summary>The recording track the conductor events live on, and the one that wins a tie at a tick.</summary>
    internal const int ConductorTrack = 0;

    // Room for a few thousand events before the first growth; a growth copies on the producer's
    // thread under the lock, never on the audio thread.
    private const int InitialCapacity = 4096;

    // A thoroughly confused producer cannot grow the problem list without bound.
    private const int MaxProblems = 100;

    // How far past a walk's own tick a time-to-tick answer may reach. A moment beyond this at the
    // tempo in force is not a musical position any more, and the cap keeps the arithmetic inside a
    // 64-bit tick count.
    private const long MaxTickAhead = long.MaxValue / 4;

    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>(InitialCapacity);
    private readonly MidiEventCollection recording;
    private readonly List<string> problems = new List<string>();
    private readonly int[] channelTracks = new int[17];
    private readonly int ticksPerQuarterNote;

    private MidiStreamSequencer attached;
    private TimeSpan preroll;
    private int order;
    private int nextTrack = 1;
    private long horizonTicks;
    private long settledTicks = -1;
    private long conductorEndTicks = -1;
    private int eventCount;
    private int lateEventCount;
    private int version;
    private bool completed;
    private bool endOfTrackReported;

    /// <summary>
    /// Creates an empty stream at the given resolution.
    /// </summary>
    /// <param name="ticksPerQuarterNote">
    /// How many ticks make a quarter note. Every appended event's tick is read in these units, and
    /// the recording carries the same resolution. A standard MIDI file stores this in 15 bits, so a
    /// stream meant to be saved or converted keeps it at 32767 or below.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerQuarterNote"/> is not positive.</exception>
    public MidiStream(int ticksPerQuarterNote = DefaultTicksPerQuarterNote)
    {
        if (ticksPerQuarterNote <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "The number of ticks per quarter note must be a positive value.");
        }

        this.ticksPerQuarterNote = ticksPerQuarterNote;
        recording = new MidiEventCollection(1, ticksPerQuarterNote);
    }

    /// <summary>
    /// How many ticks make a quarter note in this stream.
    /// </summary>
    public int TicksPerQuarterNote => ticksPerQuarterNote;

    /// <summary>
    /// How much must be buffered ahead of the head before playback advances: at the start, and
    /// again after every underrun. The default is <see cref="TimeSpan.Zero"/>, which advances as
    /// soon as a single tick is available.
    /// </summary>
    /// <remarks>
    /// It is compared in time, exactly - the gap between the head and <see cref="HorizonTime"/> -
    /// so tempo changes between the two cost it nothing. It is a RESUME threshold with hysteresis,
    /// not a sliding gate: once playback is running it continues until the head actually reaches
    /// the horizon, and only then does this much have to be buffered again. It may be changed at
    /// any time, including while playing.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan Preroll
    {
        get { lock (gate) { return preroll; } }

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The pre-roll must be a non-negative value.");
            }

            lock (gate) { preroll = value; }
        }
    }

    /// <summary>
    /// Whether the producer has said there is no more to come. <see cref="Append(MidiEvent)"/>
    /// throws once this is <see langword="true"/>, and playback drains to the last event and ends.
    /// </summary>
    public bool IsCompleted { get { lock (gate) { return completed; } } }

    /// <summary>
    /// The latest tick appended so far - how far ahead of the head playback may go. A note-off
    /// scheduled by a note's duration counts. Zero for an empty stream.
    /// </summary>
    public long HorizonTicks { get { lock (gate) { return horizonTicks; } } }

    /// <summary>
    /// How far the producer has got, in time: the moment <see cref="HorizonTicks"/> falls at, read
    /// from the stream's own tempo map. It grows as the producer appends, and is final once the
    /// stream has been completed.
    /// </summary>
    /// <remarks>
    /// This is what a player reports as its duration while a stream is playing, and what a seek
    /// beyond the end clamps to. For a completed stream it equals <c>ToSequence().Length</c>
    /// exactly - the two are derived with the same arithmetic, not merely to within a rounding -
    /// unless <see cref="AdvanceHorizon"/> has carried the horizon past the last event, in which
    /// case it is longer by the settled rest at the end, which the sequence does not carry.
    /// </remarks>
    public TimeSpan HorizonTime
    {
        get { lock (gate) { return TimeAtTickUnderGate(horizonTicks); } }
    }

    /// <summary>
    /// How many MIDI events the stream has taken. A note-on written with a duration counts twice:
    /// itself, and the note-off it schedules.
    /// </summary>
    public int EventCount { get { lock (gate) { return eventCount; } } }

    /// <summary>
    /// How many appended events arrived at a tick the head had already passed. Each was still
    /// delivered - at the head, on the next block - and each was recorded at its own tick. A
    /// producer watching this number learns that its <see cref="Preroll"/> is too small.
    /// </summary>
    public int LateEventCount { get { lock (gate) { return lateEventCount; } } }

    /// <summary>
    /// Everything appended that could not be honoured as written, one human-readable line each.
    /// Empty for a stream that was written the way this type expects. Content is reported here
    /// rather than thrown; misuse of the API throws.
    /// </summary>
    /// <remarks>Each read returns a snapshot, so the list a caller holds never changes underneath it.</remarks>
    public IReadOnlyList<string> Problems
    {
        get { lock (gate) { return problems.ToArray(); } }
    }

    /// <summary>
    /// Appends one event at the tick its <see cref="MidiEvent.AbsoluteTime"/> carries.
    /// </summary>
    /// <param name="midiEvent">The event to append.</param>
    /// <remarks>
    /// <para>
    /// Note events, control changes, patch changes, pitch wheel changes and channel after-touch are
    /// played and recorded. A <see cref="NoteOnEvent"/> written with a duration also schedules its
    /// <see cref="NoteOnEvent.OffEvent"/>, and both are recorded, exactly as reading a MIDI file
    /// links them. A <see cref="TempoEvent"/> sets the tempo from its tick and is recorded; a
    /// <see cref="TimeSignatureEvent"/> updates the beats-per-bar readback and is recorded. Key
    /// signatures, text, other meta events, sysex and sequencer-specific events are recorded only.
    /// An end-of-track meta event is ignored - <see cref="Complete"/> is how a stream ends - and
    /// noted once in <see cref="Problems"/>.
    /// </para>
    /// <para>
    /// An event that arrives at a tick the head has already passed is delivered at the head on the
    /// next block and counted in <see cref="LateEventCount"/>; the recording keeps its own tick.
    /// Nothing is ever dropped.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="midiEvent"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The event's tick is negative, or it is a channel message addressed outside channels 1 to 16.
    /// </exception>
    /// <exception cref="InvalidOperationException">The stream has been completed.</exception>
    public void Append(MidiEvent midiEvent)
    {
        if (midiEvent == null)
        {
            throw new ArgumentNullException(nameof(midiEvent));
        }

        Validate(midiEvent, nameof(midiEvent));

        lock (gate)
        {
            RequireNotCompleted();
            Take(midiEvent);
        }
    }

    /// <summary>
    /// Appends events in the order given, all or nothing: every event is validated before any of
    /// them is taken, so a batch with one bad event leaves the stream exactly as it was.
    /// </summary>
    /// <param name="events">The events to append, in the order they are to be taken.</param>
    /// <remarks>Each event is handled as <see cref="Append(MidiEvent)"/> describes.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null, or one of the events is.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// One of the events has a negative tick, or is a channel message addressed outside channels 1 to 16.
    /// </exception>
    /// <exception cref="InvalidOperationException">The stream has been completed.</exception>
    public void Append(IEnumerable<MidiEvent> events)
    {
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        var pending = new List<MidiEvent>();
        foreach (var midiEvent in events)
        {
            if (midiEvent == null)
            {
                throw new ArgumentNullException(nameof(events), "The batch holds a null event.");
            }

            Validate(midiEvent, nameof(events));
            pending.Add(midiEvent);
        }

        lock (gate)
        {
            RequireNotCompleted();

            for (var i = 0; i < pending.Count; i++)
            {
                Take(pending[i]);
            }
        }
    }

    /// <summary>
    /// Appends a note as a note-on carrying a duration, which schedules its note-off.
    /// </summary>
    /// <param name="tick">The tick the note starts at.</param>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="noteNumber">The note number, 0 to 127.</param>
    /// <param name="velocity">The velocity the note is struck at, 0 to 127.</param>
    /// <param name="durationTicks">How long the note sounds, in ticks.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The tick or the duration is negative, the duration does not fit a 32-bit tick count, or the
    /// channel is outside 1 to 16.
    /// </exception>
    /// <exception cref="InvalidOperationException">The stream has been completed.</exception>
    public void AppendNote(long tick, int channel, int noteNumber, int velocity, long durationTicks)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "The tick must be a non-negative value.");
        }

        if (channel < 1 || channel > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "The channel must be 1 to 16.");
        }

        if (durationTicks < 0 || durationTicks > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks,
                "The duration must be a non-negative number of ticks that fits a 32-bit count.");
        }

        Append(new NoteOnEvent(tick, channel, noteNumber, velocity, (int)durationTicks));
    }

    /// <summary>
    /// Appends a tempo change, in force from its tick onwards.
    /// </summary>
    /// <param name="tick">The tick the new tempo takes effect at.</param>
    /// <param name="beatsPerMinute">The new tempo, in quarter notes per minute.</param>
    /// <remarks>
    /// A tempo change retimes every event at a LATER tick, whenever it was appended - ticks are
    /// musical positions, so slowing bar 5 slows bar 6 even if bar 6 was written first. What it
    /// cannot change is the past: appended behind the head it is a late event and takes effect
    /// from the head.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The tick is negative, or the tempo is not positive.</exception>
    /// <exception cref="InvalidOperationException">The stream has been completed.</exception>
    public void AppendTempo(long tick, double beatsPerMinute)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "The tick must be a non-negative value.");
        }

        if (!(beatsPerMinute > 0) || double.IsInfinity(beatsPerMinute))
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerMinute), beatsPerMinute,
                "The tempo must be a positive number of beats per minute.");
        }

        var microsecondsPerQuarterNote = (int)Math.Round(60000000.0 / beatsPerMinute);
        if (microsecondsPerQuarterNote < 1)
        {
            microsecondsPerQuarterNote = 1;
        }

        Append(new TempoEvent(microsecondsPerQuarterNote, tick));
    }

    /// <summary>
    /// Carries the horizon forward over music that is settled and empty: "everything up to this
    /// tick has been decided, and there is simply nothing in it".
    /// </summary>
    /// <param name="tick">The tick everything up to which is settled.</param>
    /// <remarks>
    /// <para>
    /// A producer that has composed a rest - a bar of silence, a gap between phrases - has nothing
    /// to append for it, and a stream whose horizon still sits at the last note STARVES there: the
    /// head holds, and the rest is not played but waited through. This is how the rest is declared
    /// instead. The head then walks through the settled silence exactly as it walks through written
    /// music, the pre-roll is satisfied by it, and <see cref="HorizonTime"/> and a player's
    /// duration grow with it.
    /// </para>
    /// <para>
    /// It only ever RAISES the horizon: a tick at or behind it is a no-op, because the producer has
    /// already said at least that much. It creates no timeline entry, is not counted in
    /// <see cref="EventCount"/>, and leaves the recording untouched - <see cref="ToMidiEventCollection"/>
    /// and <see cref="ToSequence"/> carry exactly what was appended, so a rest declared here is not
    /// in the saved file. A stream this is never called on behaves precisely as it did without it.
    /// </para>
    /// <para>
    /// A COMPLETED stream whose horizon lies beyond its last event PLAYS THE TRAILING REST OUT: the
    /// head runs on through the silence and the stream ends at the horizon, not at the last note.
    /// A declared rest is music, and a piece that ends with one ends when the rest does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The stream has been completed.</exception>
    public void AdvanceHorizon(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                "The tick the horizon is advanced to must be a non-negative value.");
        }

        lock (gate)
        {
            RequireNotCompleted();

            if (tick <= horizonTicks)
            {
                return;
            }

            // The horizon has moved, so a reader that cached the horizon's time learns its cache is
            // stale - the same signal an append gives.
            version++;
            horizonTicks = tick;
            settledTicks = tick;
        }
    }

    /// <summary>
    /// Says there is no more to come. Appending after this throws; playback advances without
    /// waiting for a pre-roll, drains to the last event and ends. Calling it again does nothing.
    /// </summary>
    public void Complete()
    {
        lock (gate)
        {
            completed = true;
        }
    }

    /// <summary>
    /// The moment a tick falls at, read from this stream's own tempo map.
    /// </summary>
    /// <param name="tick">The tick to place in time.</param>
    /// <returns>The time that tick falls at.</returns>
    /// <remarks>
    /// <para>
    /// Defined for EVERY tick, including ticks beyond the horizon that nothing has been written at
    /// yet: the last tempo the timeline carries goes on from there, so a producer can ask where a
    /// bar it has not written yet will fall. It is taken under the stream's own lock, so it is safe
    /// while the stream is playing and while its producer is appending - and the answer is a
    /// snapshot, which a later tempo change behind that tick can move.
    /// </para>
    /// <para>
    /// It depends on the MUSIC and on nothing else: tempo changes retime it, and the meta events a
    /// producer may carry on the conductor track - text, markers, key signatures - do not, however
    /// many of them there are. The playback clock takes one further step, through the tick the
    /// recording's conductor track ends at, because a merged MIDI file does; the two can therefore
    /// differ by a single hundred-nanosecond tick, and never by more.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> is negative.</exception>
    public TimeSpan TimeAtTick(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "The tick must be a non-negative value.");
        }

        lock (gate)
        {
            return TimeAtTickIndependentUnderGate(tick);
        }
    }

    /// <summary>
    /// The tick a moment falls at - the inverse of <see cref="TimeAtTick"/>.
    /// </summary>
    /// <param name="time">The moment to place on the timeline.</param>
    /// <returns>The tick that moment falls at.</returns>
    /// <remarks>
    /// Defined beyond the horizon in the same way: the last tempo carries on. It is the LARGEST
    /// tick whose <see cref="TimeAtTick"/> is at or before <paramref name="time"/>, so
    /// <c>TickAtTime(TimeAtTick(t))</c> gives back <c>t</c> for any resolution and tempo at which a
    /// tick lasts longer than a hundred nanoseconds - which is every musical one. Faster than that,
    /// several ticks share a moment and the last of them is the answer.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is negative.</exception>
    public long TickAtTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(time), time, "The time must be a non-negative value.");
        }

        lock (gate)
        {
            return TickAtTimeUnderGate(time);
        }
    }

    /// <summary>
    /// Takes a snapshot of the recording: a deep copy of every event appended so far, at its own
    /// tick, ready for export.
    /// </summary>
    /// <returns>
    /// A type 1 collection at this stream's resolution. Track 0 holds the conductor events - tempo,
    /// time signature, key signature, text, sequencer-specific and sysex - and each channel that
    /// has received a channel message has a track of its own, in the order the channels were first
    /// used. End-of-track events are already in place, so
    /// <c>MidiFile.Export(path, stream.ToMidiEventCollection())</c> is the one-line save.
    /// </returns>
    /// <remarks>
    /// Safe to call at any time from any thread, including while the stream is playing and while
    /// its producer is still appending. The copy is taken under the stream's lock, on the calling
    /// thread; for a long piece that is a matter of milliseconds.
    /// </remarks>
    public MidiEventCollection ToMidiEventCollection()
    {
        MidiEventCollection snapshot;

        lock (gate)
        {
            // The collection's own deep copy, which relinks each cloned note-on to the clone of its
            // own note-off: cloning the two separately would leave the copy's notes with fresh off
            // events nobody else points at, and editing the snapshot's note length would then move
            // a note-off the file does not carry.
            snapshot = recording.Clone();
        }

        snapshot.PrepareForExport();
        return snapshot;
    }

    /// <summary>
    /// Builds an ordinary playable sequence from a snapshot of the recording.
    /// </summary>
    /// <returns>The sequence, as <see cref="MidiSequence.FromEvents"/> builds it.</returns>
    /// <remarks>
    /// This is the offline path: looping, <c>SoundFontRenderer</c> and every other place that wants
    /// a finished piece take the sequence. A stream that was completed before playback began renders
    /// through <see cref="MidiStreamSequencer"/> exactly what this sequence renders through
    /// <see cref="MidiSequencer"/>, sample for sample.
    /// </remarks>
    public MidiSequence ToSequence() => MidiSequence.FromEvents(ToMidiEventCollection());

    /// <summary>The lock every reader and writer of the timeline takes.</summary>
    internal object Gate => gate;

    /// <summary>The sorted timeline. Only touch it while holding <see cref="Gate"/>.</summary>
    internal List<Entry> Entries => entries;

    /// <summary>
    /// The tick the recording's conductor track ends at, or -1 when it holds nothing.
    /// </summary>
    /// <remarks>
    /// The exported conductor track carries an end-of-track event at its last event's tick, and
    /// <c>MidiSequence.MergeTracks</c> walks that event like any other - which splits the tick
    /// delta that straddles it, and truncation makes a split delta differ from an unsplit one. Each
    /// CHANNEL track ends at one of its own messages, so its end-of-track never splits anything new;
    /// the conductor's can, because time signatures and text are not messages. The walk therefore
    /// steps through this tick, and the equivalence with <see cref="ToSequence"/> survives content
    /// whose conductor track ends after its last tempo change. Only touch it while holding
    /// <see cref="Gate"/>.
    /// </remarks>
    internal long ConductorEndTicks => conductorEndTicks;

    /// <summary>The horizon, while holding <see cref="Gate"/>.</summary>
    internal long HorizonTicksUnderGate => horizonTicks;

    /// <summary>
    /// The furthest tick <see cref="AdvanceHorizon"/> has declared settled, or -1 when it has never
    /// been called. A completed stream is not finished until the head has reached it, which is what
    /// makes a trailing rest play out. Only touch it while holding <see cref="Gate"/>.
    /// </summary>
    internal long SettledTicksUnderGate => settledTicks;

    /// <summary>Whether the producer has finished, while holding <see cref="Gate"/>.</summary>
    internal bool IsCompletedUnderGate => completed;

    /// <summary>The pre-roll, while holding <see cref="Gate"/>.</summary>
    internal TimeSpan PrerollUnderGate => preroll;

    /// <summary>
    /// How many times the timeline has been appended to. It changes whenever the horizon might
    /// have moved, and never otherwise, so a reader may cache anything derived from the timeline
    /// and refresh it on a version compare. Only touch it while holding <see cref="Gate"/>.
    /// </summary>
    internal int VersionUnderGate => version;

    /// <summary>
    /// The time a tick falls at, walking the timeline from its start exactly as playback does.
    /// Callers hold <see cref="Gate"/>.
    /// </summary>
    /// <param name="tick">The tick to place in time.</param>
    /// <returns>The time that tick falls at.</returns>
    internal TimeSpan TimeAtTickUnderGate(long tick)
    {
        var walk = default(Walk);
        walk.Reset();
        return TimeAtTickUnderGate(walk, tick);
    }

    /// <summary>
    /// The time a tick falls at, carrying on from where a walk already stands. Callers hold
    /// <see cref="Gate"/>.
    /// </summary>
    /// <param name="walk">
    /// The walk to continue from - a COPY, so the caller's own walk is left exactly where it was.
    /// Pass a reset walk to derive the time from the start of the timeline.
    /// </param>
    /// <param name="tick">The tick to place in time.</param>
    /// <returns>The time that tick falls at.</returns>
    /// <remarks>
    /// The one place the tick-to-time arithmetic lives: the sequencer's playback walk, the horizon
    /// time it caches, and <see cref="HorizonTime"/> all come through here, so there is nothing for
    /// them to disagree about. Continuing from a walk rather than from tick zero is what keeps the
    /// rendering thread's cost proportional to what has arrived since it last looked, and it gives
    /// the same answer: the steps a fresh walk would take to reach the frontier are exactly the
    /// steps the playback walk already took.
    /// </remarks>
    internal TimeSpan TimeAtTickUnderGate(Walk walk, long tick)
    {
        for (var i = walk.Index; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Tick > tick)
            {
                break;
            }

            if (!entry.SplitsTime)
            {
                continue;
            }

            if (conductorEndTicks > walk.Tick && conductorEndTicks < entry.Tick)
            {
                walk.AdvanceTo(conductorEndTicks, ticksPerQuarterNote);
            }

            walk.AdvanceTo(entry.Tick, ticksPerQuarterNote);

            if (entry.IsTempo)
            {
                walk.Tempo = entry.Message.Tempo;
            }
        }

        if (conductorEndTicks > walk.Tick && conductorEndTicks <= tick)
        {
            walk.AdvanceTo(conductorEndTicks, ticksPerQuarterNote);
        }

        if (tick > walk.Tick)
        {
            walk.AdvanceTo(tick, ticksPerQuarterNote);
        }

        return walk.Time;
    }

    /// <summary>
    /// The time a tick falls at, WITHOUT the step through the conductor track's end that the
    /// playback walk takes. Callers hold <see cref="Gate"/>.
    /// </summary>
    /// <param name="tick">The tick to place in time.</param>
    /// <returns>The time that tick falls at.</returns>
    /// <remarks>
    /// The public conversion's walk. <see cref="TimeAtTickUnderGate(Walk, long)"/> steps through
    /// <see cref="ConductorEndTicks"/> because <c>MidiSequence.MergeTracks</c> walks the
    /// end-of-track event the exported conductor track carries there - which is what makes a
    /// completed stream render what its sequence renders, and which is pinned by a test. The cost
    /// of that faithfulness is that appending a meta event nobody plays MOVES the tick a stream's
    /// conductor track ends at, and so splits one tick delta in two where there was one; each split
    /// truncates to a hundred-nanosecond tick, so the answer can shift by one of them. An answer a
    /// caller is given for its own arithmetic should not depend on how many markers the producer
    /// wrote, so this walk leaves that step out and the playback walk keeps it.
    /// </remarks>
    internal TimeSpan TimeAtTickIndependentUnderGate(long tick)
    {
        var walk = default(Walk);
        walk.Reset();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Tick > tick)
            {
                break;
            }

            if (!entry.SplitsTime)
            {
                continue;
            }

            walk.AdvanceTo(entry.Tick, ticksPerQuarterNote);

            if (entry.IsTempo)
            {
                walk.Tempo = entry.Message.Tempo;
            }
        }

        if (tick > walk.Tick)
        {
            walk.AdvanceTo(tick, ticksPerQuarterNote);
        }

        return walk.Time;
    }

    /// <summary>
    /// The tick a time falls at, the exact inverse of
    /// <see cref="TimeAtTickIndependentUnderGate"/>. Callers hold <see cref="Gate"/>.
    /// </summary>
    /// <param name="time">The moment to place on the timeline.</param>
    /// <returns>The tick that moment falls at.</returns>
    internal long TickAtTimeUnderGate(TimeSpan time)
    {
        var walk = default(Walk);
        walk.Reset();

        // Forward to the last stop the walk makes at or before the moment asked for; from there the
        // tempo is constant and the answer is arithmetic.
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!entry.SplitsTime)
            {
                continue;
            }

            if (walk.TimeAt(entry.Tick, ticksPerQuarterNote) > time)
            {
                break;
            }

            walk.AdvanceTo(entry.Tick, ticksPerQuarterNote);

            if (entry.IsTempo)
            {
                walk.Tempo = entry.Message.Tempo;
            }
        }

        return TickInSegment(walk, time);
    }

    // The largest tick of the segment the walk stands in whose own time is still at or before the
    // moment asked for. The estimate is the analytic answer - a tick's time is the walk's plus the
    // TRUNCATED product, so the last tick to fit is the one just under (room + 1) / product - and
    // the two single steps around it settle whatever the floating-point division rounded.
    private long TickInSegment(in Walk walk, TimeSpan time)
    {
        if (time <= walk.Time)
        {
            return walk.Tick;
        }

        var spanTicksPerTick = 60.0 / (ticksPerQuarterNote * walk.Tempo) * TimeSpan.TicksPerSecond;
        var estimate = ((time - walk.Time).Ticks + 1.0) / spanTicksPerTick;

        if (!(estimate > 0.0))
        {
            estimate = 0.0;
        }

        if (estimate > MaxTickAhead)
        {
            estimate = MaxTickAhead;
        }

        var ahead = (long)estimate;

        while (ahead > 0 && walk.TimeAt(walk.Tick + ahead, ticksPerQuarterNote) > time)
        {
            ahead--;
        }

        while (ahead < MaxTickAhead && walk.TimeAt(walk.Tick + ahead + 1, ticksPerQuarterNote) <= time)
        {
            ahead++;
        }

        return walk.Tick + ahead;
    }

    /// <summary>
    /// Claims this stream for a sequencer. Only one may play it at a time, and the sequencer that
    /// already holds it may claim it again - that is how a rewind is spelled. Callers hold
    /// <see cref="Gate"/>, so the claim and the sequencer's own reset happen under one acquisition
    /// and a producer appending on another thread sees one or the other, never a half-attached
    /// sequencer.
    /// </summary>
    /// <param name="sequencer">The sequencer claiming it.</param>
    /// <exception cref="InvalidOperationException">Another sequencer is playing this stream.</exception>
    internal void AttachUnderGate(MidiStreamSequencer sequencer)
    {
        if (attached != null && !ReferenceEquals(attached, sequencer))
        {
            throw new InvalidOperationException(
                "This MIDI stream is already being played; stop that sequencer before playing it again.");
        }

        attached = sequencer;
    }

    /// <summary>Releases the claim a sequencer took on this stream.</summary>
    /// <param name="sequencer">The sequencer releasing it.</param>
    internal void Detach(MidiStreamSequencer sequencer)
    {
        lock (gate)
        {
            DetachUnderGate(sequencer);
        }
    }

    /// <summary>
    /// Releases the claim a sequencer took on this stream, while holding <see cref="Gate"/> - so
    /// the release and the sequencer's own reset happen under one acquisition, as the claim does.
    /// </summary>
    /// <param name="sequencer">The sequencer releasing it.</param>
    internal void DetachUnderGate(MidiStreamSequencer sequencer)
    {
        if (ReferenceEquals(attached, sequencer))
        {
            attached = null;
        }
    }

    // Everything below runs under the lock, on the producer's thread.

    private void RequireNotCompleted()
    {
        if (completed)
        {
            throw new InvalidOperationException("The MIDI stream has been completed; no more events can be appended.");
        }
    }

    private static void Validate(MidiEvent midiEvent, string parameterName)
    {
        if (midiEvent.AbsoluteTime < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, midiEvent.AbsoluteTime,
                "The tick of an appended event must be a non-negative value.");
        }

        if (IsChannelCommand(midiEvent.CommandCode) && (midiEvent.Channel < 1 || midiEvent.Channel > 16))
        {
            throw new ArgumentOutOfRangeException(parameterName, midiEvent.Channel,
                "The channel of an appended event must be 1 to 16.");
        }
    }

    private static bool IsChannelCommand(MidiCommandCode commandCode)
    {
        switch (commandCode)
        {
            case MidiCommandCode.NoteOff:
            case MidiCommandCode.NoteOn:
            case MidiCommandCode.KeyAfterTouch:
            case MidiCommandCode.ControlChange:
            case MidiCommandCode.PatchChange:
            case MidiCommandCode.ChannelAfterTouch:
            case MidiCommandCode.PitchWheelChange:
                return true;

            default:
                return false;
        }
    }

    private void Take(MidiEvent midiEvent)
    {
        // Anything taken can move the horizon, and a reader that has cached the horizon's time
        // learns from this that its cache is stale.
        version++;

        if (MidiEvent.IsEndTrack(midiEvent))
        {
            if (!endOfTrackReported)
            {
                endOfTrackReported = true;
                Report("An end-of-track event was appended; a stream ends when its producer completes it, "
                    + "so the event was ignored.");
            }

            return;
        }

        var late = false;

        if (midiEvent is TempoEvent tempoEvent)
        {
            AddEntry(PlaybackTick(tempoEvent.AbsoluteTime, ref late), ConductorTrack,
                MidiSequence.Message.TempoChange(tempoEvent.MicrosecondsPerQuarterNote), playable: false,
                beatsPerBar: 0);
            RecordConductor(tempoEvent);
            return;
        }

        if (midiEvent is TimeSignatureEvent timeSignatureEvent)
        {
            var beatsPerBar = timeSignatureEvent.Numerator;
            if (beatsPerBar <= 0)
            {
                Report($"The time signature at tick {timeSignatureEvent.AbsoluteTime} declares no beats in a bar; "
                    + "it was recorded, and the beats-per-bar readback was left as it was.");
            }
            else
            {
                AddEntry(PlaybackTick(timeSignatureEvent.AbsoluteTime, ref late), ConductorTrack, default,
                    playable: false, beatsPerBar: beatsPerBar);
            }

            RecordConductor(timeSignatureEvent);
            return;
        }

        if (!IsChannelCommand(midiEvent.CommandCode))
        {
            // Key signatures, text, markers, sysex, sequencer-specific: kept, never played, exactly
            // as MidiSequence skips them when it reads a file.
            RecordConductor(midiEvent);
            return;
        }

        var track = TrackFor(midiEvent.Channel);

        if (TryBuildMessage(midiEvent, out var message))
        {
            AddEntry(PlaybackTick(midiEvent.AbsoluteTime, ref late), track, message, playable: true,
                beatsPerBar: 0);
        }
        else
        {
            Report($"The event at tick {midiEvent.AbsoluteTime} carries the command code "
                + $"{midiEvent.CommandCode} without the data it needs; it was recorded but not played.");
        }

        Record(midiEvent, track);

        if (midiEvent is NoteOnEvent noteOn && noteOn.OffEvent != null)
        {
            var offEvent = noteOn.OffEvent;

            // The note-off may not precede its note-on on the timeline, whatever the off event says.
            var offTick = offEvent.AbsoluteTime;
            if (offTick < midiEvent.AbsoluteTime)
            {
                offTick = midiEvent.AbsoluteTime;
            }

            if (TryBuildMessage(offEvent, out var offMessage))
            {
                AddEntry(PlaybackTick(offTick, ref late), track, offMessage, playable: true, beatsPerBar: 0);
            }

            Record(offEvent, track);
        }
    }

    private static bool TryBuildMessage(MidiEvent midiEvent, out MidiSequence.Message message)
    {
        // GetAsShortMessage packs exactly what MidiFile.Export writes: the status byte with the
        // channel already made 0-based, then the one or two data bytes. Every event type that
        // carries data validates it to 0-127, so the 7-bit masks below never lose anything.
        var raw = midiEvent.GetAsShortMessage();
        var status = (byte)(raw & 0xFF);
        if ((status & 0xF0) < 0x80 || (status & 0xF0) >= 0xF0)
        {
            message = default;
            return false;
        }

        message = MidiSequence.Message.Common(status, (byte)((raw >> 8) & 0x7F), (byte)((raw >> 16) & 0x7F),
            MidiSequenceLoopType.None);
        return true;
    }

    private long PlaybackTick(long tick, ref bool counted)
    {
        var sequencer = attached;
        if (sequencer == null)
        {
            return tick;
        }

        var head = sequencer.LateClampTicksUnderGate;
        if (tick >= head)
        {
            return tick;
        }

        if (!counted)
        {
            counted = true;
            lateEventCount++;
        }

        return head;
    }

    private void AddEntry(long tick, int track, in MidiSequence.Message message, bool playable, int beatsPerBar)
    {
        var entry = new Entry(tick, track, order++, message, playable, beatsPerBar);

        // Sorted by (tick, recording track, arrival order), which is exactly the order
        // MidiSequence.MergeTracks produces from the exported file: it takes the lowest track
        // index at a tick, and a track's own events keep the order they were written in. That, and
        // only that, is what lets a completed stream render what its sequence renders when several
        // channels write at one tick.
        //
        // In-order appends land at the end; a duration's note-off lands a few entries back.
        var index = entries.Count;
        if (index > 0 && Precedes(entry, entries[index - 1]))
        {
            var low = 0;
            var high = entries.Count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (Precedes(entry, entries[middle]))
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            index = low;
        }

        // A late event is clamped to the playing sequencer's frontier tick (C.5), but the tie-break
        // above could still sort it in front of an entry at that tick which the walk has already
        // consumed - where it would never be played. Nothing is ever dropped, so it goes in at the
        // frontier instead. The list stays sorted by tick; only the tie order of events already
        // gone by is disturbed.
        var sequencer = attached;
        if (sequencer != null && index < sequencer.FrontierIndexUnderGate)
        {
            index = sequencer.FrontierIndexUnderGate;
        }

        entries.Insert(index, entry);

        if (tick > horizonTicks)
        {
            horizonTicks = tick;
        }
    }

    // Whether the first entry sorts before the second: tick, then recording track, then arrival.
    private static bool Precedes(in Entry first, in Entry second)
    {
        if (first.Tick != second.Tick)
        {
            return first.Tick < second.Tick;
        }

        if (first.Track != second.Track)
        {
            return first.Track < second.Track;
        }

        return first.Order < second.Order;
    }

    private void RecordConductor(MidiEvent midiEvent)
    {
        Record(midiEvent, 0);

        if (midiEvent.AbsoluteTime > conductorEndTicks)
        {
            conductorEndTicks = midiEvent.AbsoluteTime;
        }
    }

    private void Record(MidiEvent midiEvent, int track)
    {
        recording.AddEvent(midiEvent, track);
        eventCount++;

        // Everything the producer wrote counts towards the horizon, whether or not it is played:
        // the horizon is how far the producer has got, and it is what the player's duration follows.
        if (midiEvent.AbsoluteTime > horizonTicks)
        {
            horizonTicks = midiEvent.AbsoluteTime;
        }
    }

    private int TrackFor(int channel)
    {
        if (channelTracks[channel] == 0)
        {
            channelTracks[channel] = nextTrack;
            nextTrack++;
        }

        return channelTracks[channel];
    }

    private void Report(string problem)
    {
        if (problems.Count > MaxProblems)
        {
            return;
        }

        if (problems.Count == MaxProblems)
        {
            problems.Add("Further problems were not recorded.");
            return;
        }

        problems.Add(problem);
    }

    /// <summary>
    /// One event on the timeline: where it falls, when it arrived, and what to do with it.
    /// </summary>
    /// <remarks>
    /// A PLAYABLE entry carries a channel message and is delivered to the synthesizer. A TEMPO
    /// entry carries a tempo change and retimes what follows. A TIME SIGNATURE entry only updates
    /// the beats-per-bar readback - and, unlike the other two, it is NOT a message in the
    /// equivalent MIDI file, so the walk must not let it split a tick delta.
    /// </remarks>
    internal readonly struct Entry
    {
        internal Entry(long tick, int track, int order, in MidiSequence.Message message, bool playable,
            int beatsPerBar)
        {
            Tick = tick;
            Track = track;
            Order = order;
            Message = message;
            Playable = playable;
            BeatsPerBar = beatsPerBar;
        }

        /// <summary>The tick the entry plays at, which a late event has had clamped to the head.</summary>
        internal long Tick { get; }

        /// <summary>
        /// The recording track the entry belongs to: zero for a conductor event, and otherwise the
        /// track the entry's channel was given when it was first used. It breaks ties at a tick
        /// the way the exported file's track order does.
        /// </summary>
        internal int Track { get; }

        /// <summary>The arrival order, which breaks ties between entries on the same track at the same tick.</summary>
        internal int Order { get; }

        /// <summary>The message to deliver, or the tempo change to apply.</summary>
        internal MidiSequence.Message Message { get; }

        /// <summary>Whether the message is delivered to the synthesizer.</summary>
        internal bool Playable { get; }

        /// <summary>The beats per bar a time signature entry carries; zero for anything else.</summary>
        internal int BeatsPerBar { get; }

        /// <summary>Whether the entry is a tempo change.</summary>
        internal bool IsTempo => !Playable && BeatsPerBar == 0;

        /// <summary>Whether the entry is a time signature.</summary>
        internal bool IsTimeSignature => BeatsPerBar > 0;

        /// <summary>Whether the entry is one of the events the tick-to-time walk steps through.</summary>
        internal bool SplitsTime => BeatsPerBar == 0;
    }

    /// <summary>
    /// The tick-to-time walk, kept in exactly the terms <c>MidiSequence.MergeTracks</c> keeps them:
    /// a tick, the time that tick falls at, and the tempo in force from there.
    /// </summary>
    internal struct Walk
    {
        /// <summary>The tick the walk has reached.</summary>
        internal long Tick;

        /// <summary>The time <see cref="Tick"/> falls at.</summary>
        internal TimeSpan Time;

        /// <summary>The tempo in force from <see cref="Tick"/> onwards, in beats per minute.</summary>
        internal double Tempo;

        /// <summary>How many entries of the timeline the walk has consumed.</summary>
        internal int Index;

        /// <summary>The beats per bar the last time signature reached declared.</summary>
        internal int BeatsPerBar;

        /// <summary>Returns the walk to the start of the timeline.</summary>
        internal void Reset()
        {
            Tick = 0;
            Time = TimeSpan.Zero;
            Tempo = DefaultTempo;
            Index = 0;
            BeatsPerBar = DefaultBeatsPerBar;
        }

        /// <summary>
        /// The time a tick falls at, from where the walk stands, without moving it.
        /// </summary>
        /// <param name="tick">The tick to place.</param>
        /// <param name="ticksPerQuarterNote">The stream's resolution.</param>
        /// <returns>The time that tick falls at.</returns>
        /// <remarks>
        /// The arithmetic is character for character what <c>MidiSequence.MergeTracks</c> performs
        /// per event, in the same order and on the same types, and it goes through the same
        /// <c>GetTimeSpanFromSeconds</c>. That, and nothing else, is what makes a completed stream
        /// render bit for bit what its sequence renders.
        /// </remarks>
        internal readonly TimeSpan TimeAt(long tick, int ticksPerQuarterNote) =>
            Time + MidiSequence.GetTimeSpanFromSeconds(60.0 / (ticksPerQuarterNote * Tempo) * (tick - Tick));

        /// <summary>Moves the walk to a tick, accumulating the time it takes to get there.</summary>
        /// <param name="tick">The tick to move to.</param>
        /// <param name="ticksPerQuarterNote">The stream's resolution.</param>
        internal void AdvanceTo(long tick, int ticksPerQuarterNote)
        {
            Time = TimeAt(tick, ticksPerQuarterNote);
            Tick = tick;
        }
    }
}

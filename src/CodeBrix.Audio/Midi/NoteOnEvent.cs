using System;
using System.IO;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Represents a MIDI note on event
/// </summary>
/// <remarks>
/// A note is TWO events - this one and the <see cref="OffEvent"/> it links to - and both of them
/// normally sit in the same <see cref="MidiEventCollection"/>. <see cref="NoteNumber"/> and
/// <see cref="Channel"/> push their new value into the off event, but
/// <see cref="MidiEvent.AbsoluteTime"/> does NOT: assigning it moves only the START of the note and
/// leaves the note-off where it was, so the note's length changes without anyone asking for it. To
/// move a whole note - shifting a segment onto a timeline, quantising, rescaling to another
/// resolution - call <see cref="MoveTo"/>, which carries the off event along, or build a fresh
/// event. The plain setter is deliberately left alone, because a loop that shifts every event of a
/// collection by assigning <see cref="MidiEvent.AbsoluteTime"/> reaches the note-off in its own
/// right and would otherwise move it twice.
/// </remarks>
public class NoteOnEvent : NoteEvent
{
    private NoteEvent offEvent;

    /// <summary>
    /// Reads a new Note On event from a stream of MIDI data
    /// </summary>
    /// <param name="br">Binary reader on the MIDI data stream</param>
    public NoteOnEvent(BinaryReader br)
        : base(br)
    {
    }

    /// <summary>
    /// Creates a NoteOn event with specified parameters
    /// </summary>
    /// <param name="absoluteTime">Absolute time of this event</param>
    /// <param name="channel">MIDI channel number</param>
    /// <param name="noteNumber">MIDI note number</param>
    /// <param name="velocity">MIDI note velocity</param>
    /// <param name="duration">MIDI note duration</param>
    public NoteOnEvent(long absoluteTime, int channel, int noteNumber,
        int velocity, int duration)
        : base(absoluteTime, channel, MidiCommandCode.NoteOn, noteNumber, velocity)
    {
        OffEvent = new NoteEvent(absoluteTime, channel, MidiCommandCode.NoteOff,
            noteNumber, 0);
        NoteLength = duration;
    }

    /// <summary>
    /// Creates a deep clone of this MIDI event.
    /// </summary>
    /// <remarks>
    /// A note-on carrying no note-off clones to one that carries no note-off either, rather than
    /// failing. That shape only arises from a file that breaks the rules - tolerant reading closes
    /// a dangling note at the end of its track - but a caller that built the event itself, or that
    /// read the file strictly enough to still hold one, can copy it.
    /// </remarks>
    //was previously: the clone was always built by the (absoluteTime, channel, noteNumber,
    //velocity, duration) constructor, which reads NoteLength - and NoteLength THROWS when there is
    //no note-off to measure against. Cloning a dangling note-on was therefore an
    //InvalidOperationException rather than a copy, and one such event made a whole collection
    //uncopyable. The clone of a note-on that HAS a note-off is built exactly as it always was.
    public override MidiEvent Clone() =>
        OffEvent == null
            ? (MidiEvent)MemberwiseClone()
            : new NoteOnEvent(AbsoluteTime, Channel, NoteNumber, Velocity, NoteLength);

    /// <summary>
    /// Moves the whole note to a new absolute time, keeping its length.
    /// </summary>
    /// <param name="absoluteTime">The tick the note is to start at.</param>
    /// <remarks>
    /// The linked <see cref="OffEvent"/> moves by the same amount, so <see cref="NoteLength"/> is
    /// what it was; a note-on with no off event simply moves. This is the method to re-time a note
    /// with - assigning <see cref="MidiEvent.AbsoluteTime"/> moves only the START and silently
    /// changes the length. Whatever <see cref="MidiEvent.AbsoluteTime"/> accepts is accepted here.
    /// </remarks>
    public void MoveTo(long absoluteTime)
    {
        var delta = absoluteTime - AbsoluteTime;

        AbsoluteTime = absoluteTime;

        if (offEvent != null)
        {
            offEvent.AbsoluteTime += delta;
        }
    }

    /// <summary>
    /// The associated Note off event
    /// </summary>
    /// <remarks>
    /// It is a SEPARATE event, which normally sits in the same collection in its own right. Moving
    /// this note-on with <see cref="MidiEvent.AbsoluteTime"/> leaves the off event alone; use
    /// <see cref="MoveTo"/> to move the pair together.
    /// </remarks>
    public NoteEvent OffEvent
    {
        get
        {
            return offEvent;
        }
        set
        {
            if (!IsNoteOff(value))
            {
                throw new ArgumentException("OffEvent must be a valid MIDI note off event");
            }
            if (value.NoteNumber != NoteNumber)
            {
                throw new ArgumentException("Note Off Event must be for the same note number");
            }
            if (value.Channel != Channel)
            {
                throw new ArgumentException("Note Off Event must be for the same channel");
            }
            offEvent = value;

        }
    }

    /// <summary>
    /// Get or set the Note Number, updating the off event at the same time
    /// </summary>
    public override int NoteNumber
    {
        get
        {
            return base.NoteNumber;
        }
        set
        {
            base.NoteNumber = value;
            if (OffEvent != null)
            {
                OffEvent.NoteNumber = NoteNumber;
            }
        }
    }

    /// <summary>
    /// Get or set the channel, updating the off event at the same time
    /// </summary>
    public override int Channel
    {
        get
        {
            return base.Channel;
        }
        set
        {
            base.Channel = value;
            if (OffEvent != null)
            {
                OffEvent.Channel = Channel;
            }
        }
    }

    /// <summary>
    /// The duration of this note
    /// </summary>
    /// <remarks>
    /// <para>
    /// There must be a note off event.
    /// </para>
    /// <para>
    /// It is DERIVED: the gap between this event's tick and its <see cref="OffEvent"/>'s, read
    /// afresh each time. Assigning <see cref="MidiEvent.AbsoluteTime"/> therefore changes it
    /// without being asked to - <see cref="MoveTo"/> is the way to move a note and keep its length.
    /// A note whose off event has been left BEHIND its note-on reads as zero rather than as a
    /// negative duration, because a note that ends before it starts is no note at all and every
    /// consumer of a length - an exporter, a stream that takes a note with a duration, an arranger
    /// re-timing a phrase - would otherwise have to guard against the negative one.
    /// </para>
    /// </remarks>
    //was previously: the getter returned offEvent.AbsoluteTime - AbsoluteTime unguarded, so a
    //note-on moved past its own note-off read as a NEGATIVE length. Upstream behaves that way too;
    //the clamp is ours. Nothing in this repository ever relied on the negative value.
    public int NoteLength
    {
        get
        {
            if (offEvent == null)
            {
                throw new InvalidOperationException("Cannot get NoteLength when OffEvent is null");
            }

            var length = offEvent.AbsoluteTime - AbsoluteTime;

            return length < 0 ? 0 : (int)length;
        }
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("NoteLength must be 0 or greater");
            }
            if (offEvent == null)
            {
                throw new InvalidOperationException("Cannot set NoteLength when OffEvent is null");
            }

            offEvent.AbsoluteTime = AbsoluteTime + value;
        }
    }

    /// <summary>
    /// Calls base class export first, then exports the data 
    /// specific to this event
    /// <seealso cref="MidiEvent.Export">MidiEvent.Export</seealso>
    /// </summary>
    public override string ToString()
    {
        if ((Velocity == 0) && (OffEvent == null))
        {
            return $"{base.ToString()} (Note Off)";
        }
        return $"{base.ToString()} Len: {((OffEvent == null) ? "?" : NoteLength.ToString())}";
    }
}

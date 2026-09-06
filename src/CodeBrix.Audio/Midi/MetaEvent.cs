using System;
using System.IO;
using System.Text;
using CodeBrix.Audio.Midi.Internal;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Represents a MIDI meta event
/// </summary>
public class MetaEvent : MidiEvent
{
    private MetaEventType metaEvent;
    internal int metaDataLength;

    /// <summary>
    /// Gets the type of this meta event
    /// </summary>
    public MetaEventType MetaEventType
    {
        get
        {
            return metaEvent;
        }
    }

    /// <summary>
    /// Empty constructor
    /// </summary>
    protected MetaEvent()
    {
    }

    /// <summary>
    /// Custom constructor for use by derived types, who will manage the data themselves
    /// </summary>
    /// <param name="metaEventType">Meta event type</param>
    /// <param name="metaDataLength">Meta data length</param>
    /// <param name="absoluteTime">Absolute time</param>
    public MetaEvent(MetaEventType metaEventType, int metaDataLength, long absoluteTime)
        : base(absoluteTime,1,MidiCommandCode.MetaEvent)
    {
        this.metaEvent = metaEventType;
        this.metaDataLength = metaDataLength;
    }

    /// <summary>
    /// Creates a deep clone of this MIDI event.
    /// </summary>
    public override MidiEvent Clone() => new MetaEvent(metaEvent, metaDataLength, AbsoluteTime);

    /// <summary>
    /// Reads a meta-event from a stream, rejecting anything the specification does not allow.
    /// </summary>
    /// <param name="br">A binary reader based on the stream of MIDI data</param>
    /// <returns>A new MetaEvent object</returns>
    /// <exception cref="FormatException">The event's data does not match its declared type.</exception>
    public static MetaEvent ReadMetaEvent(BinaryReader br)
    {
        return ReadMetaEvent(br, null);
    }

    // The lenient entry point. With a null context, or a strict one, this behaves exactly as the
    // public overload always has. With a tolerant context, a meta event whose payload cannot be
    // decoded - a wrong declared length, an out-of-spec value, a type whose reader throws - is kept
    // verbatim as a RawMetaEvent and the reason is recorded, so the reader can carry on at the right
    // byte instead of losing its place in the track.
    internal static MetaEvent ReadMetaEvent(BinaryReader br, MidiReadContext context)
    {
        var metaEvent = (MetaEventType)br.ReadByte();
        var length = ReadVarInt(br);

        if (context == null || context.IsStrict)
        {
            return ReadTypedMetaEvent(br, metaEvent, length, MidiReadMode.Strict);
        }

        var start = br.BaseStream.Position;
        MetaEvent me;
        try
        {
            me = ReadTypedMetaEvent(br, metaEvent, length, MidiReadMode.Tolerant);
        }
        catch (Exception exception) when (IsRecoverableReadFailure(exception))
        {
            context.Add(
                $"Meta event 0x{(byte)metaEvent:X2} of length {length} could not be decoded " +
                $"({exception.Message}); its bytes were kept as raw data.");
            return ReadRawPayload(br, metaEvent, length, start);
        }

        var consumed = br.BaseStream.Position - start;
        if (consumed != length)
        {
            context.Add(
                $"Meta event 0x{(byte)metaEvent:X2} declares {length} byte(s) of data but " +
                $"{consumed} were read; its bytes were kept as raw data.");
            return ReadRawPayload(br, metaEvent, length, start);
        }

        if (me is KeySignatureEvent keySignature && !keySignature.IsWithinSpecification)
        {
            context.Add(
                $"Key signature event carries sharps/flats {keySignature.RawSharpsFlats} and " +
                $"major/minor {keySignature.RawMajorMinor}, which the specification does not allow; " +
                "the raw bytes were kept.");
        }

        return me;
    }

    private static bool IsRecoverableReadFailure(Exception exception) =>
        exception is FormatException
        || exception is InvalidDataException
        || exception is EndOfStreamException
        || exception is ArgumentException
        || exception is OverflowException;

    private static MetaEvent ReadRawPayload(BinaryReader br, MetaEventType metaEvent, int length, long start)
    {
        if (br.BaseStream.CanSeek)
        {
            br.BaseStream.Position = start;
        }

        // Whatever is actually there, up to the declared length. A truncated payload keeps the
        // bytes that exist so that the event still exports as a self-consistent meta event.
        byte[] data = length > 0 ? br.ReadBytes(length) : [];
        return new RawMetaEvent(metaEvent, default(long), data);
    }

    private static MetaEvent ReadTypedMetaEvent(BinaryReader br, MetaEventType metaEvent, int length, MidiReadMode readMode)
    {
        MetaEvent me = new MetaEvent();
        switch(metaEvent)
        {
        case MetaEventType.TrackSequenceNumber: // Sets the track's sequence number.
            me = new TrackSequenceNumberEvent(br,length);
            break;
        case MetaEventType.TextEvent: // Text event
        case MetaEventType.Copyright: // Copyright
        case MetaEventType.SequenceTrackName: // Sequence / Track Name
        case MetaEventType.TrackInstrumentName: // Track instrument name
        case MetaEventType.Lyric: // lyric
        case MetaEventType.Marker: // marker
        case MetaEventType.CuePoint: // cue point
        case MetaEventType.ProgramName:
        case MetaEventType.DeviceName:
            me = new TextEvent(br,length);
            break;
        case MetaEventType.EndTrack: // This event must come at the end of each track
            if(length != 0)
            {
                throw new FormatException("End track length");
            }
            break;
        case MetaEventType.SetTempo: // Set tempo
            me = new TempoEvent(br,length);
            break;
        case MetaEventType.TimeSignature: // Time signature
            me = new TimeSignatureEvent(br,length);
            break;
        case MetaEventType.KeySignature: // Key signature
            me = new KeySignatureEvent(br, length, readMode);
            break;
        case MetaEventType.SequencerSpecific: // Sequencer specific information
            me = new SequencerSpecificEvent(br, length);
            break;
        case MetaEventType.SmpteOffset:
            me = new SmpteOffsetEvent(br, length);
            break;
        default:
            // A meta event type this package does not model. Nothing is lost: the bytes are kept
            // verbatim and written back unchanged on export, so this is not reported as a problem.
            var data = br.ReadBytes(length);
            if (data.Length != length)
            {
                throw new FormatException("Failed to read metaevent's data fully");
            }
            return new RawMetaEvent(metaEvent, default(long), data);
        }
        me.metaEvent = metaEvent;
        me.metaDataLength = length;

        return me;
    }

    /// <summary>
    /// Describes this meta event
    /// </summary>
    public override string ToString()
    {
        return $"{AbsoluteTime} {metaEvent}";
    }

    /// <summary>
    /// <see cref="MidiEvent.Export"/>
    /// </summary>
    public override void Export(ref long absoluteTime, BinaryWriter writer)
    {
        base.Export(ref absoluteTime, writer);
        writer.Write((byte)metaEvent);
        WriteVarInt(writer, metaDataLength);
    }
}

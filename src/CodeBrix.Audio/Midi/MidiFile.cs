using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using CodeBrix.Audio.Midi.Internal;
using CodeBrix.Audio.Utils;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Class able to read a MIDI file
/// </summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MidiReadMode.Tolerant"/> unless a mode is asked for: a file that breaks the
/// Standard MIDI File specification still loads, and every departure is listed in
/// <see cref="Problems"/>. <see cref="MidiReadMode.Strict"/> restores the validating behaviour and
/// throws instead.
/// </para>
/// <para>
/// What tolerance covers: out-of-range key signatures (kept as raw bytes), meta events whose payload
/// does not match their declared type or length (kept as <see cref="RawMetaEvent"/>), meta event
/// types this package does not model (kept as <see cref="RawMetaEvent"/>), chunks that are not
/// tracks (skipped), note-on events with no note-off (closed at the end of their track), note-off
/// events with no note-on (kept), events after the end-of-track event (kept), and a track whose
/// bytes stop making sense (the rest of that track is skipped, the rest of the file is read).
/// </para>
/// </remarks>
public class MidiFile
{
    private readonly MidiEventCollection events;
    private readonly ushort fileFormat;
    //private ushort tracks;
    private readonly ushort deltaTicksPerQuarterNote;
    private readonly bool strictChecking;
    private readonly MidiReadMode readMode;
    private readonly IReadOnlyList<string> problems;

    /// <summary>
    /// Opens a MIDI file for reading, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="filename">Name of MIDI file</param>
    /// <remarks>Anything that could not be honoured is listed in <see cref="Problems"/>. Pass
    /// <see cref="MidiReadMode.Strict"/> to have such content throw instead.</remarks>
    public MidiFile(string filename)
        : this(filename, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// MIDI File format
    /// </summary>
    public int FileFormat => fileFormat;

    /// <summary>
    /// Opens a MIDI file for reading
    /// </summary>
    /// <param name="filename">Name of MIDI file</param>
    /// <param name="strictChecking">If true will error on non-paired note events; shorthand for
    /// <see cref="MidiReadMode.Strict"/> against <see cref="MidiReadMode.Tolerant"/></param>
    public MidiFile(string filename, bool strictChecking) :
        this(File.OpenRead(filename), ToReadMode(strictChecking), true)
    {
    }

    /// <summary>
    /// Opens a MIDI file for reading
    /// </summary>
    /// <param name="filename">Name of MIDI file</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw</param>
    public MidiFile(string filename, MidiReadMode readMode) :
        this(File.OpenRead(filename), readMode, true)
    {
    }

    /// <summary>
    /// Opens a MIDI file stream for reading, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="inputStream">The input stream containing a MIDI file</param>
    /// <remarks>Anything that could not be honoured is listed in <see cref="Problems"/>.</remarks>
    public MidiFile(Stream inputStream) :
        this(inputStream, MidiReadMode.Tolerant, false)
    {
    }

    /// <summary>
    /// Opens a MIDI file stream for reading
    /// </summary>
    /// <param name="inputStream">The input stream containing a MIDI file</param>
    /// <param name="strictChecking">If true will error on non-paired note events; shorthand for
    /// <see cref="MidiReadMode.Strict"/> against <see cref="MidiReadMode.Tolerant"/></param>
    public MidiFile(Stream inputStream, bool strictChecking) :
        this(inputStream, ToReadMode(strictChecking), false)
    {
    }

    /// <summary>
    /// Opens a MIDI file stream for reading
    /// </summary>
    /// <param name="inputStream">The input stream containing a MIDI file</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw</param>
    public MidiFile(Stream inputStream, MidiReadMode readMode) :
        this(inputStream, readMode, false)
    {
    }

    private static MidiReadMode ToReadMode(bool strictChecking) =>
        strictChecking ? MidiReadMode.Strict : MidiReadMode.Tolerant;

    private MidiFile(Stream inputStream, MidiReadMode readMode, bool ownInputStream)
    {
        this.readMode = readMode;
        strictChecking = readMode == MidiReadMode.Strict;
        var context = new MidiReadContext(readMode);
        problems = context.Problems;

        var br = new BinaryReader(inputStream);
        try
        {
            string chunkHeader = Encoding.UTF8.GetString(br.ReadBytes(4));
            if (chunkHeader == "RIFF")
            {
                // RIFF-RMID wrapper: a standard MIDI file embedded in a RIFF container
                // (issue #1236). Skip the wrapper so the inner SMF is read as normal.
                SeekToRmidMidiData(br);
                chunkHeader = Encoding.UTF8.GetString(br.ReadBytes(4));
            }
            if(chunkHeader != "MThd")
            {
                throw new FormatException("Not a MIDI file - header chunk missing");
            }
            uint chunkSize = SwapUInt32(br.ReadUInt32());

            if(chunkSize < 6)
            {
                throw new FormatException("Unexpected header chunk length");
            }
            if(chunkSize != 6)
            {
                if (strictChecking)
                {
                    throw new FormatException("Unexpected header chunk length");
                }
                context.Add($"The header chunk declares {chunkSize} bytes instead of 6; the extra bytes were skipped.");
            }
            // 0 = single track, 1 = multi-track synchronous, 2 = multi-track asynchronous
            fileFormat = SwapUInt16(br.ReadUInt16());
            int tracks = SwapUInt16(br.ReadUInt16());
            deltaTicksPerQuarterNote = SwapUInt16(br.ReadUInt16());
            if (chunkSize > 6)
            {
                br.BaseStream.Position += chunkSize - 6;
            }

            events = new MidiEventCollection(fileFormat, deltaTicksPerQuarterNote);
            for (int n = 0; n < tracks; n++)
            {
                events.AddTrack();
            }

            long absoluteTime = 0;

            for(int track = 0; track < tracks; track++)
            {
                if(fileFormat != 0)
                {
                    absoluteTime = 0;
                }
                if (!SeekToNextTrackChunk(br, context, out chunkSize))
                {
                    context.Add($"The file declares {tracks} track(s) but only {track} were found.");
                    break;
                }

                long startPos = br.BaseStream.Position;
                MidiEvent me = null;
                // Only channel-voice messages (NoteOn/Off, ControlChange, etc.) establish
                // running status; meta and sysex events leave the running status anchor intact
                // (issue #205).
                MidiEvent runningStatus = null;
                var outstandingNoteOns = new List<NoteOnEvent>();
                var orphanNoteOffs = 0;
                var reportedEventsAfterEndTrack = false;
                var abandonedTrack = false;
                while(br.BaseStream.Position < startPos + chunkSize)
                {
                    try
                    {
                        me = MidiEvent.ReadNextEvent(br, runningStatus, context);
                    }
                    catch (Exception exception) when (!strictChecking && IsRecoverableReadFailure(exception))
                    {
                        // The stream is no longer on an event boundary and there is no way to find
                        // the next one: what follows would be parsed as noise. Give up on this
                        // track, keep what was read, and carry on with the next one.
                        context.Add(
                            $"Track {track}: reading stopped {br.BaseStream.Position - startPos} byte(s) into the " +
                            $"track ({exception.Message}); the rest of the track was skipped.");
                        abandonedTrack = true;
                        break;
                    }

                    if (me.CommandCode < MidiCommandCode.Sysex)
                    {
                        runningStatus = me;
                    }
                    absoluteTime += me.DeltaTime;
                    me.AbsoluteTime = absoluteTime;
                    events[track].Add(me);
                    if (me.CommandCode == MidiCommandCode.NoteOn)
                    {
                        var ne = (NoteEvent) me;
                        if(ne.Velocity > 0)
                        {
                            outstandingNoteOns.Add((NoteOnEvent) ne);
                        }
                        else
                        {
                            // don't remove the note offs, even though
                            // they are annoying
                            // events[track].Remove(me);
                            if (!FindNoteOn(ne, outstandingNoteOns))
                            {
                                orphanNoteOffs++;
                            }
                        }
                    }
                    else if(me.CommandCode == MidiCommandCode.NoteOff)
                    {
                        if (!FindNoteOn((NoteEvent)me, outstandingNoteOns))
                        {
                            orphanNoteOffs++;
                        }
                    }
                    else if(me.CommandCode == MidiCommandCode.MetaEvent)
                    {
                        MetaEvent metaEvent = (MetaEvent) me;
                        if(metaEvent.MetaEventType == MetaEventType.EndTrack)
                        {
                            //break;
                            // some dodgy MIDI files have an event after end track
                            if (br.BaseStream.Position < startPos + chunkSize)
                            {
                                if (strictChecking)
                                {
                                    throw new FormatException(
                                        $"End Track event was not the last MIDI event on track {track}");
                                }
                                if (!reportedEventsAfterEndTrack)
                                {
                                    reportedEventsAfterEndTrack = true;
                                    context.Add(
                                        $"Track {track}: more events follow the end-of-track event; they were kept.");
                                }
                            }
                        }
                    }
                }
                if(outstandingNoteOns.Count > 0)
                {
                    if (strictChecking)
                    {
                        throw new FormatException(
                            $"Note ons without note offs {outstandingNoteOns.Count} (file format {fileFormat})");
                    }
                    context.Add(
                        $"Track {track}: {outstandingNoteOns.Count} note on event(s) had no note off; " +
                        "each was closed at the end of the track.");
                    CloseOutstandingNoteOns(events[track], outstandingNoteOns, absoluteTime);
                }
                if (orphanNoteOffs > 0)
                {
                    context.Add($"Track {track}: {orphanNoteOffs} note off event(s) had no matching note on.");
                }
                if(br.BaseStream.Position != startPos + chunkSize)
                {
                    if (strictChecking)
                    {
                        throw new FormatException($"Read too far {chunkSize}+{startPos}!={br.BaseStream.Position}");
                    }
                    if (!abandonedTrack)
                    {
                        context.Add(
                            $"Track {track}: the track chunk declares {chunkSize} byte(s) but " +
                            $"{br.BaseStream.Position - startPos} were read; the reader was moved to the end of the chunk.");
                    }
                    br.BaseStream.Position = startPos + chunkSize;
                }
            }
        }
        finally
        {
            if (ownInputStream)
            {
                br.Dispose();
            }
        }
    }

    private static bool IsRecoverableReadFailure(Exception exception) =>
        exception is FormatException
        || exception is InvalidDataException
        || exception is EndOfStreamException
        || exception is ArgumentException
        || exception is OverflowException;

    // Finds the next MTrk chunk, skipping any chunk that is not one (the specification says an
    // unrecognised chunk type must be skipped, but the strict reader has always refused). Returns
    // false when the file ends before another track chunk turns up.
    private bool SeekToNextTrackChunk(BinaryReader br, MidiReadContext context, out uint chunkSize)
    {
        while (true)
        {
            byte[] headerBytes = br.ReadBytes(4);
            if (headerBytes.Length < 4)
            {
                if (strictChecking)
                {
                    throw new FormatException("Invalid chunk header");
                }
                chunkSize = 0;
                return false;
            }

            string chunkHeader = Encoding.UTF8.GetString(headerBytes);
            if (chunkHeader != "MTrk" && strictChecking)
            {
                throw new FormatException("Invalid chunk header");
            }

            chunkSize = SwapUInt32(br.ReadUInt32());
            if (chunkHeader == "MTrk")
            {
                return true;
            }

            context.Add($"Chunk '{chunkHeader}' is not a track chunk; its {chunkSize} byte(s) were skipped.");
            long target = br.BaseStream.Position + chunkSize;
            if (target > br.BaseStream.Length)
            {
                chunkSize = 0;
                return false;
            }
            br.BaseStream.Position = target;
        }
    }

    // Gives every note-on left hanging at the end of a track a note-off at the track's last tick,
    // inserted before the end-of-track event so that the track stays exportable. Without this a
    // consumer reading NoteOnEvent.NoteLength gets an InvalidOperationException instead of a note.
    private static void CloseOutstandingNoteOns(IList<MidiEvent> trackEvents, List<NoteOnEvent> outstandingNoteOns, long endTime)
    {
        int insertAt = trackEvents.Count;
        if (insertAt > 0 && MidiEvent.IsEndTrack(trackEvents[insertAt - 1]))
        {
            insertAt--;
        }

        foreach (NoteOnEvent noteOnEvent in outstandingNoteOns)
        {
            long offTime = Math.Max(endTime, noteOnEvent.AbsoluteTime);
            var offEvent = new NoteEvent(offTime, noteOnEvent.Channel, MidiCommandCode.NoteOff,
                noteOnEvent.NoteNumber, 0);
            noteOnEvent.OffEvent = offEvent;
            trackEvents.Insert(insertAt, offEvent);
            insertAt++;
        }
    }

    /// <summary>
    /// The collection of events in this MIDI file
    /// </summary>
    public MidiEventCollection Events => events;

    /// <summary>
    /// The mode this file was read in.
    /// </summary>
    public MidiReadMode ReadMode => readMode;

    /// <summary>
    /// Everything in the file that could not be honoured as written, one human-readable line each:
    /// out-of-range values kept as raw bytes, meta events that could not be decoded, notes that were
    /// never released, bytes that were skipped. Empty for a file that follows the specification.
    /// Never thrown; reading with <see cref="MidiReadMode.Strict"/> throws instead of filling this.
    /// </summary>
    /// <remarks>The list is capped, so a thoroughly corrupt file cannot grow it without bound; the
    /// last entry then says that further problems were not recorded.</remarks>
    public IReadOnlyList<string> Problems => problems;

    /// <summary>
    /// Number of tracks in this MIDI file
    /// </summary>
    public int Tracks => events.Tracks;

    /// <summary>
    /// Delta Ticks Per Quarter Note
    /// </summary>
    public int DeltaTicksPerQuarterNote => deltaTicksPerQuarterNote;

    private bool FindNoteOn(NoteEvent offEvent, List<NoteOnEvent> outstandingNoteOns)
    {
        bool found = false;
        foreach(NoteOnEvent noteOnEvent in outstandingNoteOns)
        {
            if ((noteOnEvent.Channel == offEvent.Channel) && (noteOnEvent.NoteNumber == offEvent.NoteNumber))
            {
                noteOnEvent.OffEvent = offEvent;
                outstandingNoteOns.Remove(noteOnEvent);
                found = true;
                break;
            }
        }
        if(!found)
        {
            if (strictChecking)
            {
                throw new FormatException($"Got an off without an on {offEvent}");
            }
        }
        return found;
    }
    
    private static void SeekToRmidMidiData(BinaryReader br)
    {
        // "RIFF" has already been consumed. Layout:
        //   'RIFF' <uint32 LE size> 'RMID' { <subChunkId> <uint32 LE size> <payload, word-aligned> }*
        // The standard MIDI file lives in the 'data' sub-chunk; any other chunks
        // (e.g. 'INFO' metadata or an embedded 'DLS ' soundbank) are skipped.
        br.ReadUInt32(); // overall RIFF size - not needed
        string formType = Encoding.UTF8.GetString(br.ReadBytes(4));
        if (formType != "RMID")
        {
            throw new FormatException($"Not a MIDI file - unsupported RIFF form type '{formType}'");
        }
        while (true)
        {
            byte[] idBytes = br.ReadBytes(4);
            if (idBytes.Length < 4)
            {
                throw new FormatException("Not a MIDI file - RIFF-RMID 'data' chunk missing");
            }
            uint subChunkSize = br.ReadUInt32(); // RIFF sizes are little-endian
            if (Encoding.UTF8.GetString(idBytes) == "data")
            {
                // The embedded standard MIDI file (starting with 'MThd') begins here.
                return;
            }
            // RIFF chunks are word-aligned: an odd-length chunk is followed by a pad byte.
            long toSkip = subChunkSize + (subChunkSize & 1);
            if (br.BaseStream.CanSeek)
            {
                br.BaseStream.Seek(toSkip, SeekOrigin.Current);
            }
            else
            {
                while (toSkip > 0)
                {
                    int chunk = (int)Math.Min(toSkip, 8192);
                    if (br.Read(new byte[chunk], 0, chunk) <= 0)
                    {
                        throw new FormatException("Not a MIDI file - RIFF-RMID 'data' chunk missing");
                    }
                    toSkip -= chunk;
                }
            }
        }
    }

    private static uint SwapUInt32(uint i)
    {
        return ((i & 0xFF000000) >> 24) | ((i & 0x00FF0000) >> 8) | ((i & 0x0000FF00) << 8) | ((i & 0x000000FF) << 24);
    }

    private static ushort SwapUInt16(ushort i) 
    {
        return (ushort) (((i & 0xFF00) >> 8) | ((i & 0x00FF) << 8));
    }
    
    /// <summary>
    /// Describes the MIDI file
    /// </summary>
    /// <returns>A string describing the MIDI file and its events</returns>
    public override string ToString() 
    {
        var sb = new StringBuilder();
        sb.AppendFormat("Format {0}, Tracks {1}, Delta Ticks Per Quarter Note {2}\r\n",
            fileFormat,Tracks,deltaTicksPerQuarterNote);
        for (var n = 0; n < Tracks; n++)
        {
            foreach (var midiEvent in events[n])
            {
                sb.AppendFormat("{0}\r\n", midiEvent);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exports a MIDI file
    /// </summary>
    /// <param name="filename">Filename to export to</param>
    /// <param name="events">Events to export</param>
    public static void Export(string filename, MidiEventCollection events)
    {
        using (var stream = File.Create(filename))
        {
            Export(stream, events, leaveOpen: true);
        }
    }

    /// <summary>
    /// Exports a MIDI file to a stream.
    /// </summary>
    /// <param name="stream">Stream to write to. Must be writable and seekable: track chunk lengths are
    /// written after the fact, which requires seeking back over the track that was just written.</param>
    /// <param name="events">Events to export</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is left open once writing
    /// finishes; when <see langword="false"/> (the default), it is closed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot seek, or more than one track was supplied
    /// for a type 0 file.</exception>
    public static void Export(Stream stream, MidiEventCollection events, bool leaveOpen = false)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must be seekable.", nameof(stream));
        }
        if (events.MidiFileType == 0 && events.Tracks > 1)
        {
            throw new ArgumentException("Can't export more than one track to a type 0 file");
        }
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen))
        {
            writer.Write(Encoding.UTF8.GetBytes("MThd"));
            writer.Write(SwapUInt32(6)); // chunk size
            writer.Write(SwapUInt16((ushort)events.MidiFileType));
            writer.Write(SwapUInt16((ushort)events.Tracks));
            writer.Write(SwapUInt16((ushort)events.DeltaTicksPerQuarterNote));

            for (int track = 0; track < events.Tracks; track++ )
            {
                IList<MidiEvent> eventList = events[track];

                writer.Write(Encoding.UTF8.GetBytes("MTrk"));
                long trackSizePosition = writer.BaseStream.Position;
                writer.Write(SwapUInt32(0));

                long absoluteTime = events.StartAbsoluteTime;

                // use a stable sort to preserve ordering of MIDI events whose 
                // absolute times are the same
                MergeSort.Sort(eventList, new MidiEventComparer());
                if (eventList.Count > 0)
                {
                    System.Diagnostics.Debug.Assert(MidiEvent.IsEndTrack(eventList[eventList.Count - 1]), "Exporting a track with a missing end track");
                }
                foreach (var midiEvent in eventList)
                {
                    midiEvent.Export(ref absoluteTime, writer);
                }

                uint trackChunkLength = (uint)(writer.BaseStream.Position - trackSizePosition) - 4;
                writer.BaseStream.Position = trackSizePosition;
                writer.Write(SwapUInt32(trackChunkLength));
                writer.BaseStream.Position += trackChunkLength;
            }
        }
    }
}

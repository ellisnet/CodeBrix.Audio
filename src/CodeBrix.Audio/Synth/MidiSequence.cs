using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Midi.Internal;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// Represents a standard MIDI file.
/// </summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MidiReadMode.Tolerant"/> unless a mode is asked for: a file that breaks the
/// Standard MIDI File specification still loads and plays, and every departure is listed in
/// <see cref="Problems"/>. <see cref="MidiReadMode.Strict"/> throws on the same content instead.
/// </para>
/// <para>
/// What tolerance covers: chunks that are not tracks (skipped), meta events whose payload does not
/// match their declared length (skipped), a track that ends without an end-of-track event (one is
/// supplied), notes that were never released (released at the end of their track), system messages
/// that have no business in a file (consumed correctly rather than eaten as note data), and a track
/// whose bytes stop making sense (the rest of that track is skipped, the rest of the file is read).
/// </para>
/// </remarks>
public sealed partial class MidiSequence
{
    private Message[] messages;
    private TimeSpan[] times;
    private IReadOnlyList<string> problems;
    private IReadOnlyList<MidiTextMeta> textMetas;

    /// <summary>
    /// Loads a MIDI file from the stream, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public MidiSequence(Stream stream)
        : this(stream, 0, MidiSequenceLoopType.None, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the stream.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public MidiSequence(Stream stream, MidiReadMode readMode)
        : this(stream, 0, MidiSequenceLoopType.None, readMode)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the stream, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <param name="loopPoint">The loop start point in ticks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="loopPoint"/> is negative.</exception>
    public MidiSequence(Stream stream, int loopPoint)
        : this(stream, loopPoint, MidiSequenceLoopType.None, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the stream.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <param name="loopPoint">The loop start point in ticks.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="loopPoint"/> is negative.</exception>
    public MidiSequence(Stream stream, int loopPoint, MidiReadMode readMode)
        : this(stream, loopPoint, MidiSequenceLoopType.None, readMode)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the stream, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <param name="loopType">The type of loop extension to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public MidiSequence(Stream stream, MidiSequenceLoopType loopType)
        : this(stream, 0, loopType, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the stream.
    /// </summary>
    /// <param name="stream">The data stream used to load the MIDI file.</param>
    /// <param name="loopType">The type of loop extension to use.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public MidiSequence(Stream stream, MidiSequenceLoopType loopType, MidiReadMode readMode)
        : this(stream, 0, loopType, readMode)
    {
    }

    private MidiSequence(Stream stream, int loopPoint, MidiSequenceLoopType loopType, MidiReadMode readMode)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (loopPoint < 0)
        {
            throw new ArgumentException("The loop point must be a non-negative value.", nameof(loopPoint));
        }

        Load(stream, loopPoint, loopType, readMode);

        // Workaround for nullable warnings in .NET Standard 2.1.
        Debug.Assert(messages != null);
        Debug.Assert(times != null);
    }

    /// <summary>
    /// Loads a MIDI file from the file, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public MidiSequence(string path)
        : this(path, 0, MidiSequenceLoopType.None, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the file.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public MidiSequence(string path, MidiReadMode readMode)
        : this(path, 0, MidiSequenceLoopType.None, readMode)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the file, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <param name="loopPoint">The loop start point in ticks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="loopPoint"/> is negative.</exception>
    public MidiSequence(string path, int loopPoint)
        : this(path, loopPoint, MidiSequenceLoopType.None, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the file.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <param name="loopPoint">The loop start point in ticks.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="loopPoint"/> is negative.</exception>
    public MidiSequence(string path, int loopPoint, MidiReadMode readMode)
        : this(path, loopPoint, MidiSequenceLoopType.None, readMode)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the file, tolerating content the specification does not allow.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <param name="loopType">The type of loop extension to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public MidiSequence(string path, MidiSequenceLoopType loopType)
        : this(path, 0, loopType, MidiReadMode.Tolerant)
    {
    }

    /// <summary>
    /// Loads a MIDI file from the file.
    /// </summary>
    /// <param name="path">The MIDI file name and path.</param>
    /// <param name="loopType">The type of loop extension to use.</param>
    /// <param name="readMode">Whether to tolerate content the specification does not allow, or throw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public MidiSequence(string path, MidiSequenceLoopType loopType, MidiReadMode readMode)
        : this(path, 0, loopType, readMode)
    {
    }

    private MidiSequence(string path, int loopPoint, MidiSequenceLoopType loopType, MidiReadMode readMode)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (loopPoint < 0)
        {
            throw new ArgumentException("The loop point must be a non-negative value.", nameof(loopPoint));
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            Load(stream, loopPoint, loopType, readMode);
        }

        // Workaround for nullable warnings in .NET Standard 2.1.
        Debug.Assert(messages != null);
        Debug.Assert(times != null);
    }

    // Some .NET implementations round TimeSpan to the nearest millisecond,
    // and the timing of MIDI messages will be wrong.
    // This method makes TimeSpan without rounding.
    internal static TimeSpan GetTimeSpanFromSeconds(double value)
    {
        return new TimeSpan((long)(TimeSpan.TicksPerSecond * value));
    }

    private void Load(Stream stream, int loopPoint, MidiSequenceLoopType loopType, MidiReadMode readMode)
    {
        var context = new MidiReadContext(readMode);
        var collectedTextMetas = new List<MidiTextMeta>();
        problems = context.Problems;
        textMetas = collectedTextMetas;

        using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
        {
            var chunkType = reader.ReadFourCC();
            if (chunkType != "MThd")
            {
                throw new InvalidDataException($"The chunk type must be 'MThd', but was '{chunkType}'.");
            }

            var size = reader.ReadInt32BigEndian();
            if (size != 6)
            {
                if (context.IsStrict || size < 6)
                {
                    throw new InvalidDataException($"The MThd chunk has invalid data.");
                }

                context.Add($"The MThd chunk declares {size} bytes instead of 6; the extra bytes were skipped.");
            }

            var format = reader.ReadInt16BigEndian();
            var trackCount = reader.ReadInt16BigEndian();
            var resolution = reader.ReadInt16BigEndian();
            if (size > 6)
            {
                reader.BaseStream.Position += size - 6;
            }

            if (!(format == 0 || format == 1))
            {
                if (context.IsStrict)
                {
                    throw new NotSupportedException($"The format {format} is not supported.");
                }

                // Format 2 holds independent sequences rather than parallel tracks. Merging them on
                // one timeline is not what the file means, but it is the only thing this type can
                // do with them, and it beats refusing to open the file.
                context.Add($"MIDI file format {format} is not supported; its tracks were merged as if it were format 1.");
            }

            if (resolution <= 0)
            {
                // Zero would divide by zero below, and a negative division is the SMPTE time code
                // form, which this type does not implement.
                if (context.IsStrict)
                {
                    throw new InvalidDataException($"The division {resolution} is not supported.");
                }

                context.Add($"The MThd chunk declares an unsupported division of {resolution}; {DefaultResolution} ticks per quarter note were assumed.");
                resolution = DefaultResolution;
            }

            if (trackCount < 0)
            {
                throw new InvalidDataException($"The MThd chunk declares {trackCount} tracks.");
            }

            var messageLists = new List<Message>[trackCount];
            var tickLists = new List<int>[trackCount];
            for (var i = 0; i < trackCount; i++)
            {
                (messageLists[i], tickLists[i]) = ReadTrack(reader, loopType, context, i, collectedTextMetas);
            }

            if (loopPoint != 0 && trackCount > 0)
            {
                var tickList = tickLists[0];
                var messageList = messageLists[0];
                if (tickList.Count > 0 && loopPoint <= tickList.Last())
                {
                    for (var i = 0; i < tickList.Count; i++)
                    {
                        if (tickList[i] >= loopPoint)
                        {
                            tickList.Insert(i, loopPoint);
                            messageList.Insert(i, Message.LoopStart());
                            break;
                        }
                    }
                }
                else
                {
                    tickList.Add(loopPoint);
                    messageList.Add(Message.LoopStart());
                }
            }

            (messages, times, tempoMap) = MergeTracks(messageLists, tickLists, resolution);
        }
    }

    private const int DefaultResolution = 480;

    private static (List<Message>, List<int>) ReadTrack(BinaryReader reader, MidiSequenceLoopType loopType,
        MidiReadContext context, int trackNumber, List<MidiTextMeta> textMetas)
    {
        var strict = context.IsStrict;
        var chunkType = reader.ReadFourCC();
        while (chunkType != "MTrk")
        {
            if (strict)
            {
                throw new InvalidDataException($"The chunk type must be 'MTrk', but was '{chunkType}'.");
            }

            if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
            {
                context.Add($"The file ends before track {trackNumber} begins; the track was read as empty.");
                return EmptyTrack();
            }

            // The specification says an unrecognised chunk type is to be skipped, not refused.
            var skip = reader.ReadInt32BigEndian();
            context.Add($"Chunk '{chunkType}' is not a track chunk; its {skip} byte(s) were skipped.");
            var skipTo = reader.BaseStream.Position + skip;
            if (skip < 0 || skipTo > reader.BaseStream.Length)
            {
                context.Add($"The file ends before track {trackNumber} begins; the track was read as empty.");
                return EmptyTrack();
            }

            reader.BaseStream.Position = skipTo;
            chunkType = reader.ReadFourCC();
        }

        var end = (long)reader.ReadInt32BigEndian();
        end += reader.BaseStream.Position;
        if (!strict && end > reader.BaseStream.Length)
        {
            context.Add($"Track {trackNumber} declares more bytes than the file holds; it was read to the end of the file.");
            end = reader.BaseStream.Length;
        }

        var messages = new List<Message>();
        var ticks = new List<int>();

        // Note-on counts per channel and key, so that anything still sounding when the track ends
        // can be released rather than left hanging for the whole of playback.
        var sounding = new Dictionary<int, int>();

        int tick = 0;
        byte lastStatus = 0;
        var sawEndOfTrack = false;

        while (reader.BaseStream.Position < end)
        {
            int delta;
            byte first;
            try
            {
                delta = reader.ReadIntVariableLength();
                first = reader.ReadByte();
            }
            catch (Exception exception) when (!strict && IsRecoverableReadFailure(exception))
            {
                context.Add($"Track {trackNumber}: the track ends mid-event ({exception.Message}); the rest of it was skipped.");
                break;
            }

            try
            {
                tick = checked(tick + delta);
            }
            catch (OverflowException)
            {
                throw new NotSupportedException("Long MIDI file is not supported.");
            }

            try
            {
                if ((first & 128) == 0)
                {
                    if (lastStatus == 0)
                    {
                        if (strict)
                        {
                            throw new InvalidDataException("A running-status byte appeared before any status byte.");
                        }

                        context.Add($"Track {trackNumber}: a running-status byte appeared before any status byte; the rest of the track was skipped.");
                        break;
                    }

                    var runningCommand = lastStatus & 0xF0;
                    if (runningCommand == 0xC0 || runningCommand == 0xD0)
                    {
                        messages.Add(Message.Common(lastStatus, first));
                        ticks.Add(tick);
                    }
                    else
                    {
                        var data2 = reader.ReadByte();
                        TrackSounding(sounding, lastStatus, first, data2);
                        messages.Add(Message.Common(lastStatus, first, data2, loopType));
                        ticks.Add(tick);
                    }

                    continue;
                }

                switch (first)
                {
                    case 0xF0: // System Exclusive
                    case 0xF7: // System Exclusive
                        DiscardData(reader);
                        break;

                    case 0xFF: // Meta Event
                        var metaType = reader.ReadByte();
                        var metaLength = reader.ReadIntVariableLength();
                        var metaStart = reader.BaseStream.Position;
                        switch (metaType)
                        {
                            case 0x2F: // End of Track
                                sawEndOfTrack = true;
                                break;

                            case 0x51: // Tempo
                                if (metaLength != 3)
                                {
                                    if (strict)
                                    {
                                        throw new InvalidDataException("Failed to read the tempo value.");
                                    }

                                    context.Add($"Track {trackNumber}: a tempo event declares {metaLength} byte(s) instead of 3; it was skipped.");
                                    break;
                                }

                                messages.Add(Message.TempoChange(ReadTempoValue(reader)));
                                ticks.Add(tick);
                                break;

                            default:
                                if (IsTextMeta(metaType))
                                {
                                    textMetas.Add(new MidiTextMeta((MetaEventType)metaType, trackNumber, tick,
                                        reader.ReadBytes(metaLength)));
                                }
                                break;
                        }

                        reader.BaseStream.Position = metaStart + metaLength;
                        if (sawEndOfTrack)
                        {
                            // Some MIDI files may have events inserted after the EOT.
                            // Such events should be ignored.
                            if (reader.BaseStream.Position < end)
                            {
                                reader.BaseStream.Position = end;
                            }
                        }
                        break;

                    case 0xF1: // MIDI time code quarter frame
                    case 0xF3: // Song select
                        context.Add($"Track {trackNumber}: system message 0x{first:X2} has no meaning in a file; it was skipped.");
                        reader.ReadByte();
                        break;

                    case 0xF2: // Song position pointer
                        context.Add($"Track {trackNumber}: system message 0x{first:X2} has no meaning in a file; it was skipped.");
                        reader.ReadBytes(2);
                        break;

                    case 0xF4:
                    case 0xF5:
                    case 0xF6:
                    case 0xF8:
                    case 0xF9:
                    case 0xFA:
                    case 0xFB:
                    case 0xFC:
                    case 0xFD:
                    case 0xFE:
                        // System common and real-time messages carry no data bytes. Treating them
                        // as channel messages, as a naive reader does, eats the two bytes that
                        // follow and derails the rest of the track.
                        context.Add($"Track {trackNumber}: system message 0x{first:X2} has no meaning in a file; it was skipped.");
                        break;

                    default:
                        var command = first & 0xF0;
                        if (command == 0xC0 || command == 0xD0)
                        {
                            var data1 = reader.ReadByte();
                            messages.Add(Message.Common(first, data1));
                            ticks.Add(tick);
                        }
                        else
                        {
                            var data1 = reader.ReadByte();
                            var data2 = reader.ReadByte();
                            TrackSounding(sounding, first, data1, data2);
                            messages.Add(Message.Common(first, data1, data2, loopType));
                            ticks.Add(tick);
                        }
                        break;
                }
            }
            catch (Exception exception) when (!strict && IsRecoverableReadFailure(exception))
            {
                context.Add($"Track {trackNumber}: reading stopped at tick {tick} ({exception.Message}); the rest of the track was skipped.");
                break;
            }

            if (first < 0xF0)
            {
                // Only channel-voice messages establish running status. Letting a meta or sysex
                // event overwrite the anchor makes the next high-bit-clear byte parse as noise -
                // the same defect that was fixed in the editable model's reader.
                lastStatus = first;
            }

            if (sawEndOfTrack)
            {
                break;
            }
        }

        if (!sawEndOfTrack)
        {
            if (strict)
            {
                throw new InvalidDataException("The track chunk ends without an end-of-track event.");
            }

            context.Add($"Track {trackNumber} ends without an end-of-track event; one was supplied.");
        }

        CloseSoundingNotes(sounding, messages, ticks, tick, context, trackNumber);

        messages.Add(Message.EndOfTrack());
        ticks.Add(tick);

        if (reader.BaseStream.Position != end)
        {
            reader.BaseStream.Position = end;
        }

        return (messages, ticks);
    }

    private static (List<Message>, List<int>) EmptyTrack()
    {
        return ([Message.EndOfTrack()], [0]);
    }

    private static bool IsRecoverableReadFailure(Exception exception) =>
        exception is InvalidDataException
        || exception is EndOfStreamException
        || exception is IOException
        || exception is ArgumentException;

    private static bool IsTextMeta(byte metaType) => metaType >= 0x01 && metaType <= 0x09;

    private static void TrackSounding(Dictionary<int, int> sounding, byte status, byte data1, byte data2)
    {
        var command = status & 0xF0;
        if (command != 0x80 && command != 0x90)
        {
            return;
        }

        var key = ((status & 0x0F) << 8) | data1;
        if (command == 0x90 && data2 > 0)
        {
            sounding.TryGetValue(key, out var count);
            sounding[key] = count + 1;
            return;
        }

        if (sounding.TryGetValue(key, out var sounded) && sounded > 0)
        {
            if (sounded == 1)
            {
                sounding.Remove(key);
            }
            else
            {
                sounding[key] = sounded - 1;
            }
        }
    }

    private static void CloseSoundingNotes(Dictionary<int, int> sounding, List<Message> messages, List<int> ticks,
        int tick, MidiReadContext context, int trackNumber)
    {
        if (sounding.Count == 0)
        {
            return;
        }

        var closed = 0;
        foreach (var pair in sounding)
        {
            var channel = (byte)(pair.Key >> 8);
            var note = (byte)(pair.Key & 0xFF);
            for (var i = 0; i < pair.Value; i++)
            {
                messages.Add(Message.Common((byte)(0x80 | channel), note, 64, MidiSequenceLoopType.None));
                ticks.Add(tick);
                closed++;
            }
        }

        sounding.Clear();
        context.Add($"Track {trackNumber}: {closed} note(s) were still sounding at the end of the track; each was released there.");
    }

    //was previously: returned only (Message[], TimeSpan[]); the tempo map it applies is now
    //recorded and returned as well - see MidiSequenceTempoMap.cs for why.
    private static (Message[], TimeSpan[], MidiTempoMap) MergeTracks(List<Message>[] messageLists, List<int>[] tickLists, int resolution)
    {
        var mergedMessages = new List<Message>();
        var mergedTimes = new List<TimeSpan>();
        var tempoChanges = new List<MidiTempoChange>();

        var indices = new int[messageLists.Length];

        var currentTick = 0;
        var currentTime = TimeSpan.Zero;

        var tempo = 120.0;

        while (true)
        {
            var minTick = int.MaxValue;
            var minIndex = -1;
            for (var ch = 0; ch < tickLists.Length; ch++)
            {
                if (indices[ch] < tickLists[ch].Count)
                {
                    var tick = tickLists[ch][indices[ch]];
                    if (tick < minTick)
                    {
                        minTick = tick;
                        minIndex = ch;
                    }
                }
            }

            if (minIndex == -1)
            {
                break;
            }

            var nextTick = tickLists[minIndex][indices[minIndex]];
            var deltaTick = nextTick - currentTick;
            var deltaTime = GetTimeSpanFromSeconds(60.0 / (resolution * tempo) * deltaTick);

            currentTick += deltaTick;
            currentTime += deltaTime;

            var message = messageLists[minIndex][indices[minIndex]];
            if (message.Type == MessageType.TempoChange)
            {
                tempo = message.Tempo;
                tempoChanges.Add(new MidiTempoChange(currentTime, (double)currentTick / resolution, tempo));
            }
            else
            {
                mergedMessages.Add(message);
                mergedTimes.Add(currentTime);
            }

            indices[minIndex]++;
        }

        return (mergedMessages.ToArray(), mergedTimes.ToArray(), new MidiTempoMap(tempoChanges));
    }

    private static int ReadTempoValue(BinaryReader reader)
    {
        var b1 = reader.ReadByte();
        var b2 = reader.ReadByte();
        var b3 = reader.ReadByte();
        return (b1 << 16) | (b2 << 8) | b3;
    }

    private static void DiscardData(BinaryReader reader)
    {
        var size = reader.ReadIntVariableLength();
        reader.BaseStream.Position += size;
    }

    /// <summary>
    /// The length of the MIDI file.
    /// </summary>
    public TimeSpan Length => times.Length > 0 ? times[times.Length - 1] : TimeSpan.Zero;

    /// <summary>
    /// Everything in the file that could not be honoured as written, one human-readable line each:
    /// chunks that were skipped, meta events that could not be decoded, notes that were never
    /// released, bytes that made no sense. Empty for a file that follows the specification. Never
    /// thrown; reading with <see cref="MidiReadMode.Strict"/> throws instead of filling this.
    /// </summary>
    /// <remarks>The list is capped, so a thoroughly corrupt file cannot grow it without bound; the
    /// last entry then says that further problems were not recorded.</remarks>
    public IReadOnlyList<string> Problems => problems;

    /// <summary>
    /// The textual meta events the file carried - track names, lyrics, markers and the like - with
    /// their bytes preserved. A sequence does not play these; they are here so that a caller can
    /// identify a file without reading it a second time.
    /// </summary>
    public IReadOnlyList<MidiTextMeta> TextMetas => textMetas;

    internal Message[] Messages => messages;
    internal TimeSpan[] Times => times;



    internal struct Message
    {
        private byte channel;
        private byte command;
        private byte data1;
        private byte data2;

        private Message(byte channel, byte command, byte data1, byte data2)
        {
            this.channel = channel;
            this.command = command;
            this.data1 = data1;
            this.data2 = data2;
        }

        public static Message Common(byte status, byte data1)
        {
            byte channel = (byte)(status & 0x0F);
            byte command = (byte)(status & 0xF0);
            byte data2 = 0;
            return new Message(channel, command, data1, data2);
        }

        public static Message Common(byte status, byte data1, byte data2, MidiSequenceLoopType loopType)
        {
            byte channel = (byte)(status & 0x0F);
            byte command = (byte)(status & 0xF0);

            if (command == 0xB0)
            {
                switch (loopType)
                {
                    case MidiSequenceLoopType.RpgMaker:
                        if (data1 == 111)
                        {
                            return LoopStart();
                        }
                        break;

                    case MidiSequenceLoopType.IncredibleMachine:
                        if (data1 == 110)
                        {
                            return LoopStart();
                        }
                        if (data1 == 111)
                        {
                            return LoopEnd();
                        }
                        break;

                    case MidiSequenceLoopType.FinalFantasy:
                        if (data1 == 116)
                        {
                            return LoopStart();
                        }
                        if (data1 == 117)
                        {
                            return LoopEnd();
                        }
                        break;
                }
            }

            return new Message(channel, command, data1, data2);
        }

        public static Message TempoChange(int tempo)
        {
            byte command = (byte)(tempo >> 16);
            byte data1 = (byte)(tempo >> 8);
            byte data2 = (byte)(tempo);
            return new Message((int)MessageType.TempoChange, command, data1, data2);
        }

        public static Message LoopStart()
        {
            return new Message((int)MessageType.LoopStart, 0, 0, 0);
        }

        public static Message LoopEnd()
        {
            return new Message((int)MessageType.LoopEnd, 0, 0, 0);
        }

        public static Message EndOfTrack()
        {
            return new Message((int)MessageType.EndOfTrack, 0, 0, 0);
        }

        public override string ToString()
        {
            switch (channel)
            {
                case (int)MessageType.TempoChange:
                    return "Tempo: " + Tempo;

                case (int)MessageType.LoopStart:
                    return "LoopStart";

                case (int)MessageType.LoopEnd:
                    return "LoopEnd";

                case (int)MessageType.EndOfTrack:
                    return "EndOfTrack";

                default:
                    return "CH" + channel + ": " + command.ToString("X2") + ", " + data1.ToString("X2") + ", " + data2.ToString("X2");
            }
        }

        public MessageType Type
        {
            get
            {
                switch (channel)
                {
                    case (int)MessageType.TempoChange:
                        return MessageType.TempoChange;

                    case (int)MessageType.LoopStart:
                        return MessageType.LoopStart;

                    case (int)MessageType.LoopEnd:
                        return MessageType.LoopEnd;

                    case (int)MessageType.EndOfTrack:
                        return MessageType.EndOfTrack;

                    default:
                        return MessageType.Normal;
                }
            }
        }

        public byte Channel => channel;
        public byte Command => command;
        public byte Data1 => data1;
        public byte Data2 => data2;

        public double Tempo => 60000000.0 / ((command << 16) | (data1 << 8) | data2);
    }



    internal enum MessageType
    {
        Normal = 0,
        TempoChange = 252,
        LoopStart = 253,
        LoopEnd = 254,
        EndOfTrack = 255
    }
}

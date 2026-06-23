using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Midi;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Midi;
public class MidiFileTests
{
    [Fact]
    public void ConstructorRejectsMissingHeaderChunk()
    {
        var bytes = Encoding.ASCII.GetBytes("NOPE");
        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<FormatException>(() => new MidiFile(stream, true));
        }
    }

    [Fact]
    public void ConstructorRejectsUnexpectedHeaderChunkLength()
    {
        var bytes = new byte[]
        {
            (byte)'M', (byte)'T', (byte)'h', (byte)'d',
            0x00, 0x00, 0x00, 0x05,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x60
        };

        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<FormatException>(() => new MidiFile(stream, true));
        }
    }

    [Fact]
    public void ReadsType0FileAndPopulatesBasicMetadata()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x0A, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(0, 480, track);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(0, midiFile.FileFormat);
            Assert.Equal(480, midiFile.DeltaTicksPerQuarterNote);
            Assert.Equal(1, midiFile.Tracks);
            Assert.Equal(3, midiFile.Events[0].Count);
            Assert.Equal(0, midiFile.Events[0][0].AbsoluteTime);
            Assert.Equal(10, midiFile.Events[0][1].AbsoluteTime);
            Assert.True(MidiEvent.IsEndTrack(midiFile.Events[0][2]));
        }
    }

    [Fact]
    public void ReadsRiffRmidWrappedFile()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x0A, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var midi = CreateMidiFileBytes(0, 480, track);
        var rmid = WrapInRiffRmid(midi);

        using (var stream = new MemoryStream(rmid))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(0, midiFile.FileFormat);
            Assert.Equal(480, midiFile.DeltaTicksPerQuarterNote);
            Assert.Equal(1, midiFile.Tracks);
            Assert.Equal(3, midiFile.Events[0].Count);
        }
    }

    [Fact]
    public void ReadsRiffRmidWithOtherChunksBeforeData()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x0A, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var midi = CreateMidiFileBytes(1, 120, track);
        // An odd-length leading chunk exercises RIFF word-alignment padding.
        var infoChunk = ("IART", Encoding.ASCII.GetBytes("abc"));
        var rmid = WrapInRiffRmid(midi, infoChunk);

        using (var stream = new MemoryStream(rmid))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(1, midiFile.FileFormat);
            Assert.Equal(120, midiFile.DeltaTicksPerQuarterNote);
            Assert.Equal(3, midiFile.Events[0].Count);
        }
    }

    [Fact]
    public void RejectsNonRmidRiffFile()
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(4u);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Flush();
            }
            stream.Position = 0;
            Assert.Throws<FormatException>(() => new MidiFile(stream, true));
        }
    }

    [Fact]
    public void StrictCheckingRejectsUnmatchedNoteOn()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(0, 120, track);

        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<FormatException>(() => new MidiFile(stream, true));
        }
    }

    [Fact]
    public void NonStrictCheckingAllowsUnmatchedNoteOn()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(0, 120, track);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, false);

            Assert.Equal(2, midiFile.Events[0].Count);
            Assert.IsType<NoteOnEvent>(midiFile.Events[0][0]);
            var noteOn = (NoteOnEvent)midiFile.Events[0][0];
            Assert.Null(noteOn.OffEvent);
        }
    }

    [Fact]
    public void StrictCheckingRejectsEventsAfterEndTrack()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x00, 0xFF, 0x2F, 0x00,
            0x00, 0x80, 0x3C, 0x40
        };
        var bytes = CreateMidiFileBytes(0, 120, track);

        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<FormatException>(() => new MidiFile(stream, true));
        }
    }

    [Fact]
    public void NonStrictCheckingAllowsEventsAfterEndTrack()
    {
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,
            0x00, 0xFF, 0x2F, 0x00,
            0x00, 0x80, 0x3C, 0x40
        };
        var bytes = CreateMidiFileBytes(0, 120, track);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, false);
            Assert.Equal(3, midiFile.Events[0].Count);
        }
    }

    [Fact]
    public void RunningStatusSurvivesAcrossMetaEvents()
    {
        // Issue #205: a meta event embedded between channel-voice messages must not
        // clobber running status, otherwise the next high-bit-clear byte gets reparsed
        // as a meta event type.
        var track = new byte[]
        {
            0x00, 0x90, 0x3C, 0x64,             // NoteOn ch1 note 0x3C vel 100 (sets running status)
            0x00, 0xFF, 0x01, 0x01, (byte)'x',  // Text meta event "x"
            0x00, 0x40, 0x64,                   // running-status NoteOn ch1 note 0x40 vel 100
            0x00, 0x3C, 0x00,                   // running-status NoteOn vel 0 (note off for 0x3C)
            0x00, 0x40, 0x00,                   // running-status NoteOn vel 0 (note off for 0x40)
            0x00, 0xFF, 0x2F, 0x00              // EndTrack
        };
        var bytes = CreateMidiFileBytes(0, 480, track);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(6, midiFile.Events[0].Count);
            Assert.IsType<NoteOnEvent>(midiFile.Events[0][0]);
            Assert.IsType<TextEvent>(midiFile.Events[0][1]);
            Assert.IsType<NoteOnEvent>(midiFile.Events[0][2]);

            var runningNoteOn = (NoteEvent)midiFile.Events[0][2];
            Assert.Equal(0x40, runningNoteOn.NoteNumber);
            Assert.Equal(0x64, runningNoteOn.Velocity);
            Assert.Equal(1, runningNoteOn.Channel);
        }
    }

    [Fact]
    public void Type1TracksUseIndependentAbsoluteTimeBases()
    {
        var track1 = new byte[]
        {
            0x05, 0x90, 0x3C, 0x64,
            0x05, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var track2 = new byte[]
        {
            0x05, 0x91, 0x40, 0x64,
            0x05, 0x81, 0x40, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(1, 120, track1, track2);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(5, midiFile.Events[0][0].AbsoluteTime);
            Assert.Equal(5, midiFile.Events[1][0].AbsoluteTime);
        }
    }

    [Fact]
    public void Type2TracksUseIndependentAbsoluteTimeBases()
    {
        var track1 = new byte[]
        {
            0x05, 0x90, 0x3C, 0x64,
            0x05, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var track2 = new byte[]
        {
            0x05, 0x91, 0x40, 0x64,
            0x05, 0x81, 0x40, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(2, 120, track1, track2);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, true);

            Assert.Equal(5, midiFile.Events[0][0].AbsoluteTime);
            Assert.Equal(5, midiFile.Events[1][0].AbsoluteTime);
        }
    }

    [Fact]
    public void Type2FileTypeIsPreservedInEventCollection()
    {
        var track = new byte[]
        {
            0x00, 0xFF, 0x2F, 0x00
        };
        var bytes = CreateMidiFileBytes(2, 120, track);

        using (var stream = new MemoryStream(bytes))
        {
            var midiFile = new MidiFile(stream, true);
            Assert.Equal(2, midiFile.Events.MidiFileType);
        }
    }

    [Fact]
    public void ExportRejectsType0CollectionWithMoreThanOneTrack()
    {
        var events = new MidiEventCollection(0, 120);
        events.AddTrack();
        events.AddTrack();

        var fileName = Path.Combine(Path.GetTempPath(), $"naudio-midifiletests-{Guid.NewGuid():N}.mid");
        try
        {
            Assert.Throws<ArgumentException>(() => MidiFile.Export(fileName, events));
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    [Fact]
    public void ExportRoundTripsType1File()
    {
        var events = new MidiEventCollection(1, 480);
        events.AddTrack();
        events.AddTrack();

        events[0].Add(new TextEvent("Conductor", MetaEventType.SequenceTrackName, 0));
        events[0].Add(new MetaEvent(MetaEventType.EndTrack, 0, 0));

        events[1].Add(new NoteOnEvent(0, 1, 60, 100, 10));
        events[1].Add(new NoteEvent(10, 1, MidiCommandCode.NoteOff, 60, 64));
        events[1].Add(new MetaEvent(MetaEventType.EndTrack, 0, 11));

        var fileName = Path.Combine(Path.GetTempPath(), $"naudio-midifiletests-{Guid.NewGuid():N}.mid");
        try
        {
            MidiFile.Export(fileName, events);
            var midiFile = new MidiFile(fileName, true);

            Assert.Equal(1, midiFile.FileFormat);
            Assert.Equal(480, midiFile.DeltaTicksPerQuarterNote);
            Assert.Equal(2, midiFile.Tracks);
            Assert.Contains(midiFile.Events[0], MidiEvent.IsEndTrack);
            Assert.Contains(midiFile.Events[1], MidiEvent.IsEndTrack);
            Assert.Single(midiFile.Events[1].OfType<NoteOnEvent>());
            Assert.Equal(1, midiFile.Events[1].Count(e => e.CommandCode == MidiCommandCode.NoteOff));
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    private static byte[] CreateMidiFileBytes(ushort format, ushort division, params byte[][] tracks)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("MThd"));
            WriteUInt32BigEndian(writer, 6);
            WriteUInt16BigEndian(writer, format);
            WriteUInt16BigEndian(writer, (ushort)tracks.Length);
            WriteUInt16BigEndian(writer, division);

            foreach (var track in tracks)
            {
                writer.Write(Encoding.ASCII.GetBytes("MTrk"));
                WriteUInt32BigEndian(writer, (uint)track.Length);
                writer.Write(track);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }

    private static byte[] WrapInRiffRmid(byte[] midi, params (string id, byte[] data)[] extraChunks)
    {
        using (var body = new MemoryStream())
        {
            void WriteChunk(string id, byte[] data)
            {
                body.Write(Encoding.ASCII.GetBytes(id), 0, 4);
                body.Write(BitConverter.GetBytes((uint)data.Length), 0, 4); // RIFF sizes are little-endian
                body.Write(data, 0, data.Length);
                if ((data.Length & 1) == 1)
                {
                    body.WriteByte(0); // word-alignment pad byte
                }
            }

            body.Write(Encoding.ASCII.GetBytes("RMID"), 0, 4);
            foreach (var (id, data) in extraChunks)
            {
                WriteChunk(id, data);
            }
            WriteChunk("data", midi);

            var bodyBytes = body.ToArray();
            using (var stream = new MemoryStream())
            {
                stream.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
                stream.Write(BitConverter.GetBytes((uint)bodyBytes.Length), 0, 4);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                return stream.ToArray();
            }
        }
    }

    private static void WriteUInt16BigEndian(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}

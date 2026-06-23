using System;
using System.IO;
using CodeBrix.Audio.Midi;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Midi;
public class MidiEventTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(0x3FFF)]
    [InlineData(0x4000)]
    [InlineData(0x1FFFFF)]
    [InlineData(0x200000)]
    [InlineData(0x0FFFFFFF)]
    public void VarIntRoundTrip(int value)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            MidiEvent.WriteVarInt(writer, value);
            ms.Position = 0;
            using (var reader = new BinaryReader(ms))
            {
                Assert.Equal(value, MidiEvent.ReadVarInt(reader));
            }
        }
    }

    [Fact]
    public void WriteVarIntRejectsNegativeValues()
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiEvent.WriteVarInt(writer, -1));
        }
    }

    [Fact]
    public void WriteVarIntRejectsTooLargeValues()
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiEvent.WriteVarInt(writer, 0x10000000));
        }
    }

    [Fact]
    public void ReadVarIntRejectsInvalidEncoding()
    {
        using (var ms = new MemoryStream(new byte[] { 0x80, 0x80, 0x80, 0x80 }))
        using (var reader = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => MidiEvent.ReadVarInt(reader));
        }
    }

    [Fact]
    public void ConstructorSetsProperties()
    {
        var midiEvent = new MidiEvent(123, 2, MidiCommandCode.NoteOff);

        Assert.Equal(123, midiEvent.AbsoluteTime);
        Assert.Equal(2, midiEvent.Channel);
        Assert.Equal(MidiCommandCode.NoteOff, midiEvent.CommandCode);
        Assert.Equal(0, midiEvent.DeltaTime);
    }

    [Fact]
    public void ChannelRejectsValuesOutOfRange()
    {
        var midiEvent = new MidiEvent(0, 1, MidiCommandCode.NoteOff);

        Assert.Throws<ArgumentOutOfRangeException>(() => midiEvent.Channel = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => midiEvent.Channel = 17);
    }

    [Fact]
    public void BaseGetAsShortMessageUsesCommandAndChannel()
    {
        var midiEvent = new MidiEvent(0, 3, MidiCommandCode.ControlChange);
        Assert.Equal(0xB2, midiEvent.GetAsShortMessage());
    }

    [Fact]
    public void IsNoteOnAndIsNoteOffHandleVelocityConvention()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);
        var noteOnWithZeroVelocity = new NoteOnEvent(0, 1, 60, 0, 10);
        var noteOff = new NoteEvent(0, 1, MidiCommandCode.NoteOff, 60, 64);

        Assert.True(MidiEvent.IsNoteOn(noteOn));
        Assert.False(MidiEvent.IsNoteOff(noteOn));

        Assert.False(MidiEvent.IsNoteOn(noteOnWithZeroVelocity));
        Assert.True(MidiEvent.IsNoteOff(noteOnWithZeroVelocity));

        Assert.True(MidiEvent.IsNoteOff(noteOff));
        Assert.False(MidiEvent.IsNoteOn(noteOff));
    }

    [Fact]
    public void IsNoteHelpersReturnFalseForNull()
    {
        Assert.False(MidiEvent.IsNoteOn(null));
        Assert.False(MidiEvent.IsNoteOff(null));
    }

    [Fact]
    public void IsEndTrackReturnsTrueOnlyForEndTrackMetaEvent()
    {
        var endTrack = new MetaEvent(MetaEventType.EndTrack, 0, 0);
        var otherMeta = new MetaEvent(MetaEventType.TextEvent, 0, 0);
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);

        Assert.True(MidiEvent.IsEndTrack(endTrack));
        Assert.False(MidiEvent.IsEndTrack(otherMeta));
        Assert.False(MidiEvent.IsEndTrack(noteOn));
        Assert.False(MidiEvent.IsEndTrack(null));
    }

    [Fact]
    public void FromRawMessageParsesNoteOn()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0x92, 60, 100));

        Assert.IsType<NoteOnEvent>(midiEvent);
        var noteOn = (NoteOnEvent)midiEvent;
        Assert.Equal(3, noteOn.Channel);
        Assert.Equal(60, noteOn.NoteNumber);
        Assert.Equal(100, noteOn.Velocity);
    }

    [Fact]
    public void FromRawMessageConvertsZeroVelocityNoteOnToNoteEvent()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0x91, 60, 0));

        Assert.IsType<NoteEvent>(midiEvent);
        Assert.IsNotType<NoteOnEvent>(midiEvent);
        Assert.Equal(MidiCommandCode.NoteOn, midiEvent.CommandCode);
        Assert.Equal(0, ((NoteEvent)midiEvent).Velocity);
    }

    [Fact]
    public void FromRawMessageParsesControlChange()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0xB4, (int)MidiController.Expression, 127));

        Assert.IsType<ControlChangeEvent>(midiEvent);
        var control = (ControlChangeEvent)midiEvent;
        Assert.Equal(5, control.Channel);
        Assert.Equal(MidiController.Expression, control.Controller);
        Assert.Equal(127, control.ControllerValue);
    }

    [Fact]
    public void FromRawMessageParsesPatchChange()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0xC0, 10, 0));

        Assert.IsType<PatchChangeEvent>(midiEvent);
        var patch = (PatchChangeEvent)midiEvent;
        Assert.Equal(1, patch.Channel);
        Assert.Equal(10, patch.Patch);
    }

    [Fact]
    public void FromRawMessageParsesChannelAfterTouch()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0xD5, 12, 0));

        Assert.IsType<ChannelAfterTouchEvent>(midiEvent);
        var afterTouch = (ChannelAfterTouchEvent)midiEvent;
        Assert.Equal(6, afterTouch.Channel);
        Assert.Equal(12, afterTouch.AfterTouchPressure);
    }

    [Fact]
    public void FromRawMessageParsesPitchWheelChange()
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage(0xE1, 0x7D, 0x40));

        Assert.IsType<PitchWheelChangeEvent>(midiEvent);
        var pitch = (PitchWheelChangeEvent)midiEvent;
        Assert.Equal(2, pitch.Channel);
        Assert.Equal(0x207D, pitch.Pitch);
    }

    [Theory]
    [InlineData(MidiCommandCode.TimingClock)]
    [InlineData(MidiCommandCode.StartSequence)]
    [InlineData(MidiCommandCode.ContinueSequence)]
    [InlineData(MidiCommandCode.StopSequence)]
    [InlineData(MidiCommandCode.AutoSensing)]
    public void FromRawMessageParsesSystemRealtimeMessages(MidiCommandCode commandCode)
    {
        var midiEvent = MidiEvent.FromRawMessage(CreateRawMessage((int)commandCode, 0, 0));

        Assert.IsType<MidiEvent>(midiEvent);
        Assert.Equal(commandCode, midiEvent.CommandCode);
        Assert.Equal(1, midiEvent.Channel);
    }

    [Theory]
    [InlineData(MidiCommandCode.MetaEvent)]
    [InlineData(MidiCommandCode.Sysex)]
    [InlineData(MidiCommandCode.Eox)]
    public void FromRawMessageRejectsUnsupportedSystemMessages(MidiCommandCode commandCode)
    {
        Assert.Throws<FormatException>(() => MidiEvent.FromRawMessage(CreateRawMessage((int)commandCode, 0, 0)));
    }

    [Fact]
    public void ReadNextEventParsesStatusByteEvent()
    {
        var bytes = new byte[] { 0x00, 0x91, 0x3C, 0x64 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<NoteOnEvent>(midiEvent);
            var noteOn = (NoteOnEvent)midiEvent;
            Assert.Equal(0, noteOn.DeltaTime);
            Assert.Equal(2, noteOn.Channel);
            Assert.Equal(60, noteOn.NoteNumber);
            Assert.Equal(100, noteOn.Velocity);
        }
    }

    [Fact]
    public void ReadNextEventParsesRunningStatusEvent()
    {
        var previous = new NoteOnEvent(0, 2, 1, 1, 0);
        var bytes = new byte[] { 0x00, 0x3D, 0x40 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, previous);

            Assert.IsType<NoteOnEvent>(midiEvent);
            var noteOn = (NoteOnEvent)midiEvent;
            Assert.Equal(2, noteOn.Channel);
            Assert.Equal(61, noteOn.NoteNumber);
            Assert.Equal(64, noteOn.Velocity);
            Assert.Equal(MidiCommandCode.NoteOn, noteOn.CommandCode);
        }
    }

    [Fact]
    public void ReadNextEventWithRunningStatusAndNoPreviousThrows()
    {
        var bytes = new byte[] { 0x00, 0x3C, 0x40 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<NullReferenceException>(() => MidiEvent.ReadNextEvent(br, null));
        }
    }

    [Fact]
    public void ReadNextEventParsesMetaEvent()
    {
        var bytes = new byte[] { 0x00, 0xFF, (byte)MetaEventType.EndTrack, 0x00 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<MetaEvent>(midiEvent);
            Assert.True(MidiEvent.IsEndTrack(midiEvent));
            Assert.Equal(MidiCommandCode.MetaEvent, midiEvent.CommandCode);
        }
    }

    [Fact]
    public void ReadNextEventParsesSysexEvent()
    {
        var bytes = new byte[] { 0x00, 0xF0, 0x01, 0x02, 0xF7 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<SysexEvent>(midiEvent);
            Assert.Equal(MidiCommandCode.Sysex, midiEvent.CommandCode);
        }
    }

    [Fact]
    public void ReadNextEventRejectsUnsupportedCommandCode()
    {
        var bytes = new byte[] { 0x00, 0xF1 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => MidiEvent.ReadNextEvent(br, null));
        }
    }

    [Fact]
    public void ExportWritesDeltaAndStatusByte()
    {
        var midiEvent = new MidiEvent(240, 2, MidiCommandCode.NoteOn);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long currentAbsolute = 120;
            midiEvent.Export(ref currentAbsolute, writer);

            Assert.Equal(240, currentAbsolute);
            var bytes = ms.ToArray();
            Assert.Equal(new byte[] { 0x78, 0x91 }, bytes);
        }
    }

    [Fact]
    public void ExportRejectsUnsortedEvents()
    {
        var midiEvent = new MidiEvent(10, 1, MidiCommandCode.NoteOn);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long currentAbsolute = 11;
            Assert.Throws<FormatException>(() => midiEvent.Export(ref currentAbsolute, writer));
        }
    }

    [Fact]
    public void CloneCopiesValueProperties()
    {
        var source = new MidiEvent(50, 4, MidiCommandCode.StopSequence);
        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.Equal(source.AbsoluteTime, clone.AbsoluteTime);
        Assert.Equal(source.Channel, clone.Channel);
        Assert.Equal(source.CommandCode, clone.CommandCode);
        Assert.Equal(source.DeltaTime, clone.DeltaTime);
    }

    [Fact]
    public void ToStringIncludesChannelForChannelMessages()
    {
        var midiEvent = new MidiEvent(12, 3, MidiCommandCode.NoteOff);
        Assert.Equal("12 NoteOff Ch: 3", midiEvent.ToString());
    }

    [Fact]
    public void ToStringOmitsChannelForSystemMessages()
    {
        var midiEvent = new MidiEvent(12, 1, MidiCommandCode.Sysex);
        Assert.Equal("12 Sysex", midiEvent.ToString());
    }

    private static int CreateRawMessage(int status, int data1, int data2)
    {
        return (status & 0xFF) | ((data1 & 0xFF) << 8) | ((data2 & 0xFF) << 16);
    }
}

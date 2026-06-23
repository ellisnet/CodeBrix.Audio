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
public class TimeSignatureEventTests
{
    [Fact]
    public void ConstructorSetsProperties()
    {
        var timeSignatureEvent = new TimeSignatureEvent(123, 3, 2, 24, 8);

        Assert.Equal(123, timeSignatureEvent.AbsoluteTime);
        Assert.Equal(MidiCommandCode.MetaEvent, timeSignatureEvent.CommandCode);
        Assert.Equal(MetaEventType.TimeSignature, timeSignatureEvent.MetaEventType);
        Assert.Equal(3, timeSignatureEvent.Numerator);
        Assert.Equal(2, timeSignatureEvent.Denominator);
        Assert.Equal(24, timeSignatureEvent.TicksInMetronomeClick);
        Assert.Equal(8, timeSignatureEvent.No32ndNotesInQuarterNote);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ConstructorRejectsNumeratorOutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSignatureEvent(0, value, 2, 24, 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ConstructorRejectsDenominatorOutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSignatureEvent(0, 3, value, 24, 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ConstructorRejectsTicksInMetronomeClickOutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSignatureEvent(0, 3, 2, value, 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ConstructorRejectsNo32ndNotesInQuarterNoteOutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSignatureEvent(0, 3, 2, 24, value));
    }

    [Fact]
    public void BinaryReaderConstructorRejectsInvalidLength()
    {
        using (var ms = new MemoryStream(new byte[] { 0x04, 0x02, 0x18, 0x08 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new TimeSignatureEvent(br, 3));
        }
    }

    [Fact]
    public void BinaryReaderConstructorReadsAllFields()
    {
        using (var ms = new MemoryStream(new byte[] { 0x04, 0x02, 0x18, 0x08 }))
        using (var br = new BinaryReader(ms))
        {
            var timeSignatureEvent = new TimeSignatureEvent(br, 4);

            Assert.Equal(4, timeSignatureEvent.Numerator);
            Assert.Equal(2, timeSignatureEvent.Denominator);
            Assert.Equal(24, timeSignatureEvent.TicksInMetronomeClick);
            Assert.Equal(8, timeSignatureEvent.No32ndNotesInQuarterNote);
        }
    }

    [Fact]
    public void ReadNextEventParsesTimeSignatureMetaEvent()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x58, 0x04, 0x03, 0x02, 0x18, 0x08 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<TimeSignatureEvent>(midiEvent);
            var timeSignatureEvent = (TimeSignatureEvent)midiEvent;
            Assert.Equal(0, timeSignatureEvent.DeltaTime);
            Assert.Equal(1, timeSignatureEvent.Channel);
            Assert.Equal(MidiCommandCode.MetaEvent, timeSignatureEvent.CommandCode);
            Assert.Equal(MetaEventType.TimeSignature, timeSignatureEvent.MetaEventType);
            Assert.Equal(3, timeSignatureEvent.Numerator);
            Assert.Equal(2, timeSignatureEvent.Denominator);
            Assert.Equal(24, timeSignatureEvent.TicksInMetronomeClick);
            Assert.Equal(8, timeSignatureEvent.No32ndNotesInQuarterNote);
        }
    }

    [Theory]
    [InlineData(0, "3/1")]
    [InlineData(1, "3/2")]
    [InlineData(2, "3/4")]
    [InlineData(3, "3/8")]
    [InlineData(4, "3/16")]
    [InlineData(5, "3/32")]
    [InlineData(6, "3/64")]
    public void TimeSignatureReturnsPowerOfTwoDenominatorText(int denominator, string expected)
    {
        var timeSignatureEvent = new TimeSignatureEvent(0, 3, denominator, 24, 8);

        Assert.Equal(expected, timeSignatureEvent.TimeSignature);
    }

    [Fact]
    public void TimeSignatureReturnsUnknownForVeryLargeDenominatorExponent()
    {
        var timeSignatureEvent = new TimeSignatureEvent(0, 3, 31, 24, 8);

        Assert.Equal("3/Unknown (31)", timeSignatureEvent.TimeSignature);
    }

    [Fact]
    public void ExportWritesDeltaMetaTypeLengthAndData()
    {
        var timeSignatureEvent = new TimeSignatureEvent(10, 4, 2, 24, 8);

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            timeSignatureEvent.Export(ref absoluteTime, writer);

            Assert.Equal(10, absoluteTime);
            Assert.Equal(new byte[] { 0x0A, 0xFF, 0x58, 0x04, 0x04, 0x02, 0x18, 0x08 }, ms.ToArray());
        }
    }

    [Fact]
    public void CloneCopiesAllProperties()
    {
        var timeSignatureEvent = new TimeSignatureEvent(55, 6, 3, 36, 8);

        var clone = (TimeSignatureEvent)timeSignatureEvent.Clone();

        Assert.NotSame(timeSignatureEvent, clone);
        Assert.Equal(timeSignatureEvent.AbsoluteTime, clone.AbsoluteTime);
        Assert.Equal(timeSignatureEvent.CommandCode, clone.CommandCode);
        Assert.Equal(timeSignatureEvent.MetaEventType, clone.MetaEventType);
        Assert.Equal(timeSignatureEvent.Numerator, clone.Numerator);
        Assert.Equal(timeSignatureEvent.Denominator, clone.Denominator);
        Assert.Equal(timeSignatureEvent.TicksInMetronomeClick, clone.TicksInMetronomeClick);
        Assert.Equal(timeSignatureEvent.No32ndNotesInQuarterNote, clone.No32ndNotesInQuarterNote);
        Assert.Equal(timeSignatureEvent.TimeSignature, clone.TimeSignature);
    }

    [Fact]
    public void ToStringIncludesTimeSignatureAndTimingFields()
    {
        var timeSignatureEvent = new TimeSignatureEvent(0, 3, 2, 24, 8);

        var text = timeSignatureEvent.ToString();

        Assert.Contains("TimeSignature", text);
        Assert.Contains("3/4", text);
        Assert.Contains("TicksInClick:24", text);
        Assert.Contains("32ndsInQuarterNote:8", text);
    }
}

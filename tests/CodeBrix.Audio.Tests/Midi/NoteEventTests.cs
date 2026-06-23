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
public class NoteEventTests
{
    [Fact]
    public void ConstructorSetsProperties()
    {
        var noteEvent = new NoteEvent(123, 2, MidiCommandCode.NoteOn, 60, 100);

        Assert.Equal(123, noteEvent.AbsoluteTime);
        Assert.Equal(2, noteEvent.Channel);
        Assert.Equal(MidiCommandCode.NoteOn, noteEvent.CommandCode);
        Assert.Equal(60, noteEvent.NoteNumber);
        Assert.Equal(100, noteEvent.Velocity);
    }

    [Fact]
    public void BinaryReaderConstructorSetsNoteAndVelocity()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 100 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteEvent = new NoteEvent(reader);

            Assert.Equal(60, noteEvent.NoteNumber);
            Assert.Equal(100, noteEvent.Velocity);
        }
    }

    [Fact]
    public void BinaryReaderConstructorClampsVelocityAbove127()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 200 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteEvent = new NoteEvent(reader);
            Assert.Equal(127, noteEvent.Velocity);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void NoteNumberRejectsValuesOutOfRange(int value)
    {
        var noteEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => noteEvent.NoteNumber = value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void VelocityRejectsValuesOutOfRange(int value)
    {
        var noteEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => noteEvent.Velocity = value);
    }

    [Fact]
    public void GetAsShortMessageReturnsCorrectValue()
    {
        var noteEvent = new NoteEvent(0, 2, MidiCommandCode.NoteOn, 60, 100);
        Assert.Equal(0x00643C91, noteEvent.GetAsShortMessage());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    public void NoteNameReturnsDrumNameOnDrumChannels(int channel)
    {
        var noteEvent = new NoteEvent(0, channel, MidiCommandCode.NoteOn, 35, 100);
        Assert.Equal("Acoustic Bass Drum", noteEvent.NoteName);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    public void NoteNameReturnsDrumFallbackWhenUnknown(int channel)
    {
        var noteEvent = new NoteEvent(0, channel, MidiCommandCode.NoteOn, 34, 100);
        Assert.Equal("Drum 34", noteEvent.NoteName);
    }

    [Fact]
    public void NoteNameReturnsMelodicNameOnNonDrumChannel()
    {
        var noteEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100);
        Assert.Equal("C5", noteEvent.NoteName);
    }

    [Fact]
    public void ExportWritesDeltaStatusNoteAndVelocity()
    {
        var noteEvent = new NoteEvent(0, 2, MidiCommandCode.NoteOn, 60, 100);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            noteEvent.Export(ref absoluteTime, writer);

            var bytes = ms.ToArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x00, bytes[0]);
            Assert.Equal(0x91, bytes[1]);
            Assert.Equal(0x3C, bytes[2]);
            Assert.Equal(0x64, bytes[3]);
        }
    }
}

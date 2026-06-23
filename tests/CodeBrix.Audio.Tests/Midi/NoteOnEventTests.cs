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
public class NoteOnEventTests
{
    [Fact]
    public void ConstructorSetsOffEventAndNoteLength()
    {
        var noteOn = new NoteOnEvent(10, 2, 60, 100, 25);

        Assert.Equal(10, noteOn.AbsoluteTime);
        Assert.Equal(2, noteOn.Channel);
        Assert.Equal(60, noteOn.NoteNumber);
        Assert.Equal(100, noteOn.Velocity);
        Assert.Equal(25, noteOn.NoteLength);

        Assert.NotNull(noteOn.OffEvent);
        Assert.Equal(MidiCommandCode.NoteOff, noteOn.OffEvent.CommandCode);
        Assert.Equal(60, noteOn.OffEvent.NoteNumber);
        Assert.Equal(2, noteOn.OffEvent.Channel);
        Assert.Equal(35, noteOn.OffEvent.AbsoluteTime);
    }

    [Fact]
    public void CloneCreatesEquivalentIndependentCopy()
    {
        var noteOn = new NoteOnEvent(10, 2, 60, 100, 25);

        var clone = (NoteOnEvent)noteOn.Clone();

        Assert.NotSame(noteOn, clone);
        Assert.Equal(noteOn.AbsoluteTime, clone.AbsoluteTime);
        Assert.Equal(noteOn.Channel, clone.Channel);
        Assert.Equal(noteOn.NoteNumber, clone.NoteNumber);
        Assert.Equal(noteOn.Velocity, clone.Velocity);
        Assert.Equal(noteOn.NoteLength, clone.NoteLength);
        Assert.NotSame(noteOn.OffEvent, clone.OffEvent);

        clone.NoteNumber = 61;
        clone.Channel = 3;
        clone.NoteLength = 40;

        Assert.Equal(60, noteOn.NoteNumber);
        Assert.Equal(2, noteOn.Channel);
        Assert.Equal(25, noteOn.NoteLength);
    }

    [Fact]
    public void OffEventRejectsNull()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);

        Assert.Throws<ArgumentException>(() => noteOn.OffEvent = null);
    }

    [Fact]
    public void OffEventRejectsNonNoteOffEvent()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);
        var invalidOffEvent = new NoteEvent(0, 1, MidiCommandCode.ControlChange, 60, 0);

        Assert.Throws<ArgumentException>(() => noteOn.OffEvent = invalidOffEvent);
    }

    [Fact]
    public void OffEventRejectsDifferentNoteNumber()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);
        var invalidOffEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOff, 61, 0);

        Assert.Throws<ArgumentException>(() => noteOn.OffEvent = invalidOffEvent);
    }

    [Fact]
    public void OffEventRejectsDifferentChannel()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);
        var invalidOffEvent = new NoteEvent(0, 2, MidiCommandCode.NoteOff, 60, 0);

        Assert.Throws<ArgumentException>(() => noteOn.OffEvent = invalidOffEvent);
    }

    [Fact]
    public void OffEventAcceptsNoteOnWithZeroVelocity()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);
        var validOffEvent = new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 0);

        noteOn.OffEvent = validOffEvent;

        Assert.Same(validOffEvent, noteOn.OffEvent);
    }

    [Fact]
    public void NoteNumberSetterUpdatesOffEventNoteNumber()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);

        noteOn.NoteNumber = 62;

        Assert.Equal(62, noteOn.NoteNumber);
        Assert.Equal(62, noteOn.OffEvent.NoteNumber);
    }

    [Fact]
    public void ChannelSetterUpdatesOffEventChannel()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);

        noteOn.Channel = 2;

        Assert.Equal(2, noteOn.Channel);
        Assert.Equal(2, noteOn.OffEvent.Channel);
    }

    [Fact]
    public void NoteLengthSetterRejectsNegativeValues()
    {
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 10);

        Assert.Throws<ArgumentException>(() => noteOn.NoteLength = -1);
    }

    [Fact]
    public void NoteLengthSetterUpdatesOffEventAbsoluteTime()
    {
        var noteOn = new NoteOnEvent(10, 1, 60, 100, 5);

        noteOn.NoteLength = 20;

        Assert.Equal(20, noteOn.NoteLength);
        Assert.Equal(30, noteOn.OffEvent.AbsoluteTime);
    }

    [Fact]
    public void ToStringIncludesNoteOffMarkerWhenVelocityZeroAndNoOffEvent()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 0 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteOn = new NoteOnEvent(reader);
            var text = noteOn.ToString();

            Assert.Contains("(Note Off)", text);
        }
    }

    [Fact]
    public void ToStringIncludesUnknownLengthWhenOffEventMissing()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 100 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteOn = new NoteOnEvent(reader);
            var text = noteOn.ToString();

            Assert.Contains("Len: ?", text);
        }
    }

    [Fact]
    public void NoteLengthGetterThrowsInvalidOperationExceptionWhenOffEventMissing()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 100 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteOn = new NoteOnEvent(reader);
            var ex = Assert.Throws<InvalidOperationException>(() => _ = noteOn.NoteLength);
            Assert.Equal("Cannot get NoteLength when OffEvent is null", ex.Message);
        }
    }

    [Fact]
    public void NoteLengthSetterThrowsInvalidOperationExceptionWhenOffEventMissing()
    {
        using (var ms = new MemoryStream(new byte[] { 60, 100 }))
        using (var reader = new BinaryReader(ms))
        {
            var noteOn = new NoteOnEvent(reader);
            var ex = Assert.Throws<InvalidOperationException>(() => noteOn.NoteLength = 10);
            Assert.Equal("Cannot set NoteLength when OffEvent is null", ex.Message);
        }
    }
}

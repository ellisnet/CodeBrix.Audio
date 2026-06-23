using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Midi;
public class MidiEventCloneTests
{
    [Fact]
    public void CanCloneForSameTrack()
    {
        var collection = new MidiEventCollection(0, 120);
        collection.AddEvent(new NoteOnEvent(0, 1, 30, 100, 15), 0);
        
        var clone = (NoteOnEvent)collection[0][0].Clone();
        clone.AbsoluteTime += 15;
        clone.NoteNumber++;
        collection.AddEvent(clone, 0);

        collection.PrepareForExport();

        Assert.Equal(0, collection[0][0].AbsoluteTime);
        Assert.Equal(15, collection[0][1].AbsoluteTime);
        Assert.Equal(30, ((NoteOnEvent)collection[0][0]).NoteNumber);
        Assert.Equal(31, ((NoteOnEvent)collection[0][1]).NoteNumber);
    }

    [Fact]
    public void NoteOnIsDeepClone()
    {
        var ev = new NoteOnEvent(0, 1, 30, 100, 15);
        var clone = (NoteOnEvent)ev.Clone();
        Assert.NotSame(ev.OffEvent, clone.OffEvent);
    }

    [Fact]
    public void SequencerSpecificIsDeepClone()
    {
        var ev = new SequencerSpecificEvent(new byte[] { 0x01 }, 0);
        var clone = (SequencerSpecificEvent)ev.Clone();
        Assert.NotSame(ev.Data, clone.Data);
    }

    private static readonly Dictionary<Type, MidiEvent> TestMidiEvents = new[]
    {
        new MidiEvent(0, 1, MidiCommandCode.Eox),
        new ChannelAfterTouchEvent(0, 1, 0),
        new ControlChangeEvent(0, 1, MidiController.AllNotesOff, 0),
        new KeySignatureEvent(0, 0, 0),
        new MetaEvent(MetaEventType.Copyright, 0, 0),
        new RawMetaEvent(MetaEventType.Copyright, 0, new byte[0]),
        new NoteEvent(0, 1, MidiCommandCode.NoteOff, 0, 0), 
        new NoteOnEvent(0, 1, 0, 0, 0),
        new PatchChangeEvent(0, 1, 0),
        new PitchWheelChangeEvent(0, 1, 0),
        new SequencerSpecificEvent(new byte[0], 0),
        new SmpteOffsetEvent(1, 1, 1, 1, 1),
        new SysexEvent(),
        new TempoEvent(0, 0),
        new TextEvent(string.Empty, MetaEventType.Copyright, 0),
        new TimeSignatureEvent(0, 1, 1, 1, 1),
        new TrackSequenceNumberEvent(1)
    }.ToDictionary(_ => _.GetType());

    [Fact]
    public void CloneReturnsCorrectType()
    {
        foreach (var midiEventType in typeof(MidiEvent).Assembly.GetTypes().Where(typeof(MidiEvent).IsAssignableFrom))
        {
            Assert.True(TestMidiEvents.TryGetValue(midiEventType, out var instance), $"{midiEventType.Name} should be tested.");
            Assert.IsType(midiEventType, instance.Clone());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.Tests.Midi;
public class MidiEventCollectionTest
{
    [Fact]
    public void TestType1()
    {
        MidiEventCollection collection = new MidiEventCollection(1,120);
        collection.AddEvent(new TextEvent("Test",MetaEventType.TextEvent,0),0);
        collection.AddEvent(new NoteOnEvent(0, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(15, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(30, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(0, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(15, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(30, 10, 60, 100, 15), 10);
        Assert.Equal(11, collection.Tracks);
        collection.PrepareForExport();
        Assert.Equal(3, collection.Tracks);
        IList<MidiEvent> track0 = collection.GetTrackEvents(0);
        Assert.Equal(2, track0.Count);
        Assert.Equal(4, collection.GetTrackEvents(1).Count);
        Assert.Equal(4, collection.GetTrackEvents(2).Count);
        Assert.True(MidiEvent.IsEndTrack(track0[track0.Count - 1]));
    }

    [Fact]
    public void TestType0()
    {
        MidiEventCollection collection = new MidiEventCollection(0, 120);
        collection.AddEvent(new TextEvent("Test", MetaEventType.TextEvent, 0), 0);
        collection.AddEvent(new NoteOnEvent(0, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(15, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(30, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(0, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(15, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(30, 10, 60, 100, 15), 10);
        Assert.Equal(1, collection.Tracks);
        collection.PrepareForExport();
        Assert.Equal(1, collection.Tracks);
        IList<MidiEvent> track0 = collection.GetTrackEvents(0);
        Assert.Equal(8, track0.Count);
        Assert.True(MidiEvent.IsEndTrack(track0[track0.Count - 1]));
    }

    [Fact]
    public void TestType1ToType0()
    {
        MidiEventCollection collection = new MidiEventCollection(1, 120);
        collection.AddEvent(new TextEvent("Test", MetaEventType.TextEvent, 0), 0);
        collection.AddEvent(new NoteOnEvent(0, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(15, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(30, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(0, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(15, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(30, 10, 60, 100, 15), 10);
        Assert.Equal(11, collection.Tracks);
        collection.MidiFileType = 0;
        collection.PrepareForExport();
        Assert.Equal(1, collection.Tracks);
        IList<MidiEvent> track0 = collection.GetTrackEvents(0);
        Assert.Equal(8, track0.Count);
        Assert.True(MidiEvent.IsEndTrack(track0[track0.Count - 1]));
    }

    [Fact]
    public void TestType0ToType1()
    {
        MidiEventCollection collection = new MidiEventCollection(0, 120);
        collection.AddEvent(new TextEvent("Test", MetaEventType.TextEvent, 0), 0);
        collection.AddEvent(new NoteOnEvent(0, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(15, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(30, 1, 30, 100, 15), 1);
        collection.AddEvent(new NoteOnEvent(0, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(15, 10, 60, 100, 15), 10);
        collection.AddEvent(new NoteOnEvent(30, 10, 60, 100, 15), 10);
        Assert.Equal(1, collection.Tracks);
        collection.MidiFileType = 1;
        collection.PrepareForExport();
        Assert.Equal(3, collection.Tracks);
        IList<MidiEvent> track0 = collection.GetTrackEvents(0);
        Assert.Equal(2, track0.Count);
        Assert.Equal(4, collection.GetTrackEvents(1).Count);
        Assert.Equal(4, collection.GetTrackEvents(2).Count);
        Assert.True(MidiEvent.IsEndTrack(track0[track0.Count - 1]));
    }
}

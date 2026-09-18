using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// Covers the CodeBrix additions to <see cref="MidiEventCollection"/>. The upstream behaviour is
/// covered by MidiEventCollectionTest.cs beside this file.
/// </summary>
public class MidiEventCollectionTests
{
    [Fact]
    public void clone_relinks_note_on_events_to_their_cloned_off_events()
    {
        //Arrange - a note-on and the note-off it owns, on one track, the way reading a file links
        // them. Cloning the two events separately does NOT reproduce that link: a cloned note-on
        // builds itself a fresh note-off that nothing else in the copy points at.
        var collection = new MidiEventCollection(1, 480) { StartAbsoluteTime = 7 };
        var track = collection.AddTrack();
        var noteOn = new NoteOnEvent(0, 1, 60, 100, 480);
        track.Add(noteOn);
        track.Add(noteOn.OffEvent);
        track.Add(new TextEvent("CodeBrix.Audio", MetaEventType.TextEvent, 0));

        //Act
        var clone = collection.Clone();

        //Assert
        clone.Tracks.Should().Be(1);
        clone.MidiFileType.Should().Be(1);
        clone.DeltaTicksPerQuarterNote.Should().Be(480);
        clone.StartAbsoluteTime.Should().Be(7);

        var cloned = clone.GetTrackEvents(0);
        cloned.Should().HaveCount(3);
        cloned[0].Should().NotBeSameAs(noteOn);

        var clonedNoteOn = (NoteOnEvent)cloned[0];
        clonedNoteOn.OffEvent.Should().BeSameAs(cloned[1]);
        clonedNoteOn.OffEvent.Should().NotBeSameAs(noteOn.OffEvent);
        clonedNoteOn.NoteLength.Should().Be(480);

        // The link is the whole point: shortening the copy's note moves the note-off the COPY
        // holds, and leaves the original where it was.
        clonedNoteOn.NoteLength = 240;
        cloned[1].AbsoluteTime.Should().Be(240);
        noteOn.OffEvent.AbsoluteTime.Should().Be(480);
    }

    [Fact]
    public void clone_copies_every_track_and_leaves_the_original_alone()
    {
        //Arrange
        var collection = new MidiEventCollection(1, 96);
        collection.AddEvent(new TempoEvent(500000, 0), 0);
        collection.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100), 1);
        collection.AddEvent(new NoteEvent(0, 10, MidiCommandCode.NoteOn, 38, 100), 2);

        //Act
        var clone = collection.Clone();
        clone.GetTrackEvents(1).Add(new NoteEvent(96, 1, MidiCommandCode.NoteOff, 60, 0));

        //Assert
        clone.Tracks.Should().Be(3);
        clone.GetTrackEvents(0)[0].Should().BeOfType<TempoEvent>();
        clone.GetTrackEvents(2)[0].Channel.Should().Be(10);

        collection.GetTrackEvents(1).Should().HaveCount(1);
        clone.GetTrackEvents(1).Should().HaveCount(2);
    }

    [Fact]
    public void clone_copies_a_collection_holding_a_note_on_with_no_note_off()
    {
        //Arrange - a note-on read straight off the wire has no note-off, so its length cannot be
        // read. Such a collection used to be uncopyable: one dangling note defeated the whole clone.
        using var stream = new System.IO.MemoryStream(new byte[] { 60, 100 });
        using var reader = new System.IO.BinaryReader(stream);
        var dangling = new NoteOnEvent(reader) { AbsoluteTime = 0 };
        var collection = new MidiEventCollection(1, 480);
        collection.AddEvent(new TempoEvent(500000, 0), 0);
        collection.AddEvent(dangling, 1);

        //Act
        var clone = collection.Clone();

        //Assert
        clone.Tracks.Should().Be(2);
        var copied = clone.GetTrackEvents(1)[0] as NoteOnEvent;
        copied.Should().NotBeNull();
        copied.Should().NotBeSameAs(dangling);
        copied.OffEvent.Should().BeNull();
        copied.NoteNumber.Should().Be(60);
        copied.Velocity.Should().Be(100);
        dangling.OffEvent.Should().BeNull();
    }
}

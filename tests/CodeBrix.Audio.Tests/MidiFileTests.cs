using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Round-trip tests for reading and writing Standard MIDI Files via
/// <see cref="MidiFile"/> and <see cref="MidiEventCollection"/>.
/// </summary>
public class MidiFileTests
{
    private static MidiEventCollection BuildSingleNoteCollection()
    {
        var events = new MidiEventCollection(0, 480);
        var track = events.AddTrack();
        track.Add(new TempoEvent(500000, 0)); // 120 bpm
        track.Add(new NoteOnEvent(0, 1, 60, 100, 480)); // middle C, one quarter note
        track.Add(new TextEvent("CodeBrix.Audio", MetaEventType.TextEvent, 0));
        events.PrepareForExport();
        return events;
    }

    [Fact]
    public void Export_then_read_preserves_track_count_and_ticks()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mid");
        var written = BuildSingleNoteCollection();

        //Act
        MidiFile.Export(path, written);
        var read = new MidiFile(path, false);

        //Assert
        read.Tracks.Should().Be(written.Tracks);
        read.DeltaTicksPerQuarterNote.Should().Be(480);
        File.Delete(path);
    }

    [Fact]
    public void Export_then_read_preserves_the_note_on_event()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mid");
        MidiFile.Export(path, BuildSingleNoteCollection());

        //Act
        var read = new MidiFile(path, false);
        var noteOn = read.Events[0].OfType<NoteOnEvent>().FirstOrDefault();

        //Assert
        noteOn.Should().NotBeNull();
        noteOn.NoteNumber.Should().Be(60);
        noteOn.Velocity.Should().Be(100);
        File.Delete(path);
    }

    [Fact]
    public void Type0_collection_rejects_a_second_track_on_export()
    {
        //Arrange
        var events = new MidiEventCollection(0, 480);
        events.AddTrack().Add(new MetaEvent(MetaEventType.EndTrack, 0, 0));
        events.AddTrack().Add(new MetaEvent(MetaEventType.EndTrack, 0, 0));
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mid");

        //Act
        var act = () => MidiFile.Export(path, events);

        //Assert
        act.Should().Throw<System.ArgumentException>();
    }
}

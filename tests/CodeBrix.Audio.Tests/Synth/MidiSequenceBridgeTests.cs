using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiSequence.FromEvents"/> - the bridge from the editable MIDI file model in
/// CodeBrix.Audio.Midi to the immutable playback sequence in CodeBrix.Audio.Synth.
/// </summary>
public class MidiSequenceBridgeTests
{
    // A quarter note at 120 ticks per quarter, 120 BPM: one note on, one note off, end of track.
    private static MidiEventCollection BuildSingleNoteCollection(int ticksPerQuarter = 120)
    {
        var collection = new MidiEventCollection(1, ticksPerQuarter);
        collection.AddEvent(new NoteOnEvent(0, 1, 60, 100, ticksPerQuarter), 1);
        collection.AddEvent(new NoteEvent(ticksPerQuarter, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.PrepareForExport();
        return collection;
    }

    [Fact]
    public void from_events_builds_a_playable_sequence()
    {
        //Arrange
        var collection = BuildSingleNoteCollection();

        //Act
        var sequence = MidiSequence.FromEvents(collection);

        //Assert
        sequence.Should().NotBeNull();
        sequence.Length.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void from_events_preserves_the_tempo_derived_duration()
    {
        //Arrange
        // 120 ticks per quarter with the default 120 BPM means one quarter note is 0.5 seconds.
        var collection = BuildSingleNoteCollection();

        //Act
        var sequence = MidiSequence.FromEvents(collection);

        //Assert
        sequence.Length.TotalSeconds.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void from_events_matches_what_the_file_path_produces()
    {
        //Arrange
        // The bridge deliberately routes through the standard MIDI encoding, so a collection written
        // to disk and read back must give the same sequence as converting it directly.
        var collection = BuildSingleNoteCollection();
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codebrix-bridge-{Guid.NewGuid():N}.mid");

        try
        {
            MidiFile.Export(path, collection);

            //Act
            var viaBridge = MidiSequence.FromEvents(BuildSingleNoteCollection());
            var viaFile = new MidiSequence(path);

            //Assert
            viaBridge.Length.Should().Be(viaFile.Length);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [Fact]
    public void from_events_rejects_a_null_collection()
    {
        //Act
        var act = () => MidiSequence.FromEvents(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void from_events_survives_a_collection_with_several_tracks()
    {
        //Arrange
        var collection = new MidiEventCollection(1, 120);
        collection.AddEvent(new NoteOnEvent(0, 1, 60, 100, 120), 1);
        collection.AddEvent(new NoteEvent(120, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.AddEvent(new NoteOnEvent(0, 2, 67, 100, 240), 2);
        collection.AddEvent(new NoteEvent(240, 2, MidiCommandCode.NoteOff, 67, 0), 2);
        collection.PrepareForExport();

        //Act
        var sequence = MidiSequence.FromEvents(collection);

        //Assert
        // The tracks are merged into one absolute-time stream, and the longer track sets the length.
        sequence.Length.TotalSeconds.Should().BeApproximately(1.0, 0.01);
    }
}

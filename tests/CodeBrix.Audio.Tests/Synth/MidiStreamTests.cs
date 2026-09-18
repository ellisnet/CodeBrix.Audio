using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiStream"/>: what it takes, in what order it keeps it, what it refuses, and
/// the recording it builds as it goes.
/// </summary>
public class MidiStreamTests
{
    [Fact]
    public void a_new_stream_is_empty_not_completed_and_has_no_horizon()
    {
        //Arrange
        //Act
        var stream = new MidiStream();

        //Assert
        stream.TicksPerQuarterNote.Should().Be(480);
        stream.EventCount.Should().Be(0);
        stream.HorizonTicks.Should().Be(0);
        stream.LateEventCount.Should().Be(0);
        stream.IsCompleted.Should().BeFalse();
        stream.Preroll.Should().Be(TimeSpan.Zero);
        stream.Problems.Should().BeEmpty();
    }

    [Fact]
    public void ticks_per_quarter_note_must_be_positive()
    {
        //Arrange
        Action zero = () => new MidiStream(0);
        Action negative = () => new MidiStream(-480);

        //Act
        var stream = new MidiStream(96);

        //Assert
        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
        stream.TicksPerQuarterNote.Should().Be(96);
    }

    [Fact]
    public void append_orders_events_by_tick_and_keeps_arrival_order_for_equal_ticks()
    {
        //Arrange - appended out of order, with two at the same tick.
        var stream = new MidiStream(480);

        //Act
        stream.Append(new ControlChangeEvent(960, 1, MidiController.Expression, 1));
        stream.Append(new ControlChangeEvent(0, 1, MidiController.Expression, 2));
        stream.Append(new ControlChangeEvent(960, 1, MidiController.Expression, 3));
        stream.Append(new ControlChangeEvent(480, 1, MidiController.Expression, 4));

        //Assert
        var entries = stream.Entries;
        entries.Count.Should().Be(4);
        entries[0].Tick.Should().Be(0);
        entries[1].Tick.Should().Be(480);
        entries[2].Tick.Should().Be(960);
        entries[3].Tick.Should().Be(960);

        // The two at tick 960 play in the order they arrived, the way a track's own order survives
        // the merge that builds a sequence.
        entries[2].Message.Data2.Should().Be(1);
        entries[3].Message.Data2.Should().Be(3);
        stream.HorizonTicks.Should().Be(960);
    }

    [Fact]
    public void a_note_on_with_a_duration_schedules_its_note_off_and_moves_the_horizon()
    {
        //Arrange
        var stream = new MidiStream(480);

        //Act
        stream.AppendNote(480, 1, 60, 100, 240);

        //Assert
        stream.HorizonTicks.Should().Be(720);
        stream.EventCount.Should().Be(2);

        var entries = stream.Entries;
        entries.Count.Should().Be(2);
        entries[0].Tick.Should().Be(480);
        entries[0].Message.Command.Should().Be(0x90);
        entries[1].Tick.Should().Be(720);
        entries[1].Message.Command.Should().Be(0x80);
        entries[1].Message.Data1.Should().Be(60);
    }

    [Fact]
    public void append_rejects_a_negative_tick_and_a_channel_outside_one_to_sixteen()
    {
        //Arrange
        var stream = new MidiStream(480);
        Action negativeTick = () => stream.Append(new NoteEvent(-1, 1, MidiCommandCode.NoteOn, 60, 100));
        Action channelSeventeen = () => stream.AppendNote(0, 17, 60, 100, 240);
        Action channelZero = () => stream.AppendNote(0, 0, 60, 100, 240);
        Action nothing = () => stream.Append((MidiEvent)null);

        //Act
        //Assert
        negativeTick.Should().Throw<ArgumentOutOfRangeException>();
        channelSeventeen.Should().Throw<ArgumentOutOfRangeException>();
        channelZero.Should().Throw<ArgumentOutOfRangeException>();
        nothing.Should().Throw<ArgumentNullException>();
        stream.EventCount.Should().Be(0);
        stream.HorizonTicks.Should().Be(0);
    }

    [Fact]
    public void append_of_a_batch_validates_everything_before_taking_anything()
    {
        //Arrange
        var stream = new MidiStream(480);
        var spoiled = new List<MidiEvent>
        {
            new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100),
            new NoteEvent(480, 1, MidiCommandCode.NoteOff, 60, 0),
            new NoteEvent(-5, 1, MidiCommandCode.NoteOn, 62, 100)
        };
        Action act = () => stream.Append(spoiled);

        //Act
        act.Should().Throw<ArgumentOutOfRangeException>();

        //Assert - not one of the good events was taken.
        stream.EventCount.Should().Be(0);
        stream.HorizonTicks.Should().Be(0);

        stream.Append(new List<MidiEvent>
        {
            new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100),
            new NoteEvent(480, 1, MidiCommandCode.NoteOff, 60, 0)
        });
        stream.EventCount.Should().Be(2);
        stream.HorizonTicks.Should().Be(480);
    }

    [Fact]
    public void append_after_complete_throws_and_complete_is_idempotent()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 60, 100, 240);

        //Act
        stream.Complete();
        stream.Complete();

        //Assert
        stream.IsCompleted.Should().BeTrue();
        stream.EventCount.Should().Be(2);

        Action one = () => stream.AppendNote(480, 1, 62, 100, 240);
        Action batch = () => stream.Append(new List<MidiEvent> { new NoteEvent(480, 1, MidiCommandCode.NoteOn, 62, 100) });
        Action tempo = () => stream.AppendTempo(480, 90);
        one.Should().Throw<InvalidOperationException>();
        batch.Should().Throw<InvalidOperationException>();
        tempo.Should().Throw<InvalidOperationException>();
        stream.EventCount.Should().Be(2);
    }

    [Fact]
    public void end_of_track_events_are_ignored_and_reported_once()
    {
        //Arrange
        var stream = new MidiStream(480);

        //Act
        stream.Append(new MetaEvent(MetaEventType.EndTrack, 0, 0));
        stream.Append(new MetaEvent(MetaEventType.EndTrack, 0, 960));

        //Assert - a stream ends when its producer completes it, not when an event says so.
        stream.EventCount.Should().Be(0);
        stream.HorizonTicks.Should().Be(0);
        stream.Entries.Should().BeEmpty();
        stream.Problems.Should().HaveCount(1);
    }

    [Fact]
    public void the_recording_keeps_every_event_at_its_original_tick()
    {
        //Arrange
        var stream = new MidiStream(480);

        //Act
        stream.AppendTempo(0, 100);
        stream.AppendNote(480, 1, 60, 100, 240);
        stream.Append(new ControlChangeEvent(960, 1, MidiController.Expression, 64));

        //Assert
        var collection = stream.ToMidiEventCollection();
        var channelTrack = collection.GetTrackEvents(1);
        channelTrack[0].AbsoluteTime.Should().Be(480);
        channelTrack[0].CommandCode.Should().Be(MidiCommandCode.NoteOn);
        channelTrack[1].AbsoluteTime.Should().Be(720);
        channelTrack[1].CommandCode.Should().Be(MidiCommandCode.NoteOff);
        channelTrack[2].AbsoluteTime.Should().Be(960);
        channelTrack[2].CommandCode.Should().Be(MidiCommandCode.ControlChange);
        collection.GetTrackEvents(0)[0].AbsoluteTime.Should().Be(0);
    }

    [Fact]
    public void the_recording_puts_conductor_events_on_track_zero_and_channel_events_on_channel_tracks()
    {
        //Arrange
        var stream = new MidiStream(480);

        //Act - channel 2 is used before channel 10, so it takes the earlier track.
        stream.AppendTempo(0, 120);
        stream.Append(new TimeSignatureEvent(0, 3, 2, 24, 8));
        stream.AppendNote(0, 2, 60, 100, 480);
        stream.AppendNote(0, 10, 38, 100, 120);

        //Assert
        var collection = stream.ToMidiEventCollection();
        collection.MidiFileType.Should().Be(1);
        collection.DeltaTicksPerQuarterNote.Should().Be(480);
        collection.Tracks.Should().Be(3);

        var conductor = collection.GetTrackEvents(0);
        conductor.Should().HaveCount(3); // tempo, time signature, end of track
        conductor[0].Should().BeOfType<TempoEvent>();
        conductor[1].Should().BeOfType<TimeSignatureEvent>();

        collection.GetTrackEvents(1)[0].Channel.Should().Be(2);
        collection.GetTrackEvents(2)[0].Channel.Should().Be(10);
    }

    [Fact]
    public void the_snapshot_links_each_note_on_to_its_own_off_event()
    {
        //Arrange - the snapshot is a deep copy, and a copy whose note-ons point at note-offs it
        // does not contain is a trap for anyone who edits it before saving.
        var stream = new MidiStream(480);
        stream.AppendTempo(0, 120);
        stream.AppendNote(0, 1, 60, 100, 480);
        stream.AppendNote(960, 1, 64, 100, 240);

        //Act
        var snapshot = stream.ToMidiEventCollection();

        //Assert
        var channelTrack = snapshot.GetTrackEvents(1);
        var first = (NoteOnEvent)channelTrack[0];
        var second = (NoteOnEvent)channelTrack[2];

        first.OffEvent.Should().BeSameAs(channelTrack[1]);
        second.OffEvent.Should().BeSameAs(channelTrack[3]);
        first.NoteLength.Should().Be(480);
        second.NoteLength.Should().Be(240);

        // Two snapshots share nothing, with each other or with the stream.
        var other = stream.ToMidiEventCollection();
        other.GetTrackEvents(1)[0].Should().NotBeSameAs(first);
        ((NoteOnEvent)other.GetTrackEvents(1)[0]).OffEvent.Should().BeSameAs(other.GetTrackEvents(1)[1]);
    }

    [Fact]
    public void to_sequence_of_a_completed_stream_has_the_length_of_its_last_event()
    {
        //Arrange - 960 ticks at 480 per quarter and the default 120 BPM is exactly one second.
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 60, 100, 960);
        stream.Complete();

        //Act
        var sequence = stream.ToSequence();

        //Assert
        sequence.Length.TotalSeconds.Should().BeApproximately(1.0, 1e-6);

        // The stream derives the horizon's time with the very arithmetic the sequence uses, so the
        // two agree exactly rather than approximately.
        sequence.Length.Should().Be(stream.HorizonTime);
    }

    [Fact]
    public void append_tempo_and_append_note_are_the_typed_events_they_stand_for()
    {
        //Arrange
        var stream = new MidiStream(480);

        //Act
        stream.AppendTempo(240, 90);
        stream.AppendNote(480, 3, 64, 111, 240);

        //Assert
        var collection = stream.ToMidiEventCollection();

        collection.GetTrackEvents(0)[0].Should().BeOfType<TempoEvent>();
        var tempo = (TempoEvent)collection.GetTrackEvents(0)[0];
        tempo.AbsoluteTime.Should().Be(240);
        tempo.Tempo.Should().BeApproximately(90.0, 0.001);

        collection.GetTrackEvents(1)[0].Should().BeOfType<NoteOnEvent>();
        var noteOn = (NoteOnEvent)collection.GetTrackEvents(1)[0];
        noteOn.AbsoluteTime.Should().Be(480);
        noteOn.Channel.Should().Be(3);
        noteOn.NoteNumber.Should().Be(64);
        noteOn.Velocity.Should().Be(111);
        noteOn.NoteLength.Should().Be(240);
    }
}

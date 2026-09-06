using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiTempoMap"/> and the tempo map a <see cref="MidiSequence"/> records while it
/// loads: loading APPLIES the tempo map and drops the tempo events, so this is the only thing that
/// can answer a musical question about a loaded sequence.
/// </summary>
public class MidiTempoMapTests
{
    [Fact]
    public void an_empty_map_reports_one_entry_at_the_midi_default()
    {
        //Arrange & Act
        var map = new MidiTempoMap([]);

        //Assert
        map.Changes.Should().HaveCount(1);
        map.IsConstant.Should().BeTrue();
        map.InitialBeatsPerMinute.Should().Be(120.0);
    }

    [Fact]
    public void a_map_whose_first_change_is_late_gains_a_default_entry_in_front_of_it()
    {
        //Arrange
        var late = new MidiTempoChange(TimeSpan.FromSeconds(2), 4.0, 90.0);

        //Act
        var map = new MidiTempoMap([late]);

        //Assert
        map.Changes.Should().HaveCount(2);
        map.Changes[0].Time.Should().Be(TimeSpan.Zero);
        map.Changes[0].BeatsPerMinute.Should().Be(120.0);
        map.Changes[1].Should().Be(late);
    }

    [Fact]
    public void beat_position_follows_the_tempo_in_force()
    {
        //Arrange - 120 BPM for two beats, then 60 BPM.
        var map = new MidiTempoMap([
            new MidiTempoChange(TimeSpan.Zero, 0.0, 120.0),
            new MidiTempoChange(TimeSpan.FromSeconds(1), 2.0, 60.0),
        ]);

        //Act
        var early = map.BeatPositionAt(TimeSpan.FromSeconds(0.5));
        var late = map.BeatPositionAt(TimeSpan.FromSeconds(3));

        //Assert
        early.Should().BeApproximately(1.0, 1e-9);
        late.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void time_at_is_the_inverse_of_beat_position_at()
    {
        //Arrange
        var map = new MidiTempoMap([
            new MidiTempoChange(TimeSpan.Zero, 0.0, 140.0),
            new MidiTempoChange(TimeSpan.FromSeconds(1.7142857), 4.0, 75.0),
        ]);

        //Act
        var time = map.TimeAt(9.5);
        var round = map.BeatPositionAt(time);

        //Assert
        round.Should().BeApproximately(9.5, 1e-6);
    }

    [Fact]
    public void negative_positions_read_as_the_start()
    {
        //Arrange
        var map = new MidiTempoMap([new MidiTempoChange(TimeSpan.Zero, 0.0, 100.0)]);

        //Act & Assert
        map.BeatPositionAt(TimeSpan.FromSeconds(-5)).Should().Be(0.0);
        map.TimeAt(-3).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void a_sequence_with_no_tempo_event_reports_the_default_map()
    {
        //Arrange & Act
        var sequence = MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 500, stepTicks: 1000);

        //Assert
        sequence.TempoMap.Should().NotBeNull();
        sequence.TempoMap.IsConstant.Should().BeTrue();
        sequence.TempoMap.InitialBeatsPerMinute.Should().Be(120.0);
    }

    [Fact]
    public void a_sequence_records_every_tempo_change_it_applied()
    {
        //Arrange - 480 ppq: the change at tick 960 is on beat 2.
        var sequence = MultiTrackTestSong.BuildTempoSequence(
            [(0, 100.0), (960, 150.0)],
            lengthTicks: 1920);

        //Act
        var map = sequence.TempoMap;

        //Assert
        map.Changes.Should().HaveCount(2);
        map.Changes[0].BeatPosition.Should().BeApproximately(0.0, 1e-9);
        map.Changes[0].BeatsPerMinute.Should().BeApproximately(100.0, 0.01);
        map.Changes[1].BeatPosition.Should().BeApproximately(2.0, 1e-9);
        map.Changes[1].BeatsPerMinute.Should().BeApproximately(150.0, 0.01);
        map.Changes[1].Time.TotalSeconds.Should().BeApproximately(1.2, 1e-3);
    }

    [Fact]
    public void a_sequence_tempo_map_converts_its_own_length_back_to_beats()
    {
        //Arrange - four beats at 480 ppq, whatever the tempo does in the middle.
        var sequence = MultiTrackTestSong.BuildTempoSequence(
            [(0, 90.0), (480, 180.0), (1440, 60.0)],
            lengthTicks: 1920);

        //Act
        var beats = sequence.TempoMap.BeatPositionAt(sequence.Length);

        //Assert
        beats.Should().BeApproximately(4.0, 1e-4);
    }
}

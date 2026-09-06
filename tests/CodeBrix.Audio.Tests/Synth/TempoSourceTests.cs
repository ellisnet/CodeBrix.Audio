using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="TempoSource"/>: the defaults a source reports before a transport has touched
/// it, the values it refuses, and the derived musical readings.
/// </summary>
public class TempoSourceTests
{
    [Fact]
    public void a_new_source_reports_the_midi_defaults()
    {
        //Arrange & Act
        var tempo = new TempoSource();

        //Assert
        tempo.BeatsPerMinute.Should().Be(120.0);
        tempo.BeatPosition.Should().Be(0.0);
        tempo.BeatsPerBar.Should().Be(4);
        tempo.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void update_publishes_all_three_readings()
    {
        //Arrange
        var tempo = new TempoSource();

        //Act
        tempo.Update(93.5, 17.25, true);

        //Assert
        tempo.BeatsPerMinute.Should().Be(93.5);
        tempo.BeatPosition.Should().Be(17.25);
        tempo.IsPlaying.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-30.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void beats_per_minute_ignores_a_value_that_is_not_a_tempo(double value)
    {
        //Arrange
        var tempo = new TempoSource();
        tempo.BeatsPerMinute = 90.0;

        //Act
        tempo.BeatsPerMinute = value;

        //Assert
        tempo.BeatsPerMinute.Should().Be(90.0);
    }

    [Fact]
    public void beat_position_clamps_a_negative_value_to_zero()
    {
        //Arrange
        var tempo = new TempoSource();

        //Act
        tempo.BeatPosition = -4.0;

        //Assert
        tempo.BeatPosition.Should().Be(0.0);
    }

    [Fact]
    public void beats_per_bar_ignores_a_value_below_one()
    {
        //Arrange
        var tempo = new TempoSource();
        tempo.BeatsPerBar = 3;

        //Act
        tempo.BeatsPerBar = 0;

        //Assert
        tempo.BeatsPerBar.Should().Be(3);
    }

    [Fact]
    public void bar_position_divides_the_beat_position_by_the_bar_length()
    {
        //Arrange
        var tempo = new TempoSource { BeatsPerBar = 3 };

        //Act
        tempo.BeatPosition = 7.5;

        //Assert
        tempo.BarPosition.Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void beat_duration_follows_the_tempo()
    {
        //Arrange
        var tempo = new TempoSource();

        //Act
        tempo.BeatsPerMinute = 60.0;

        //Assert
        tempo.BeatDuration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void reset_returns_the_position_and_tempo_but_keeps_the_bar_length()
    {
        //Arrange
        var tempo = new TempoSource { BeatsPerBar = 7 };
        tempo.Update(180.0, 40.0, true);

        //Act
        tempo.Reset();

        //Assert
        tempo.BeatsPerMinute.Should().Be(120.0);
        tempo.BeatPosition.Should().Be(0.0);
        tempo.IsPlaying.Should().BeFalse();
        tempo.BeatsPerBar.Should().Be(7);
    }
}

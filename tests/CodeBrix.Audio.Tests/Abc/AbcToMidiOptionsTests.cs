using System;
using CodeBrix.Audio.Abc;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Abc;

/// <summary>
/// The defaults <see cref="AbcToMidiOptions"/> starts with, and the values it refuses.
/// </summary>
public class AbcToMidiOptionsTests
{
    [Fact]
    public void the_defaults_are_the_documented_ones()
    {
        //Arrange
        //Act
        var options = new AbcToMidiOptions();

        //Assert
        options.TicksPerQuarterNote.Should().Be(480);
        options.DefaultBeatsPerMinute.Should().Be(120.0);
        options.Velocity.Should().Be(100);
        options.GraceNoteLength.Should().Be(new AbcDuration(1, 64));
        options.HonourMidiDirectives.Should().BeTrue();
        options.VoiceChannels.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32768)]
    public void an_impossible_resolution_is_refused(int value)
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        Action act = () => options.TicksPerQuarterNote = value;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(480)]
    [InlineData(32767)]
    public void a_usable_resolution_is_accepted(int value)
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        options.TicksPerQuarterNote = value;

        //Assert
        options.TicksPerQuarterNote.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1001.0)]
    [InlineData(double.NaN)]
    public void an_impossible_default_tempo_is_refused(double value)
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        Action act = () => options.DefaultBeatsPerMinute = value;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(128)]
    public void an_impossible_velocity_is_refused(int value)
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        Action act = () => options.Velocity = value;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_grace_note_length_of_zero_is_refused()
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        Action act = () => options.GraceNoteLength = AbcDuration.Zero;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_negative_grace_note_length_is_refused()
    {
        //Arrange
        var options = new AbcToMidiOptions();

        //Act
        Action act = () => options.GraceNoteLength = new AbcDuration(-1, 64);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_clone_carries_every_value_including_the_channel_map()
    {
        //Arrange
        var options = new AbcToMidiOptions
        {
            TicksPerQuarterNote = 96,
            DefaultBeatsPerMinute = 72,
            Velocity = 90,
            GraceNoteLength = new AbcDuration(1, 32),
            HonourMidiDirectives = false,
        };
        options.VoiceChannels["T1"] = 4;

        //Act
        var copy = options.Clone();

        //Assert
        copy.TicksPerQuarterNote.Should().Be(96);
        copy.DefaultBeatsPerMinute.Should().Be(72.0);
        copy.Velocity.Should().Be(90);
        copy.GraceNoteLength.Should().Be(new AbcDuration(1, 32));
        copy.HonourMidiDirectives.Should().BeFalse();
        copy.VoiceChannels["T1"].Should().Be(4);
    }

    [Fact]
    public void a_clones_channel_map_is_its_own()
    {
        //Arrange
        var options = new AbcToMidiOptions();
        options.VoiceChannels["T1"] = 4;
        var copy = options.Clone();

        //Act
        copy.VoiceChannels["T1"] = 9;

        //Assert
        options.VoiceChannels["T1"].Should().Be(4);
    }
}

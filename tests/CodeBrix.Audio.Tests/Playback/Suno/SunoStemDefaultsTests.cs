using System.Linq;
using CodeBrix.Audio.Playback.Suno;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

public class SunoStemDefaultsTests
{
    [Fact]
    public void the_vocabulary_holds_all_twelve_names_suno_advertises() =>
        SunoStemDefaults.KnownStemNames.Should().HaveCount(12);

    [Fact]
    public void every_stem_keeps_the_program_channel_and_percussion_flag_it_was_given()
    {
        // The table is written with GeneralMidiProgram members rather than bare numbers, so this
        // pins the NUMBERS they stand for - all twelve of them, in order, not just the eight the
        // measured-defaults theory below covers.
        //Arrange
        var expected = new[]
        {
            ("Vocals", 54, 1, false),
            ("Backing Vocals", 53, 1, false),
            ("Drums", 118, 10, true),
            ("Percussion", 9, 10, true),
            ("Bass", 32, 1, false),
            ("Guitar", 24, 1, false),
            ("Keyboard", 0, 1, false),
            ("Piano", 0, 1, false),
            ("Synth", 80, 1, false),
            ("Strings", 48, 1, false),
            ("Brass", 61, 1, false),
            ("FX", 96, 1, false),
        };

        //Act
        var actual = SunoStemDefaults.All
            .Select(stem => (stem.Name, stem.GmProgram, stem.Channel, stem.IsPercussion))
            .ToArray();

        //Assert
        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData("Vocals", 54, 1, false)]
    [InlineData("Backing Vocals", 53, 1, false)]
    [InlineData("Bass", 32, 1, false)]
    [InlineData("Drums", 118, 10, true)]
    [InlineData("Percussion", 9, 10, true)]
    [InlineData("Guitar", 24, 1, false)]
    [InlineData("Synth", 80, 1, false)]
    [InlineData("FX", 96, 1, false)]
    public void TryGet_returns_the_measured_defaults(string name, int program, int channel, bool percussion)
    {
        //Act
        var found = SunoStemDefaults.TryGet(name, out var defaults);

        //Assert
        found.Should().BeTrue();
        defaults.GmProgram.Should().Be(program);
        defaults.Channel.Should().Be(channel);
        defaults.IsPercussion.Should().Be(percussion);
    }

    [Fact]
    public void TryGet_ignores_case_and_surrounding_space() =>
        SunoStemDefaults.TryGet("  backing VOCALS ", out _).Should().BeTrue();

    [Fact]
    public void TryGet_does_not_invent_defaults_for_a_name_outside_the_vocabulary() =>
        SunoStemDefaults.TryGet("Kazoo", out _).Should().BeFalse();

    [Fact]
    public void GetOrFallback_falls_back_to_program_zero_on_channel_one()
    {
        //Act
        var defaults = SunoStemDefaults.GetOrFallback("Kazoo");

        //Assert
        defaults.Name.Should().Be("Kazoo");
        defaults.GmProgram.Should().Be(0);
        defaults.Channel.Should().Be(1);
        defaults.IsPercussion.Should().BeFalse();
    }

    [Fact]
    public void SortIndex_puts_an_unknown_name_after_every_known_one() =>
        SunoStemDefaults.SortIndex("Kazoo").Should()
            .BeGreaterThan(SunoStemDefaults.SortIndex("FX"));

    [Fact]
    public void IsKnown_says_no_to_null() =>
        SunoStemDefaults.IsKnown(null).Should().BeFalse();
}

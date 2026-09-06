using System;
using System.Text;
using CodeBrix.Audio.Playback.Suno;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Covers <see cref="SunoTitleCodec"/>, the lossy transform a stems export applies to a song title
/// on its way into a track-name meta event. The expected bytes here were measured against real
/// exports; no corpus content is committed.
/// </summary>
public class SunoTitleCodecTests
{
    [Fact]
    public void encode_leaves_plain_text_unchanged() =>
        SunoTitleCodec.Encode("Fading Breath (Drums)").Should()
            .Equal(Encoding.ASCII.GetBytes("Fading Breath (Drums)"));

    [Fact]
    public void encode_writes_one_byte_per_utf16_code_unit()
    {
        //Arrange - one astral character is two code units, one basic-plane character is one
        const string title = "\U0001F344⛪";

        //Act
        var encoded = SunoTitleCodec.Encode(title);

        //Assert
        encoded.Length.Should().Be(3);
        title.Length.Should().Be(3);
    }

    [Fact]
    public void encode_writes_the_low_byte_of_the_code_point_for_both_halves_of_a_surrogate_pair()
    {
        //Arrange - U+1F344 is D83C DF44; the low byte of the code point is 0x44, not 0x3C
        const string title = "\U0001F344";

        //Act
        var encoded = SunoTitleCodec.Encode(title);

        //Assert
        encoded.Should().Equal([(byte)0x44, 0x44]);
    }

    [Fact]
    public void encode_reproduces_a_measured_export_name()
    {
        //Arrange - the mushroom, gem and church title, as one export wrote it
        const string title = "\U0001F344\U0001F48E⛪ Mycelium Dream Cathedral (Bass)";
        var expected = new byte[] { 0x44, 0x44, 0x8E, 0x8E, 0xEA, 0x20 };

        //Act
        var encoded = SunoTitleCodec.Encode(title);

        //Assert
        encoded[..6].Should().Equal(expected);
        Encoding.ASCII.GetString(encoded, 6, encoded.Length - 6).Should().Be("Mycelium Dream Cathedral (Bass)");
    }

    [Fact]
    public void encode_reproduces_a_measured_export_name_with_a_flag()
    {
        //Arrange - the drum and flag title; a flag is two regional indicators, each a surrogate pair
        const string title = "\U0001FA98\U0001F1F0\U0001F1EA In the Hakunaverse (Synth)";
        var expected = new byte[] { 0x98, 0x98, 0xF0, 0xF0, 0xEA, 0xEA, 0x20 };

        //Act
        var encoded = SunoTitleCodec.Encode(title);

        //Assert
        encoded[..7].Should().Equal(expected);
    }

    [Fact]
    public void matches_accepts_the_title_that_produced_the_name()
    {
        //Arrange
        const string name = "\U0001F344\U0001F48E⛪ Fake Song (Drums)";
        var raw = SunoTitleCodec.Encode(name);

        //Act
        var matches = SunoTitleCodec.Matches(raw, name);

        //Assert
        matches.Should().BeTrue();
    }

    [Fact]
    public void matches_rejects_a_different_title()
    {
        //Arrange
        var raw = SunoTitleCodec.Encode("Fake Song (Drums)");

        //Act
        var matches = SunoTitleCodec.Matches(raw, "Other Song (Drums)");

        //Assert
        matches.Should().BeFalse();
    }

    [Fact]
    public void matches_rejects_a_title_of_a_different_length()
    {
        //Arrange
        var raw = SunoTitleCodec.Encode("Fake Song (Drums)");

        //Act
        var matches = SunoTitleCodec.Matches(raw, "Fake Song");

        //Assert
        matches.Should().BeFalse();
    }

    [Fact]
    public void matches_rejects_two_titles_whose_low_bytes_agree_but_whose_lengths_do_not()
    {
        //Arrange - one astral character encodes to two bytes, so a one-character title cannot match
        var raw = SunoTitleCodec.Encode("\U0001F344");

        //Act
        var matches = SunoTitleCodec.Matches(raw, "D");

        //Assert
        matches.Should().BeFalse();
    }

    [Fact]
    public void starts_with_matches_the_song_title_inside_a_stem_track_name()
    {
        //Arrange
        const string title = "\U0001F344\U0001F48E⛪ Fake Song";
        var raw = SunoTitleCodec.Encode(title + " (Drums)");

        //Act
        var startsWith = SunoTitleCodec.StartsWith(raw, title);

        //Assert
        startsWith.Should().BeTrue();
    }

    [Fact]
    public void starts_with_rejects_a_title_that_is_not_the_prefix()
    {
        //Arrange
        var raw = SunoTitleCodec.Encode("Fake Song (Drums)");

        //Act
        var startsWith = SunoTitleCodec.StartsWith(raw, "Other Song");

        //Assert
        startsWith.Should().BeFalse();
    }

    [Fact]
    public void starts_with_rejects_a_title_longer_than_the_name()
    {
        //Arrange
        var raw = SunoTitleCodec.Encode("Fake");

        //Act
        var startsWith = SunoTitleCodec.StartsWith(raw, "Fake Song");

        //Assert
        startsWith.Should().BeFalse();
    }

    [Fact]
    public void a_lone_high_surrogate_encodes_as_its_own_low_byte()
    {
        //Arrange - an unpaired surrogate is not a code point; the code unit is all there is
        var title = "\uD83C";

        //Act
        var encoded = SunoTitleCodec.Encode(title);

        //Assert
        encoded.Should().Equal([(byte)0x3C]);
        SunoTitleCodec.Matches(encoded, title).Should().BeTrue();
    }

    [Theory]
    [InlineData("Fading Breath", false)]
    [InlineData("Bearded Gray-haired Wonder (Bass)", false)]
    [InlineData("\U0001F344 Mycelium", true)]
    [InlineData("Nöldeke", false)]
    [InlineData("⛪", true)]
    public void is_lossy_says_whether_the_name_will_survive(string title, bool expected) =>
        SunoTitleCodec.IsLossy(title).Should().Be(expected);

    [Fact]
    public void to_best_effort_string_reads_the_bytes_back_one_character_each() =>
        SunoTitleCodec.ToBestEffortString(SunoTitleCodec.Encode("Fake Song")).Should().Be("Fake Song");

    [Fact]
    public void encode_rejects_null() =>
        new Action(() => SunoTitleCodec.Encode(null)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void matches_rejects_a_null_candidate() =>
        new Action(() => SunoTitleCodec.Matches([1, 2, 3], null)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void matches_treats_a_null_name_as_empty() =>
        SunoTitleCodec.Matches((byte[])null, string.Empty).Should().BeTrue();
}

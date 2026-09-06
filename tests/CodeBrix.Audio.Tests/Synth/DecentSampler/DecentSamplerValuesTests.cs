using CodeBrix.Audio.Synth.DecentSampler.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers the value conventions the Decent Sampler format uses throughout: volumes in linear or
/// decibel form, note numbers and note names, percentages, booleans, tag lists, translation tables and
/// note ranges.
/// </summary>
public class DecentSamplerValuesTests
{
    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("1", 1.0)]
    [InlineData("  0.25  ", 0.25)]
    public void TryVolume_reads_a_linear_volume(string text, double expected)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryVolume(text, out var linear, out var wasDecibels);

        //Assert
        read.Should().BeTrue();
        wasDecibels.Should().BeFalse();
        linear.Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("0dB", 1.0)]
    [InlineData("6dB", 1.9952623149688795)]
    [InlineData("-6 dB", 0.5011872336272722)]
    [InlineData("-20.142255783081dB", 0.09837555847398102)]
    public void TryVolume_reads_a_decibel_volume(string text, double expected)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryVolume(text, out var linear, out var wasDecibels);

        //Assert
        read.Should().BeTrue();
        wasDecibels.Should().BeTrue();
        linear.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void TryVolume_rejects_text_that_is_not_a_number() =>
        DecentSamplerValues.TryVolume("O.2", out _, out _).Should().BeFalse();

    [Fact]
    public void TryDecayRate_makes_a_positive_decibel_rate_negative()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryDecayRate("3dB", out var value, out var inDecibels);

        //Assert
        read.Should().BeTrue();
        inDecibels.Should().BeTrue();
        value.Should().Be(-3.0);
    }

    [Fact]
    public void TryDecayRate_keeps_a_linear_rate_as_written()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryDecayRate("0.3", out var value, out var inDecibels);

        //Assert
        read.Should().BeTrue();
        inDecibels.Should().BeFalse();
        value.Should().Be(0.3);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("0", 0)]
    [InlineData("127", 127)]
    [InlineData("C3", 60)]
    [InlineData("c3", 60)]
    [InlineData("C4", 72)]
    [InlineData("C5", 84)]
    [InlineData("C-1", 12)]
    [InlineData("C0", 24)]
    [InlineData("A4", 81)]
    [InlineData("C#4", 73)]
    [InlineData("Db4", 73)]
    [InlineData("D3", 62)]
    [InlineData("Bb4", 82)]
    public void TryNote_reads_numbers_and_names(string text, int expected)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryNote(text, out var note);

        //Assert
        read.Should().BeTrue();
        note.Should().Be(expected);
    }

    [Theory]
    [InlineData("128")]
    [InlineData("-1")]
    [InlineData("C9")]
    [InlineData("C-3")]
    [InlineData("H4")]
    [InlineData("B")]
    [InlineData("")]
    public void TryNote_rejects_anything_else(string text) =>
        DecentSamplerValues.TryNote(text, out _).Should().BeFalse();

    [Fact]
    public void middle_c_is_c3_under_the_yamaha_convention_the_reference_player_uses() =>
        DecentSamplerValues.NoteName(60).Should().Be("C3");

    [Theory]
    [InlineData("-6db")]
    [InlineData("-6DB")]
    [InlineData("0")]
    [InlineData("-6")]
    public void TryVolume_reads_anything_at_or_below_zero_as_silence(string text)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryVolume(text, out var linear, out _);

        //Assert
        read.Should().BeTrue();
        linear.Should().Be(0.0);
    }

    [Fact]
    public void TryVolume_clamps_a_linear_volume_to_sixteen()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryVolume("40", out var linear, out var wasDecibels);

        //Assert
        read.Should().BeTrue();
        wasDecibels.Should().BeFalse();
        linear.Should().Be(16.0);
    }

    [Fact]
    public void TryVolume_reads_a_bare_number_as_a_linear_gain_not_decibels()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryVolume("3", out var linear, out var wasDecibels);

        //Assert
        read.Should().BeTrue();
        wasDecibels.Should().BeFalse();
        linear.Should().Be(3.0);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void TryBool_accepts_both_spellings(string text, bool expected)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryBool(text, out var value);

        //Assert
        read.Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("50", 0.5)]
    [InlineData("50%", 0.5)]
    [InlineData("100", 1.0)]
    public void TryPercent_divides_by_one_hundred(string text, double expected)
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryPercent(text, out var fraction);

        //Assert
        read.Should().BeTrue();
        fraction.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void TagList_trims_and_drops_blanks() =>
        DecentSamplerValues.TagList(" rt , mic1 ,, ").Should().Equal("rt", "mic1");

    [Fact]
    public void TagList_of_nothing_is_empty() => DecentSamplerValues.TagList(null).Should().BeEmpty();

    [Fact]
    public void TryTranslationTable_reads_every_point()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryTranslationTable("0,33;0.5,1100;1.0001,22000", out var points);

        //Assert
        read.Should().BeTrue();
        points.Should().HaveCount(3);
        points[0].Input.Should().Be(0.0);
        points[0].Output.Should().Be(33.0);
        points[2].Input.Should().Be(1.0001);
        points[2].Output.Should().Be(22000.0);
    }

    [Theory]
    [InlineData("0,0")]
    [InlineData("0,0;1")]
    [InlineData("nonsense")]
    public void TryTranslationTable_needs_two_well_formed_points(string text) =>
        DecentSamplerValues.TryTranslationTable(text, out _).Should().BeFalse();

    [Fact]
    public void TryNoteRange_reads_a_single_note()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryNoteRange("11", out var low, out var high);

        //Assert
        read.Should().BeTrue();
        low.Should().Be(11);
        high.Should().Be(11);
    }

    [Fact]
    public void TryNoteRange_reads_a_dashed_range()
    {
        //Arrange
        //Act
        var read = DecentSamplerValues.TryNoteRange("24-35", out var low, out var high);

        //Assert
        read.Should().BeTrue();
        low.Should().Be(24);
        high.Should().Be(35);
    }

    [Fact]
    public void Color_drops_a_leading_hash_and_surrounding_space() =>
        DecentSamplerValues.Color(" #FF2C365E ").Should().Be("FF2C365E");

    [Fact]
    public void decibels_and_linear_round_trip() =>
        DecentSamplerValues.LinearToDecibels(DecentSamplerValues.DecibelsToLinear(-6.0))
            .Should().BeApproximately(-6.0, 1e-9);
}

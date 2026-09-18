using System;
using CodeBrix.Audio.Abc;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Abc;

/// <summary>
/// The exact-rational duration every length and position in the abc reader is carried as. These
/// tests fence the one property everything else leans on: arithmetic on durations stays exact, and
/// rounding happens once, at the tick.
/// </summary>
public class AbcDurationTests
{
    [Fact]
    public void a_duration_is_reduced_when_it_is_made()
    {
        //Arrange
        //Act
        var duration = new AbcDuration(6, 8);

        //Assert
        duration.Numerator.Should().Be(3);
        duration.Denominator.Should().Be(4);
        duration.ToString().Should().Be("3/4");
    }

    [Fact]
    public void a_negative_denominator_moves_the_sign_to_the_numerator()
    {
        //Arrange
        //Act
        var duration = new AbcDuration(1, -4);

        //Assert
        duration.Numerator.Should().Be(-1);
        duration.Denominator.Should().Be(4);
    }

    [Fact]
    public void a_denominator_of_zero_is_refused()
    {
        //Arrange
        //Act
        Action act = () => new AbcDuration(1, 0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void zero_and_whole_are_what_they_say()
    {
        //Arrange
        //Act
        var zero = AbcDuration.Zero;
        var whole = AbcDuration.Whole;

        //Assert
        zero.IsZero.Should().BeTrue();
        zero.Value.Should().Be(0.0);
        whole.Numerator.Should().Be(1);
        whole.Denominator.Should().Be(1);
    }

    [Fact]
    public void adding_thirds_three_times_gives_exactly_one()
    {
        //Arrange - the case a double gets wrong.
        var third = new AbcDuration(1, 3);

        //Act
        var total = third + third + third;

        //Assert
        total.Should().Be(AbcDuration.Whole);
    }

    [Fact]
    public void arithmetic_stays_exact()
    {
        //Arrange
        var half = new AbcDuration(1, 2);
        var third = new AbcDuration(1, 3);

        //Act
        var sum = half + third;
        var difference = half - third;
        var product = half * third;
        var quotient = half / third;

        //Assert
        sum.Should().Be(new AbcDuration(5, 6));
        difference.Should().Be(new AbcDuration(1, 6));
        product.Should().Be(new AbcDuration(1, 6));
        quotient.Should().Be(new AbcDuration(3, 2));
    }

    [Fact]
    public void dividing_by_a_zero_duration_is_refused()
    {
        //Arrange
        var half = new AbcDuration(1, 2);

        //Act
        Action act = () => { var ignored = half / AbcDuration.Zero; };

        //Assert
        act.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void dividing_by_zero_is_refused()
    {
        //Arrange
        var half = new AbcDuration(1, 2);

        //Act
        Action act = () => { var ignored = half / 0L; };

        //Assert
        act.Should().Throw<DivideByZeroException>();
    }

    [Theory]
    [InlineData(1, 1, 1920)]
    [InlineData(1, 2, 960)]
    [InlineData(1, 4, 480)]
    [InlineData(1, 8, 240)]
    [InlineData(1, 16, 120)]
    [InlineData(1, 12, 160)]
    [InlineData(1, 64, 30)]
    public void a_duration_becomes_the_ticks_it_should(long numerator, long denominator, long ticks)
    {
        //Arrange
        var duration = new AbcDuration(numerator, denominator);

        //Act
        long actual = duration.ToTicks(480);

        //Assert
        actual.Should().Be(ticks);
    }

    [Fact]
    public void ticks_round_to_the_nearest_rather_than_truncating()
    {
        //Arrange - a septuplet eighth at 480 ticks a quarter note is 68 and four sevenths.
        var duration = new AbcDuration(1, 28);

        //Act
        long ticks = duration.ToTicks(480);

        //Assert
        ticks.Should().Be(69);
    }

    [Fact]
    public void a_half_tick_rounds_away_from_zero()
    {
        //Arrange
        var duration = new AbcDuration(1, 128);

        //Act
        long ticks = duration.ToTicks(16);

        //Assert
        // 1/128 of a whole note at 16 ticks a quarter note is exactly half a tick.
        ticks.Should().Be(1);
    }

    [Fact]
    public void a_negative_duration_rounds_away_from_zero_too()
    {
        //Arrange
        var duration = new AbcDuration(-1, 128);

        //Act
        long ticks = duration.ToTicks(16);

        //Assert
        ticks.Should().Be(-1);
    }

    [Theory]
    [InlineData(1, 4, true)]
    [InlineData(1, 12, true)]
    [InlineData(1, 28, false)]
    [InlineData(1, 5, true)]
    [InlineData(1, 7, false)]
    public void whole_ticks_are_recognised(long numerator, long denominator, bool whole)
    {
        //Arrange
        var duration = new AbcDuration(numerator, denominator);

        //Act
        bool actual = duration.IsWholeTicks(480);

        //Assert
        actual.Should().Be(whole);
    }

    [Fact]
    public void a_resolution_of_zero_is_refused()
    {
        //Arrange
        var duration = AbcDuration.Whole;

        //Act
        Action act = () => duration.ToTicks(0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void durations_compare_by_length()
    {
        //Arrange
        var third = new AbcDuration(1, 3);
        var quarter = new AbcDuration(1, 4);

        //Act
        //Assert
        (third > quarter).Should().BeTrue();
        (quarter < third).Should().BeTrue();
        (third >= new AbcDuration(2, 6)).Should().BeTrue();
        (third <= new AbcDuration(2, 6)).Should().BeTrue();
        (third == new AbcDuration(2, 6)).Should().BeTrue();
        (third != quarter).Should().BeTrue();
        third.CompareTo(quarter).Should().BeGreaterThan(0);
    }

    [Fact]
    public void equal_durations_hash_the_same_however_they_were_written()
    {
        //Arrange
        var written = new AbcDuration(2, 6);
        var reduced = new AbcDuration(1, 3);

        //Act
        //Assert
        written.Equals(reduced).Should().BeTrue();
        written.Equals((object)reduced).Should().BeTrue();
        written.GetHashCode().Should().Be(reduced.GetHashCode());
        written.Equals("1/3").Should().BeFalse();
    }

    [Fact]
    public void a_duration_can_be_built_from_whole_notes()
    {
        //Arrange
        //Act
        var duration = AbcDuration.FromWholeNotes(3);

        //Assert
        duration.Should().Be(new AbcDuration(3, 1));
        duration.ToTicks(480).Should().Be(5760);
    }

    [Fact]
    public void a_position_accumulated_from_thirds_never_drifts()
    {
        //Arrange - twelve triplet eighths make exactly two half notes, at every resolution.
        var triplet = new AbcDuration(1, 12);
        var position = AbcDuration.Zero;

        //Act
        for (int i = 0; i < 12; i++)
        {
            position += triplet;
        }

        //Assert
        position.Should().Be(AbcDuration.Whole);
        position.ToTicks(480).Should().Be(1920);
        position.ToTicks(96).Should().Be(384);
    }
}

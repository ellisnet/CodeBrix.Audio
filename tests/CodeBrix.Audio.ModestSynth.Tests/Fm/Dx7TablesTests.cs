using System;
using CodeBrix.Audio.ModestSynth.Fm;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The rate, level, detune, velocity and rate-scaling laws the six-operator oscillator is built
/// from, pinned at the points that matter.
/// </summary>
/// <remarks>
/// The RATE SCALE, the DETUNE LAW and the VELOCITY TABLE are now measured against the reference
/// player (round 2 item 27 and round 3 item 42) and the numbers below are the reference's own. The
/// level scale and the rate scaling remain reconstructions, and for those what these tests protect is
/// STABILITY rather than fidelity: a change to either should be a deliberate one.
/// </remarks>
public class Dx7TablesTests
{
    [Theory]
    [InlineData(20, 1.991)]
    [InlineData(50, 0.078)]
    public void The_rate_scale_reproduces_the_reference_at_its_two_resolved_points(
        int rate, double measuredSeconds)
    {
        //Assert
        // MEASURED (round 2, item 27): rate 20 reached 99 % of its final level in 1.991 s and rate 50
        // in 0.078 s. The full sweep is a little longer than the 99 % time, so the tolerance is
        // generous in one direction; what the two points pin is the SLOPE, 5.5 rate units per halving.
        Dx7Tables.FullSweepSeconds(rate).Should().BeInRange(measuredSeconds * 0.6, measuredSeconds * 2.0);
    }

    [Fact]
    public void Thirty_rate_units_higher_is_forty_five_times_faster()
    {
        //Act
        // MEASURED: from rate 20 to rate 50 the reference's time fell by a factor of 45.
        double slow = Dx7Tables.FullSweepSeconds(20);
        double fast = Dx7Tables.FullSweepSeconds(50);

        //Assert
        (slow / fast).Should().BeApproximately(Math.Pow(2.0, 30.0 / Dx7Tables.RateUnitsPerHalving), 1e-9);
        (slow / fast).Should().BeApproximately(43.5, 2.0);
    }

    [Fact]
    public void Rate_zero_crawls_rather_than_holding()
    {
        //Assert
        // MEASURED (round 2, item 27): rate 0 reached 99 % of its final level in 5.481 s, where the
        // format's own description promises a hold. The halving law would have given it half a minute.
        Dx7Tables.FullSweepSeconds(0).Should().BeApproximately(5.5, 0.01);
        Dx7Tables.LevelUnitsPerSecond(0).Should().BeApproximately(99.0 / 5.5, 1e-9);
    }

    [Fact]
    public void LevelUnitsPerSecond_is_the_whole_range_divided_by_the_sweep_time() =>
        Dx7Tables.LevelUnitsPerSecond(99)
            .Should().BeApproximately(99.0 / Dx7Tables.FullSweepSecondsAtMaximumRate, 1e-6);

    [Theory]
    [InlineData(99, 1.0)]
    [InlineData(91, 0.5)]
    [InlineData(83, 0.25)]
    [InlineData(0, 0.0)]
    [InlineData(-4, 0.0)]
    public void Eight_level_units_halve_the_amplitude(double units, double expected) =>
        Dx7Tables.LevelUnitsToAmplitude(units).Should().BeApproximately(expected, 1e-12);

    [Fact]
    public void One_level_unit_is_three_quarters_of_a_decibel()
    {
        //Act
        double decibels = 20.0 * Math.Log10(Dx7Tables.LevelUnitsToAmplitude(98) /
            Dx7Tables.LevelUnitsToAmplitude(99));

        //Assert
        Math.Abs(decibels).Should().BeApproximately(Dx7Tables.DecibelsPerLevelUnit, 1e-9);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.01)]
    public void AmplitudeToLevelUnits_reverses_LevelUnitsToAmplitude(double amplitude)
    {
        //Act
        double units = Dx7Tables.AmplitudeToLevelUnits(amplitude);

        //Assert
        Dx7Tables.LevelUnitsToAmplitude(units).Should().BeApproximately(amplitude, 1e-12);
    }

    [Theory]
    [InlineData(7, 130.813, 0.818)]
    [InlineData(7, 261.626, 1.270)]
    [InlineData(7, 523.251, 1.917)]
    public void Detune_follows_the_measured_power_law(int detune, double frequency, double expectedHz) =>
        Dx7Tables.DetuneHz(detune, frequency).Should().BeApproximately(expectedHz, 0.04);

    [Theory]
    [InlineData(0, 261.626)]
    [InlineData(3, 0.0)]
    public void Detune_is_nothing_without_both_a_step_and_a_frequency(int detune, double frequency) =>
        Dx7Tables.DetuneHz(detune, frequency).Should().Be(0.0);

    [Fact]
    public void Detune_is_symmetric_and_clamped_at_seven()
    {
        //Assert
        Dx7Tables.DetuneHz(-7, 261.626).Should().BeApproximately(-Dx7Tables.DetuneHz(7, 261.626), 1e-12);
        Dx7Tables.DetuneHz(99, 261.626).Should().Be(Dx7Tables.DetuneHz(7, 261.626));
        Dx7Tables.DetuneHz(-99, 261.626).Should().Be(Dx7Tables.DetuneHz(-7, 261.626));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 64)]
    [InlineData(0, 127)]
    public void Velocity_sensitivity_zero_ignores_velocity(int sensitivity, int velocity) =>
        Dx7Tables.VelocityScale(sensitivity, velocity).Should().Be(1.0);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Velocity_ninety_six_is_the_neutral_point_at_every_sensitivity(int sensitivity) =>
        Dx7Tables.VelocityScale(sensitivity, Dx7Tables.NeutralVelocity).Should().BeApproximately(1.0, 1e-12);

    [Theory]
    [InlineData(3, 1, -33.5)]
    [InlineData(3, 32, -10.2)]
    [InlineData(3, 64, -4.5)]
    [InlineData(3, 96, 0.0)]
    [InlineData(3, 127, 2.2)]
    [InlineData(7, 32, -23.7)]
    [InlineData(7, 64, -10.6)]
    [InlineData(7, 127, 5.3)]
    public void The_velocity_table_matches_the_reference(int sensitivity, int velocity, double expectedDb)
    {
        //Act
        // MEASURED (round 2, item 27): the offset from the sensitivity-0 case, in decibels.
        double decibels = 20.0 * Math.Log10(Dx7Tables.VelocityScale(sensitivity, velocity));

        //Assert
        decibels.Should().BeApproximately(expectedDb, 0.6);
    }

    [Fact]
    public void Velocity_sensitivity_seven_silences_the_softest_note()
    {
        //Act
        // MEASURED: "VS = 7, velocity 1" was recorded as silence.
        double decibels = 20.0 * Math.Log10(Dx7Tables.VelocityScale(7, 1));

        //Assert
        decibels.Should().BeLessThan(-70.0);
    }

    [Fact]
    public void Above_the_neutral_point_velocity_sensitivity_pushes_the_operator_up()
    {
        //Assert - MEASURED: +2.2 dB at sensitivity 3 and +5.3 dB at sensitivity 7, at velocity 127.
        Dx7Tables.VelocityScale(3, 127).Should().BeGreaterThan(1.0);
        Dx7Tables.VelocityScale(7, 127).Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void Velocity_scaling_rises_with_velocity()
    {
        //Act
        double soft = Dx7Tables.VelocityScale(4, 20);
        double medium = Dx7Tables.VelocityScale(4, 64);
        double hard = Dx7Tables.VelocityScale(4, 110);

        //Assert
        soft.Should().BeLessThan(medium);
        medium.Should().BeLessThan(hard);
        hard.Should().BeLessThan(Dx7Tables.VelocityScale(4, 127));
    }

    [Fact]
    public void Rate_scaling_is_off_at_zero_and_climbs_with_the_note()
    {
        //Assert
        Dx7Tables.RateScalingUnits(0, 127).Should().Be(0.0);
        Dx7Tables.RateScalingUnits(7, 0).Should().Be(0.0);
        Dx7Tables.RateScalingUnits(7, 127).Should().BeApproximately(Dx7Tables.RateUnitsAtFullRateScaling, 1e-12);
        Dx7Tables.RateScalingUnits(7, 60).Should().BeLessThan(Dx7Tables.RateScalingUnits(7, 96));
    }

    [Theory]
    [InlineData(440.0, 69)]
    [InlineData(261.625565, 60)]
    [InlineData(27.5, 21)]
    [InlineData(0.0, 0)]
    [InlineData(-5.0, 0)]
    [InlineData(1000000.0, 127)]
    public void FrequencyToMidiNote_uses_equal_temperament_with_a_at_four_forty(double frequency, int expected) =>
        Dx7Tables.FrequencyToMidiNote(frequency).Should().Be(expected);
}

using System;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Patch;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The two per-operator envelopes: the ADSR measured in seconds, and the four-stage rate and level
/// envelope measured on the hardware's 0-99 scales.
/// </summary>
/// <remarks>
/// <para>
/// Every measurement here is taken from the RENDERED SIGNAL rather than from the envelope object,
/// because what a consumer hears is the only thing worth pinning. A single carrier at a known
/// pitch is rendered and its peak is read a window at a time, so the peak IS the envelope times
/// the output gain.
/// </para>
/// <para>
/// The stage times come from <see cref="Dx7Tables" />: a full 0-to-99 sweep takes 6 ms at rate 99
/// and doubles every 6.24 rate units down from there, so rate 60 takes 0.455 s and a 48-unit
/// stretch of it takes 0.221 s. Those are the numbers these tests demand.
/// </para>
/// </remarks>
public class Fm6OpEnvelopes
{
    private const int Rate = FmSignal.SampleRate;
    private const double Gain = Fm6OpOscillator.ReferenceOutputGain;
    private const double Pitch = 1000.0;

    [Fact]
    public void The_adsr_rises_decays_and_holds_where_it_was_told_to()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.Attack = 0.1;
        settings.Decay = 0.2;
        settings.Sustain = 0.5;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate];
        oscillator.Render(block);

        //Assert
        FmSignal.PeakAround(block, (int)(0.05 * Rate), 24).Should().BeApproximately(0.5 * Gain, 0.01);
        FmSignal.PeakAround(block, (int)(0.1 * Rate), 24).Should().BeApproximately(Gain, 0.01);
        FmSignal.PeakAround(block, (int)(0.2 * Rate), 24).Should().BeApproximately(0.75 * Gain, 0.01);
        FmSignal.PeakAround(block, (int)(0.4 * Rate), 240).Should().BeApproximately(0.5 * Gain, 0.02);
        FmSignal.PeakAround(block, (int)(0.9 * Rate), 240).Should().BeApproximately(0.5 * Gain, 0.02);
    }

    [Fact]
    public void An_adsr_release_takes_the_time_it_was_given()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.Sustain = 0.5;
        settings.Release = 0.2;

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[Rate / 2]);
        oscillator.NoteOff();
        float[] released = new float[Rate / 2];
        oscillator.Render(released);

        //Assert
        FmSignal.PeakAround(released, (int)(0.1 * Rate), 24).Should().BeApproximately(0.25 * Gain, 0.01);
        FmSignal.TimeToFallBelow(released, 0.001, 480).Should().BeApproximately(0.2, 0.02);
        oscillator.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void A_release_of_zero_stops_the_operator_at_once()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        oscillator.GetOperator(1).Release = 0.0;

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[512]);
        oscillator.NoteOff();
        float[] released = new float[512];
        oscillator.Render(released);

        //Assert
        FmSignal.PeakAround(released, 256, 256).Should().Be(0.0);
        oscillator.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void The_release_sentinel_hands_the_ending_to_the_group_envelope()
    {
        //Arrange - -1 is the default, and it means "the group decides when this note ends".
        Fm6OpOscillator oscillator = Carrier();
        oscillator.GetOperator(1).Release.Should().Be(ModestFmOperator.OuterEnvelopeSentinel);
        oscillator.UsesOuterRelease.Should().BeTrue();
        oscillator.OperatorUsesOuterRelease(1).Should().BeTrue();

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[512]);
        oscillator.NoteOff();
        float[] released = new float[Rate];
        oscillator.Render(released);

        //Assert
        FmSignal.PeakAround(released, Rate - 256, 240).Should().BeApproximately(Gain, 0.02);
        oscillator.IsFinished.Should().BeFalse();
    }

    [Fact]
    public void The_attack_sentinel_gives_the_operator_no_envelope_at_all()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.Attack = -1.0;
        settings.Decay = 0.1;
        settings.Sustain = 0.0;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate / 2];
        oscillator.Render(block);

        //Assert - the decay and sustain are ignored along with the attack.
        FmSignal.PeakAround(block, 240, 240).Should().BeApproximately(Gain, 0.02);
        FmSignal.PeakAround(block, Rate / 4, 240).Should().BeApproximately(Gain, 0.02);
        oscillator.IsFinished.Should().BeFalse();
    }

    [Fact]
    public void A_negative_release_of_any_size_is_the_sentinel()
    {
        //Arrange
        ModestFmOperator settings = new ModestFmOperator(1);

        //Act
        settings.Release = -0.25;

        //Assert
        settings.Release.Should().Be(ModestFmOperator.OuterEnvelopeSentinel);
    }

    [Fact]
    public void A_four_stage_attack_takes_the_time_its_rate_says()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 60;
        settings.EgLevel1 = 99;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate];
        oscillator.Render(block);
        double reached = FmSignal.TimeToRiseAbove(block, 0.99 * Gain, 480);

        //Assert
        reached.Should().BeApproximately(Dx7Tables.FullSweepSeconds(60), 0.02);
    }

    [Fact]
    public void A_four_stage_decay_walks_the_level_scale_at_the_rate_it_was_given()
    {
        //Arrange - 99 units down to 51 at rate 30, which the measured scale makes a slow three
        //quarters of a second for the whole range.
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 99;
        settings.EgLevel1 = 99;
        settings.EgRate2 = 30;
        settings.EgLevel2 = 51;
        settings.EgRate3 = 0;
        settings.EgLevel3 = 51;

        double unitsPerSecond = Dx7Tables.LevelUnitsPerSecond(30);
        double attack = Dx7Tables.FullSweepSeconds(99);
        double halfway = attack + (24.0 / unitsPerSecond);
        double arrival = attack + (48.0 / unitsPerSecond);

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate];
        oscillator.Render(block);

        //Assert
        FmSignal.PeakAround(block, (int)(halfway * Rate), 24)
            .Should().BeApproximately(Dx7Tables.LevelUnitsToAmplitude(75) * Gain, 0.002);
        FmSignal.PeakAround(block, (int)((arrival + 0.05) * Rate), 24)
            .Should().BeApproximately(Dx7Tables.LevelUnitsToAmplitude(51) * Gain, 0.001);
    }

    [Fact]
    public void Rate_zero_holds_the_level_for_as_long_as_the_key_is_down()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 99;
        settings.EgRate2 = 99;
        settings.EgLevel2 = 71;
        settings.EgRate3 = 0;
        settings.EgLevel3 = 71;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[2 * Rate];
        oscillator.Render(block);

        //Assert
        double expected = Dx7Tables.LevelUnitsToAmplitude(71) * Gain;
        FmSignal.PeakAround(block, Rate / 2, 240).Should().BeApproximately(expected, 0.002);
        FmSignal.PeakAround(block, (2 * Rate) - 256, 240).Should().BeApproximately(expected, 0.002);
    }

    [Fact]
    public void A_four_stage_release_falls_to_L4_at_R4_and_then_the_voice_is_done()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 99;
        settings.EgRate2 = 99;
        settings.EgRate3 = 0;
        settings.EgRate4 = 30;
        settings.EgLevel4 = 0;

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[Rate / 10]);
        oscillator.NoteOff();
        float[] released = new float[Rate / 4];
        oscillator.Render(released);
        bool finishedEarly = oscillator.IsFinished;
        oscillator.Render(new float[Rate]);

        //Assert - rate 30 sweeps the whole range in three quarters of a second, so a quarter of a
        //second is not enough and a further second is.
        finishedEarly.Should().BeFalse();
        oscillator.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void A_four_stage_release_that_stops_above_silence_never_finishes()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate4 = 99;
        settings.EgLevel4 = 40;

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[Rate / 10]);
        oscillator.NoteOff();
        float[] released = new float[Rate / 2];
        oscillator.Render(released);

        //Assert
        oscillator.IsFinished.Should().BeFalse();
        FmSignal.PeakAround(released, Rate / 4, 240)
            .Should().BeApproximately(Dx7Tables.LevelUnitsToAmplitude(40) * Gain, 0.001);
    }

    [Fact]
    public void The_four_stage_envelope_ignores_the_adsr_attributes()
    {
        //Arrange - an instant ADSR attack next to a slow four-stage one; the slow one must win.
        Fm6OpOscillator oscillator = Carrier();
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.Attack = 0.0;
        settings.Decay = 0.0;
        settings.Sustain = 1.0;
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 30;
        settings.EgLevel1 = 99;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate];
        oscillator.Render(block);

        //Assert
        FmSignal.PeakAround(block, 2400, 240).Should().BeLessThan(0.2 * Gain);
        FmSignal.TimeToRiseAbove(block, 0.99 * Gain, 480)
            .Should().BeApproximately(Dx7Tables.FullSweepSeconds(30), 0.05);
    }

    [Fact]
    public void The_default_four_stage_envelope_is_the_flat_one_the_format_documents()
    {
        //Arrange
        ModestFmOperator settings = new ModestFmOperator(1);

        //Assert
        settings.EgRate1.Should().Be(99);
        settings.EgRate2.Should().Be(99);
        settings.EgRate3.Should().Be(0);
        settings.EgRate4.Should().Be(99);
        settings.EgLevel1.Should().Be(99);
        settings.EgLevel2.Should().Be(99);
        settings.EgLevel3.Should().Be(99);
        settings.EgLevel4.Should().Be(0);
    }

    [Fact]
    public void The_default_four_stage_envelope_is_instant_on_flat_and_instant_off()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        oscillator.GetOperator(1).EnvelopeType = ModestFmEnvelopeType.Dx7;

        //Act
        oscillator.Reset(0.0);
        float[] held = new float[Rate];
        oscillator.Render(held);
        oscillator.NoteOff();
        float[] released = new float[Rate / 10];
        oscillator.Render(released);

        //Assert
        FmSignal.TimeToRiseAbove(held, 0.99 * Gain, 480).Should().BeLessThan(0.02);
        FmSignal.PeakAround(held, Rate - 256, 240).Should().BeApproximately(Gain, 0.01);
        FmSignal.TimeToFallBelow(released, 0.001, 480).Should().BeLessThan(0.02);
        oscillator.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void Rate_scaling_speeds_the_envelope_up_towards_the_top_of_the_keyboard()
    {
        //Arrange
        Fm6OpOscillator low = Carrier();
        Fm6OpOscillator high = Carrier();
        low.SetFrequency(55.0);
        high.SetFrequency(3520.0);

        foreach (Fm6OpOscillator oscillator in new[] { low, high })
        {
            oscillator.RateScaling = 7;
            ModestFmOperator settings = oscillator.GetOperator(1);
            settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
            settings.EgRate1 = 20;
            settings.EgLevel1 = 99;
        }

        //Act
        low.Reset(0.0);
        high.Reset(0.0);
        float[] lowBlock = new float[2 * Rate];
        float[] highBlock = new float[2 * Rate];
        low.Render(lowBlock);
        high.Render(highBlock);

        double lowRise = FmSignal.TimeToRiseAbove(lowBlock, 0.9 * Gain, 2400);
        double highRise = FmSignal.TimeToRiseAbove(highBlock, 0.9 * Gain, 2400);

        //Assert
        highRise.Should().BeLessThan(0.4);
        lowRise.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Rate_scaling_leaves_a_rate_of_zero_crawling_at_its_own_speed()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();
        oscillator.SetFrequency(3520.0);
        oscillator.RateScaling = 7;
        ModestFmOperator settings = oscillator.GetOperator(1);
        settings.EnvelopeType = ModestFmEnvelopeType.Dx7;
        settings.EgRate1 = 99;
        settings.EgRate2 = 99;
        settings.EgLevel2 = 71;
        settings.EgRate3 = 0;
        settings.EgLevel3 = 0;

        //Act
        oscillator.Reset(0.0);
        float[] block = new float[Rate];
        oscillator.Render(block);

        //Assert
        // MEASURED (round 2, item 27): rate 0 does not hold - it crawls, covering the whole level
        // range in about 5.5 s. Rate scaling must not turn that crawl into a race: a second in, the
        // level has fallen from 71 by one second's worth of the crawl and no more.
        var units = 71.0 - (Dx7Tables.MaximumValue / Dx7Tables.FullSweepSecondsAtMinimumRate);
        double expected = Dx7Tables.LevelUnitsToAmplitude(units) * Gain;
        FmSignal.PeakAround(block, Rate - 256, 240).Should().BeApproximately(expected, 0.002);
    }

    [Fact]
    public void A_real_release_takes_the_ending_back_from_the_group()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();

        //Act
        oscillator.GetOperator(1).Release = 0.3;

        //Assert
        oscillator.UsesOuterRelease.Should().BeFalse();
        oscillator.OperatorUsesOuterRelease(1).Should().BeFalse();
    }

    [Fact]
    public void The_four_stage_envelope_has_no_sentinel_because_R4_and_L4_decide()
    {
        //Arrange
        Fm6OpOscillator oscillator = Carrier();

        //Act
        oscillator.GetOperator(1).EnvelopeType = ModestFmEnvelopeType.Dx7;

        //Assert
        oscillator.GetOperator(1).Release.Should().Be(ModestFmOperator.OuterEnvelopeSentinel);
        oscillator.OperatorUsesOuterRelease(1).Should().BeFalse();
        oscillator.UsesOuterRelease.Should().BeFalse();
    }

    [Fact]
    public void A_silent_carrier_does_not_hold_the_note_open()
    {
        //Arrange - algorithm 1 has two carriers and only operator 1 has a level by default.
        Fm6OpOscillator oscillator = Carrier();
        oscillator.GetOperator(1).Release = 0.05;

        //Act
        oscillator.Reset(0.0);
        oscillator.Render(new float[512]);
        oscillator.NoteOff();
        oscillator.Render(new float[Rate / 4]);

        //Assert
        oscillator.Topology.Carriers.Should().Equal(1, 3);
        oscillator.GetOperator(3).Level.Should().Be(0.0);
        oscillator.IsFinished.Should().BeTrue();
    }

    private static Fm6OpOscillator Carrier()
    {
        Fm6OpOscillator oscillator = new Fm6OpOscillator();
        oscillator.SetSampleRate(Rate);
        oscillator.SetFrequency(Pitch);
        return oscillator;
    }
}

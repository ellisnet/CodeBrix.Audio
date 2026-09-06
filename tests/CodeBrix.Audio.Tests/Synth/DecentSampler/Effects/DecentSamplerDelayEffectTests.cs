using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The delay: echo spacing in seconds and in musical time, the stereo offset, and feedback.
/// </summary>
public class DecentSamplerDelayEffectTests
{
    [Fact]
    public void an_echo_lands_one_delay_time_after_the_impulse()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTime=\"0.25\" feedback=\"0.0\" wetLevel=\"1.0\" />");

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be((int)(0.25 * DecentSamplerEffectHarness.SampleRate));
    }

    [Fact]
    public void feedback_repeats_the_echo_at_the_same_spacing()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTime=\"0.1\" feedback=\"0.5\" wetLevel=\"1.0\" />");

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        var spacing = (int)(0.1 * DecentSamplerEffectHarness.SampleRate);
        left[spacing].Should().BeApproximately(1f, 0.001f);
        left[spacing * 2].Should().BeApproximately(0.5f, 0.001f);
        left[spacing * 3].Should().BeApproximately(0.25f, 0.001f);
    }

    [Fact]
    public void wet_level_scales_the_echo_and_leaves_the_dry_signal_alone()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTime=\"0.1\" feedback=\"0.0\" wetLevel=\"0.25\" />");

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        left[0].Should().Be(1f);
        left[(int)(0.1 * DecentSamplerEffectHarness.SampleRate)].Should().BeApproximately(0.25f, 0.001f);
    }

    [Fact]
    public void the_stereo_offset_splits_the_two_channels_by_its_whole_amount()
    {
        //Arrange - the guide's own example: 0.5 s with a 0.02 s offset gives 0.49 s and 0.51 s.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTime=\"0.5\" stereoOffset=\"0.02\" feedback=\"0.0\" wetLevel=\"1.0\" />");

        //Act
        var (left, right) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be((int)(0.49 * DecentSamplerEffectHarness.SampleRate));
        DecentSamplerEffectHarness.FirstPeakAfter(right, 1, 0.5)
            .Should().Be((int)(0.51 * DecentSamplerEffectHarness.SampleRate));
    }

    [Fact]
    public void a_musical_time_delay_is_a_quarter_note_at_a_hundred_and_twenty()
    {
        //Arrange - index 13 is a quarter note, and 120 BPM makes that half a second.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTimeFormat=\"musical_time\" delayTime=\"13\" feedback=\"0.0\" " +
            "wetLevel=\"1.0\" />",
            new TempoSource { BeatsPerMinute = 120.0 });

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be((int)(0.5 * DecentSamplerEffectHarness.SampleRate));
    }

    [Fact]
    public void a_musical_time_delay_follows_the_tempo_source()
    {
        //Arrange
        var tempo = new TempoSource { BeatsPerMinute = 120.0 };
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTimeFormat=\"musical_time\" delayTime=\"13\" feedback=\"0.0\" " +
            "wetLevel=\"1.0\" />",
            tempo);

        //Act - the transport slows to half speed, so a quarter note is twice as long.
        tempo.BeatsPerMinute = 60.0;
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate * 2));

        //Assert
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be(DecentSamplerEffectHarness.SampleRate);
    }

    [Theory]
    [InlineData(10, 0.25)]
    [InlineData(19, 2.0)]
    [InlineData(24, 10.0)]
    public void every_measured_subdivision_lands_where_the_table_says(int index, double seconds)
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"delay\" delayTimeFormat=\"musical_time\" delayTime=\"{index}\" " +
            "feedback=\"0.0\" wetLevel=\"1.0\" />",
            new TempoSource { BeatsPerMinute = 120.0 });

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect,
            DecentSamplerEffectHarness.Impulse((int)((seconds + 0.5) * DecentSamplerEffectHarness.SampleRate)));

        //Assert
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be((int)(seconds * DecentSamplerEffectHarness.SampleRate));
    }

    [Fact]
    public void the_delay_time_is_live()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"delay\" delayTime=\"0.5\" feedback=\"0.0\" wetLevel=\"1.0\" />");

        //Act
        effect.TrySetParameter("FX_DELAY_TIME", 0.25);
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        //Assert
        element.DelayTime.Should().Be(0.25);
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.5)
            .Should().Be((int)(0.25 * DecentSamplerEffectHarness.SampleRate));
    }
}

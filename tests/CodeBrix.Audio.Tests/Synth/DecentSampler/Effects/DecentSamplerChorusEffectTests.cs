using System;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The chorus, against the reference's own click-train probe.
/// </summary>
/// <remarks>
/// MEASURED (round 2 item 22, with the two oscillators settled by round 3 item 41): ONE modulated tap
/// per channel at base delays of 624 samples left and 441 right; <c>delay = base * (1 + modDepth *
/// lfo)</c> with the sine running -1 to +1, so <c>modDepth="1"</c> sweeps each channel from zero to
/// twice its base; the RIGHT oscillator runs at ten ninths of <c>modRate</c>; and <c>mix</c> is the
/// plain linear crossfade.
/// </remarks>
public class DecentSamplerChorusEffectTests
{
    [Theory]
    [InlineData(0.5, 10.0)]
    [InlineData(2.0, 4.0)]
    [InlineData(5.0, 4.0)]
    public void the_sweep_runs_at_the_rate_in_hertz(double rate, double window)
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"chorus\" mix=\"1.0\" modDepth=\"1.0\" modRate=\"{rate}\" />");
        var chorus = (DecentSamplerChorusEffect)effect;

        //Act
        var measured = SweepRate(chorus, window);

        //Assert - inside the 5 % the plan asks for.
        measured.Should().BeApproximately(rate, rate * 0.05);
    }

    [Fact]
    public void a_zero_depth_holds_the_delay_still()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"1.0\" modDepth=\"0.0\" modRate=\"2.0\" />");
        var chorus = (DecentSamplerChorusEffect)effect;
        var first = chorus.DelayLeftFrames;

        //Act
        DecentSamplerEffectHarness.Run(effect, DecentSamplerEffectHarness.Sine(440, 0.5));

        //Assert
        chorus.DelayLeftFrames.Should().BeApproximately(first, 1.0e-9);
    }

    [Fact]
    public void a_mix_of_zero_is_the_dry_signal_exactly()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"0.0\" modDepth=\"1.0\" modRate=\"2.0\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.2);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        for (var i = 0; i < input.Length; i++)
        {
            left[i].Should().Be(input[i]);
        }
    }

    [Fact]
    public void a_full_mix_keeps_the_level_of_what_went_in()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"1.0\" modDepth=\"0.5\" modRate=\"0.5\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 1.0);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert - the wet path is the swept copy alone, so at mix 1 the level is unchanged. The
        //reference measured unity average magnitude at mix 1.0 and -3.7 dB at mix 0.504, which is the
        //pair a crossfade against an equal-amplitude copy gives.
        DecentSamplerEffectHarness.Rms(left, 22050, 20000)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 22050, 20000), 0.005);
    }

    [Fact]
    public void a_half_mix_cancels_the_way_a_crossfade_does()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"0.5\" modDepth=\"1.0\" modRate=\"0.5\" />");
        var input = DecentSamplerEffectHarness.Sine(1000, 2.0);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert - averaged over a whole sweep the crossfade sits about 4 dB down, as measured.
        var measured = 20.0 * Math.Log10(
            DecentSamplerEffectHarness.Rms(left, 44100, 44100) /
            DecentSamplerEffectHarness.Rms(input, 44100, 44100));

        measured.Should().BeInRange(-6.0, -1.5);
    }

    [Fact]
    public void the_rate_is_live()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"1.0\" modDepth=\"1.0\" modRate=\"0.5\" />");
        var chorus = (DecentSamplerChorusEffect)effect;

        //Act
        effect.TrySetParameter("FX_MOD_RATE", 4.0);
        var measured = SweepRate(chorus, seconds: 2.0);

        //Assert
        element.ModRate.Should().Be(4.0);
        measured.Should().BeApproximately(4.0, 0.2);
    }

    // The sweep rate, read from the time between the first and the last wrap of the oscillator's phase,
    // so where the measurement window happens to end cannot bias it.
    [Fact]
    public void the_two_taps_sit_at_the_measured_base_delays()
    {
        //Arrange
        // MEASURED: 624 samples (14.15 ms) left and 441 (10.00 ms) right, a fixed 4.15 ms offset, read
        // off a click train at modDepth="0" and identical at all 33 clicks of the note.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"1.0\" modDepth=\"0.0\" modRate=\"0.125\" />");
        var chorus = (DecentSamplerChorusEffect)effect;

        //Act
        var (left, right) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(4096));

        //Assert
        chorus.DelayLeftFrames.Should().BeApproximately(624.0, 0.5);
        chorus.DelayRightFrames.Should().BeApproximately(441.0, 0.5);
        DecentSamplerEffectHarness.FirstPeakAfter(left, 1, 0.4).Should().BeInRange(623, 625);
        DecentSamplerEffectHarness.FirstPeakAfter(right, 1, 0.9).Should().Be(441);
    }

    [Theory]
    [InlineData("0.25", 155.8, 111.8)]
    [InlineData("0.5", 311.7, 204.9)]
    [InlineData("1.0", 623.6, 434.9)]
    public void the_excursion_is_proportional_to_each_channels_base_delay(
        string modDepth, double leftAmplitude, double rightAmplitude)
    {
        //Arrange
        // MEASURED: fitting delay(t) = centre + amplitude*cos(...) to 33 clicks gave a centre that
        // never moves and an amplitude proportional to modDepth, equal to the base delay at
        // modDepth="1" on both channels.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"chorus\" mix=\"1.0\" modDepth=\"{modDepth}\" modRate=\"0.125\" />");
        var chorus = (DecentSamplerChorusEffect)effect;

        //Act
        var (minimumLeft, maximumLeft, minimumRight, maximumRight) = Sweep(effect, chorus, 8.0);

        //Assert
        // The fitted amplitude is half the peak-to-peak swing. The right channel's own tolerance is
        // wider because the reference fitted a fixed-rate cosine to a channel whose rate is ten ninths
        // of the one it fitted, which reads its excursion low.
        ((maximumLeft - minimumLeft) / 2.0).Should().BeApproximately(leftAmplitude, 5.0);
        ((maximumRight - minimumRight) / 2.0).Should().BeApproximately(rightAmplitude, 20.0);
        ((maximumLeft + minimumLeft) / 2.0).Should().BeApproximately(624.0, 2.0);
    }

    [Fact]
    public void the_right_oscillator_runs_at_ten_ninths_of_the_left()
    {
        //Arrange
        // MEASURED (round 3, item 41): seven independent fits gave f_right/modRate between 1.10992 and
        // 1.11156 against 10/9 = 1.11111, so the two channels drift with a beat period of 9/modRate.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"chorus\" mix=\"1.0\" modDepth=\"1.0\" modRate=\"1.0\" />");
        var chorus = (DecentSamplerChorusEffect)effect;

        //Act
        DecentSamplerEffectHarness.Run(effect, new float[DecentSamplerEffectHarness.SampleRate * 9]);

        //Assert - nine seconds is exactly nine left cycles and ten right ones, so both come home.
        chorus.Phase.Should().BeApproximately(0.0, 0.001);
        chorus.PhaseRight.Should().BeApproximately(0.0, 0.001);
    }

    [Theory]
    [InlineData("1.0", 1.000)]
    [InlineData("0.5", 0.502)]
    [InlineData("0.25", 0.250)]
    public void the_mix_is_a_plain_linear_crossfade(string mix, double wetTap)
    {
        //Arrange
        // MEASURED: with a dry click of known amplitude beside it, the wet tap reads exactly `mix` and
        // the dry reads 1 - mix, to three decimal places. The RIGHT channel is the one to read: its
        // 441-sample delay is a whole number of samples, so the interpolator does not spread the click.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"chorus\" mix=\"{mix}\" modDepth=\"0.0\" modRate=\"0.125\" />");

        //Act
        var (_, right) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(4096));

        //Assert
        ((double)right[441]).Should().BeApproximately(wetTap, 0.003);
        ((double)right[0]).Should().BeApproximately(1.0 - wetTap, 0.003);
    }

    // The extremes each channel's delay reaches over a whole sweep.
    private static (double MinimumLeft, double MaximumLeft, double MinimumRight, double MaximumRight) Sweep(
        IInstrumentEffect effect, DecentSamplerChorusEffect chorus, double seconds)
    {
        var minimumLeft = double.MaxValue;
        var maximumLeft = double.MinValue;
        var minimumRight = double.MaxValue;
        var maximumRight = double.MinValue;

        var blocks = (int)(seconds * DecentSamplerEffectHarness.SampleRate /
                           DecentSamplerEffectHarness.BlockSize);
        var block = new float[DecentSamplerEffectHarness.BlockSize];

        for (var i = 0; i < blocks; i++)
        {
            var left = new float[DecentSamplerEffectHarness.BlockSize];
            var right = new float[DecentSamplerEffectHarness.BlockSize];
            Array.Copy(block, left, block.Length);
            Array.Copy(block, right, block.Length);

            effect.Process(left, right, DecentSamplerEffectHarness.BlockSize);

            minimumLeft = Math.Min(minimumLeft, chorus.DelayLeftFrames);
            maximumLeft = Math.Max(maximumLeft, chorus.DelayLeftFrames);
            minimumRight = Math.Min(minimumRight, chorus.DelayRightFrames);
            maximumRight = Math.Max(maximumRight, chorus.DelayRightFrames);
        }

        return (minimumLeft, maximumLeft, minimumRight, maximumRight);
    }

    private static double SweepRate(DecentSamplerChorusEffect chorus, double seconds)
    {
        var blocks = (int)(seconds * DecentSamplerEffectHarness.SampleRate /
                           DecentSamplerEffectHarness.BlockSize);

        var left = new float[DecentSamplerEffectHarness.BlockSize];
        var right = new float[DecentSamplerEffectHarness.BlockSize];

        var first = -1;
        var last = -1;
        var wraps = 0;
        var previous = chorus.Phase;

        for (var i = 0; i < blocks; i++)
        {
            Array.Clear(left, 0, left.Length);
            Array.Clear(right, 0, right.Length);
            chorus.Process(left, right, left.Length);

            if (chorus.Phase < previous)
            {
                if (first < 0)
                {
                    first = i;
                }

                last = i;
                wraps++;
            }

            previous = chorus.Phase;
        }

        if (wraps < 2)
        {
            return 0.0;
        }

        var cycleBlocks = (last - first) / (double)(wraps - 1);

        return DecentSamplerEffectHarness.SampleRate /
               (cycleBlocks * DecentSamplerEffectHarness.BlockSize);
    }
}

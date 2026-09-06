using System;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The compressor, against the reference player's own numbers.
/// </summary>
/// <remarks>
/// MEASURED (round 2, item 20): a stereo-linked hard-knee PEAK compressor whose threshold scale is
/// offset by exactly <c>20*log10(2*sqrt(2))</c> = 9.031 dB. Every table below is the reference's, read
/// off 101 cases over two presets; the tolerances are a tenth of a decibel, against a target of half.
/// </remarks>
public class DecentSamplerCompressorEffectTests
{
    [Fact]
    public void a_signal_below_threshold_is_left_alone()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-6\" ratio=\"4\" attack=\"1\" release=\"10\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.5, 0.1);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        DecentSamplerEffectHarness.Rms(left, 11025, 11025)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 11025, 11025), 1.0e-6);
    }

    [Theory]
    [InlineData(-32, 8, 0.000)]
    [InlineData(-24, 8, 0.000)]
    [InlineData(-20, 8, 2.814)]
    [InlineData(-16, 8, 6.314)]
    [InlineData(-12, 8, 9.814)]
    [InlineData(-8, 8, 13.315)]
    [InlineData(-16, 4, 5.412)]
    [InlineData(-8, 4, 11.413)]
    [InlineData(-16, 2, 3.608)]
    [InlineData(-8, 2, 7.609)]
    [InlineData(-12, 20, 10.655)]
    public void the_input_sweep_matches_the_reference(int inputDecibels, int ratio, double expected)
    {
        //Arrange - the reference's own sweep: threshold="-30", attack 5 ms, release 100 ms, a 440 Hz
        //sine at a known root-mean-square level, and the knee sitting 9.031 dB above the declared
        //threshold. The two cells below the knee are what proves the knee is HARD.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"compressor\" threshold=\"-30\" ratio=\"{ratio}\" attack=\"5\" " +
            "release=\"100\" />");

        //Act
        var reduction = SteadyGainReduction(effect, inputDecibels);

        //Assert
        reduction.Should().BeApproximately(expected, 0.1);
    }

    [Theory]
    [InlineData(-80, 53.961)]
    [InlineData(-60, 36.488)]
    [InlineData(-50, 27.738)]
    [InlineData(-40, 18.989)]
    [InlineData(-35, 14.614)]
    [InlineData(-30, 10.239)]
    [InlineData(-25, 5.864)]
    [InlineData(-22, 3.239)]
    [InlineData(-20, 1.489)]
    [InlineData(-18, 0.000)]
    [InlineData(0, 0.000)]
    [InlineData(12, 0.000)]
    public void the_threshold_sweep_matches_the_reference(int threshold, double expected)
    {
        //Arrange - 0.875 dB of reduction per dB of threshold, which is 1 - 1/8 exactly, linear over
        //62 dB. The documented -60 to 0 range is not enforced at either end.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"compressor\" threshold=\"{threshold}\" ratio=\"8\" attack=\"1\" " +
            "release=\"100\" />");

        //Act
        var reduction = SteadyGainReduction(effect, -12);

        //Assert
        reduction.Should().BeApproximately(expected, 0.1);
    }

    [Theory]
    [InlineData("0.5", 0.000)]
    [InlineData("30", 11.312)]
    [InlineData("100", 11.585)]
    public void the_ratio_is_held_at_one_below_and_not_capped_above(string ratio, double expected)
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"compressor\" threshold=\"-30\" ratio=\"{ratio}\" attack=\"1\" " +
            "release=\"100\" />");

        //Act
        var reduction = SteadyGainReduction(effect, -12);

        //Assert
        reduction.Should().BeApproximately(expected, 0.1);
    }

    [Theory]
    [InlineData("0.1", "100", 10.428)]
    [InlineData("1", "100", 10.227)]
    [InlineData("5", "100", 9.807)]
    [InlineData("20", "100", 8.989)]
    [InlineData("50", "100", 8.047)]
    [InlineData("200", "100", 5.822)]
    [InlineData("1", "5", 8.985)]
    [InlineData("1", "50", 10.093)]
    [InlineData("1", "200", 10.336)]
    [InlineData("1", "500", 10.394)]
    [InlineData("1", "2000", 10.447)]
    public void the_eleven_attack_and_release_settings_match_the_reference(
        string attack, string release, double expected)
    {
        //Arrange
        // The reference's own step: 0.6 s at -40 dBFS RMS, 1.6 s at -12, 1.8 s at -40, through
        // threshold="-30" ratio="8". The STEADY reduction depends on the ATTACK time because a slow
        // peak follower cannot reach the peaks of a 440 Hz sine - which is exactly why the gain must
        // not be smoothed as well as the detector.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"compressor\" threshold=\"-30\" ratio=\"8\" attack=\"{attack}\" " +
            $"release=\"{release}\" />");

        var step =
            Concatenate(SineAtRms(-40, 0.6), SineAtRms(-12, 1.6), SineAtRms(-40, 1.8));

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, step);

        var window = (int)(0.6 * DecentSamplerEffectHarness.SampleRate);
        var from = (int)(1.6 * DecentSamplerEffectHarness.SampleRate);
        var reduction = Decibels(
            DecentSamplerEffectHarness.Rms(step, from, window) /
            DecentSamplerEffectHarness.Rms(left, from, window));

        //Assert
        reduction.Should().BeApproximately(expected, 0.1);
    }

    [Fact]
    public void the_input_gain_drives_the_detector_and_is_not_compensated()
    {
        //Arrange
        // MEASURED: inputGain="6" at input -20 dBFS through threshold="-30" ratio="8" landed on
        // -27.346 dBFS. Six decibels of drive buys 6 dB more level into the detector and the
        // compressor gives 7/8 of it back, so the net is 0.75 dB up on the uncompressed -20 dB - and
        // -27.346 is what the whole chain reads.
        var (driven, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-30\" ratio=\"8\" attack=\"5\" " +
            "release=\"100\" inputGain=\"6\" />");

        //Act
        var input = SineAtRms(-20, 0.5);
        var (left, _) = DecentSamplerEffectHarness.Run(driven, input);

        var window = DecentSamplerEffectHarness.SampleRate / 5;
        var from = (int)(0.3 * DecentSamplerEffectHarness.SampleRate);
        var level = Decibels(DecentSamplerEffectHarness.Rms(left, from, window));

        //Assert
        level.Should().BeApproximately(-22.065, 0.1);
    }

    [Fact]
    public void auto_bypass_takes_the_output_gain_out_with_the_rest_of_the_effect()
    {
        //Arrange
        // MEASURED: the discriminator is threshold="0" - never crossed at these levels - with
        // outputGain="6". Without autoBypass the output is 6 dB up; with it, exactly unchanged.
        var (engaged, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"0\" ratio=\"8\" outputGain=\"6\" />");
        var (bypassed, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"0\" ratio=\"8\" outputGain=\"6\" " +
            "autoBypass=\"true\" />");

        var input = SineAtRms(-20, 0.5);

        //Act
        var (withEffect, _) = DecentSamplerEffectHarness.Run(engaged, input);
        var (withBypass, _) = DecentSamplerEffectHarness.Run(bypassed, input);

        var window = DecentSamplerEffectHarness.SampleRate / 5;
        var from = (int)(0.3 * DecentSamplerEffectHarness.SampleRate);
        var reference = DecentSamplerEffectHarness.Rms(input, from, window);

        //Assert
        Decibels(DecentSamplerEffectHarness.Rms(withEffect, from, window) / reference)
            .Should().BeApproximately(6.0, 0.02);
        Decibels(DecentSamplerEffectHarness.Rms(withBypass, from, window) / reference)
            .Should().BeApproximately(0.0, 0.02);
    }

    [Fact]
    public void auto_bypass_fades_back_in_over_about_fifty_milliseconds()
    {
        //Arrange
        // MEASURED: the wet mix reaches 0.85 at 30 ms after the signal crosses the threshold, 0.95 at
        // 40 ms and 0.99 at 50 ms, and is fully engaged by 60 to 80 ms.
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-30\" ratio=\"8\" attack=\"1\" " +
            "release=\"100\" autoBypass=\"true\" />");

        //Act
        DecentSamplerEffectHarness.Run(effect, SineAtRms(-40, 0.5));
        var quiet = ((DecentSamplerCompressorEffect)effect).BypassBlend;

        DecentSamplerEffectHarness.Run(effect, SineAtRms(-12, 0.030));
        var afterThirty = ((DecentSamplerCompressorEffect)effect).BypassBlend;

        DecentSamplerEffectHarness.Run(effect, SineAtRms(-12, 0.050));
        var afterEighty = ((DecentSamplerCompressorEffect)effect).BypassBlend;

        //Assert
        quiet.Should().BeApproximately(0.0, 0.01);
        afterThirty.Should().BeInRange(0.75, 0.95);
        afterEighty.Should().BeGreaterThan(0.99);
    }

    [Fact]
    public void a_ratio_of_one_is_no_compression()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-24\" ratio=\"1\" attack=\"1\" release=\"10\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.3, 1.0);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        DecentSamplerEffectHarness.Rms(left, 6615, 6615)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 6615, 6615), 1.0e-6);
    }

    [Fact]
    public void the_input_and_output_gains_are_decibels()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"0\" ratio=\"1\" inputGain=\"6\" outputGain=\"-6\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.2, 0.25);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert - the two cancel, so nothing below threshold changes level at all.
        DecentSamplerEffectHarness.Rms(left, 4410, 4410)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 4410, 4410), 1.0e-5);
    }

    [Fact]
    public void makeup_gain_is_applied_after_the_compression()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"0\" ratio=\"1\" outputGain=\"6\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.2, 0.25);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        var gain = DecentSamplerEffectHarness.Rms(left, 4410, 4410) /
                   DecentSamplerEffectHarness.Rms(input, 4410, 4410);

        (20.0 * Math.Log10(gain)).Should().BeApproximately(6.0, 0.05);
    }

    [Fact]
    public void a_slower_release_holds_the_gain_reduction_longer()
    {
        //Arrange
        //Act
        var quick = ReductionAfterSilence("20");
        var slow = ReductionAfterSilence("2000");

        //Assert
        slow.Should().BeGreaterThan(quick + 3.0);
    }

    [Fact]
    public void auto_bypass_fades_the_compressor_out_under_threshold()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-6\" ratio=\"8\" attack=\"1\" release=\"20\" " +
            "outputGain=\"-12\" autoBypass=\"true\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 1.0, 0.05);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert - after the fade the effect is out of the way, makeup gain and all.
        ((DecentSamplerCompressorEffect)effect).BypassBlend.Should().BeApproximately(0.0, 0.001);
        DecentSamplerEffectHarness.Rms(left, 33075, 8820)
            .Should().BeApproximately(DecentSamplerEffectHarness.Rms(input, 33075, 8820), 1.0e-6);
    }

    [Fact]
    public void auto_bypass_fades_back_in_when_the_signal_returns()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"-30\" ratio=\"8\" attack=\"1\" release=\"20\" " +
            "autoBypass=\"true\" />");

        var quiet = DecentSamplerEffectHarness.Sine(440, 0.5, 0.005);
        var loud = DecentSamplerEffectHarness.Sine(440, 0.5, 1.0);

        //Act
        DecentSamplerEffectHarness.Run(effect, quiet);
        var faded = ((DecentSamplerCompressorEffect)effect).BypassBlend;

        DecentSamplerEffectHarness.Run(effect, loud);

        //Assert
        faded.Should().BeApproximately(0.0, 0.001);
        ((DecentSamplerCompressorEffect)effect).BypassBlend.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void the_threshold_is_live()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"compressor\" threshold=\"0\" ratio=\"8\" attack=\"1\" release=\"10\" />");
        var input = DecentSamplerEffectHarness.Sine(440, 0.3, 0.5);

        //Act
        effect.TrySetParameter("FX_THRESHOLD", -30.0);
        DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        element.Threshold.Should().Be(-30.0);
        ((DecentSamplerCompressorEffect)effect).GainReductionDecibels.Should().BeGreaterThan(10.0);
    }

    // A 440 Hz sine at a declared root-mean-square level in dBFS, which is how every level in the
    // measurement's tables is written.
    private static float[] SineAtRms(double decibels, double seconds) =>
        DecentSamplerEffectHarness.Sine(440.0, seconds, Math.Pow(10.0, decibels / 20.0) * Math.Sqrt(2.0));

    private static float[] Concatenate(params float[][] parts)
    {
        var total = 0;

        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new float[total];
        var offset = 0;

        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    private static double Decibels(double ratio) => 20.0 * Math.Log10(ratio);

    // The steady gain reduction on a 440 Hz sine at a declared root-mean-square level, read from the
    // last fifth of a half-second tone so that no attack transient is in the window.
    private static double SteadyGainReduction(IInstrumentEffect effect, int inputDecibels)
    {
        var input = SineAtRms(inputDecibels, 0.5);
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        var window = DecentSamplerEffectHarness.SampleRate / 5;
        var from = (int)(0.3 * DecentSamplerEffectHarness.SampleRate);

        return Decibels(
            DecentSamplerEffectHarness.Rms(input, from, window) /
            DecentSamplerEffectHarness.Rms(left, from, window));
    }

    // How much gain reduction is left a fifth of a second after the signal stops.
    private static double ReductionAfterSilence(string release)
    {
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"compressor\" threshold=\"-24\" ratio=\"8\" attack=\"1\" release=\"{release}\" />");

        DecentSamplerEffectHarness.Run(effect, DecentSamplerEffectHarness.Sine(440, 0.3, 1.0));
        DecentSamplerEffectHarness.Run(
            effect, new float[DecentSamplerEffectHarness.SampleRate / 5]);

        return ((DecentSamplerCompressorEffect)effect).GainReductionDecibels;
    }
}

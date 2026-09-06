using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The reverb: stock Freeverb, read off the reference's own impulse response.
/// </summary>
/// <remarks>
/// MEASURED (round 2, item 21): eight combs at 1116 1188 1277 1356 1422 1491 1557 1617 into four
/// allpasses at 556 441 341 225 with feedback 0.5; the right channel adds exactly 23 samples to all
/// twelve; width 1; no pre-delay and no early reflections; comb feedback 0.7 + 0.28 * roomSize; the
/// damping one-pole is damp1 = 0.375 * damping; input scale 0.015 and wet scale 3, so one comb echo at
/// <c>wetLevel="1"</c> is 0.045 of the input.
/// </remarks>
public class DecentSamplerReverbEffectTests
{
    private const int TailSeconds = 4;

    [Fact]
    public void a_bigger_room_decays_more_slowly()
    {
        //Arrange
        //Act
        var small = TailEnergy("0.0");
        var medium = TailEnergy("0.5");
        var large = TailEnergy("1.0");

        //Assert
        medium.Should().BeGreaterThan(small);
        large.Should().BeGreaterThan(medium * 2.0);
    }

    [Fact]
    public void damping_darkens_the_tail()
    {
        //Arrange
        //Act
        var bright = TailTilt("0.0");
        var dark = TailTilt("1.0");

        //Assert - the measured tilt at roomSize 0.5 falls from +8.9 dB to +1.9 dB over the range.
        bright.Should().BeGreaterThan(dark + 3.0);
    }

    [Fact]
    public void wet_level_is_a_linear_gain_on_the_wet_path()
    {
        //Arrange
        //Act
        var full = TailRms("1.0");
        var half = TailRms("0.5");

        //Assert
        (20.0 * Math.Log10(half / full)).Should().BeApproximately(-6.02, 0.3);
    }

    [Fact]
    public void a_wet_level_of_zero_leaves_the_dry_signal_exactly_alone()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"reverb\" roomSize=\"0.7\" damping=\"0.3\" wetLevel=\"0\" />");
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
    public void the_dry_path_is_never_attenuated()
    {
        //Arrange
        var (effect, _) = DecentSamplerEffectHarness.Build(
            "<effect type=\"reverb\" roomSize=\"0.5\" damping=\"0.5\" wetLevel=\"1.0\" />");
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert - the first sample is the dry impulse itself, before any comb has fed back.
        left[0].Should().Be(1f);
    }

    [Fact]
    public void the_room_size_is_live()
    {
        //Arrange
        var (effect, element) = DecentSamplerEffectHarness.Build(
            "<effect type=\"reverb\" roomSize=\"0.0\" damping=\"0.0\" wetLevel=\"1.0\" />");

        //Act
        effect.TrySetParameter("FX_REVERB_ROOM_SIZE", 1.0);
        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate * TailSeconds));

        //Assert
        element.RoomSize.Should().Be(1.0);
        DecentSamplerEffectHarness.Rms(left, DecentSamplerEffectHarness.SampleRate * 2, 44100)
            .Should().BeGreaterThan(0.0005);
    }

    [Theory]
    [InlineData(1116)]
    [InlineData(1188)]
    [InlineData(1277)]
    [InlineData(1356)]
    [InlineData(1422)]
    [InlineData(1491)]
    [InlineData(1617)]
    public void every_comb_echo_arrives_at_its_stock_freeverb_lag(int lag)
    {
        //Arrange
        //Act
        var (left, right) = ImpulseResponse();

        //Assert
        // MEASURED at -26.9 dB relative to the dry impulse, i.e. 0.015 * 3 = 0.045. The right channel
        // carries the same echo exactly 23 samples later. 1557 is left out because it is the one lag
        // where a comb echo and the 1116 + 441 comb-plus-allpass term cancel in the left channel.
        Decibels(Math.Abs(left[lag])).Should().BeApproximately(-26.9, 0.6);
        Decibels(Math.Abs(right[lag + 23])).Should().BeApproximately(-26.9, 0.6);
    }

    [Fact]
    public void the_right_channel_carries_the_stock_twenty_three_sample_spread()
    {
        //Arrange
        //Act
        var (_, right) = ImpulseResponse();

        //Assert - the same eight combs, every one of them 23 samples later.
        Decibels(Math.Abs(right[1580])).Should().BeApproximately(-26.9, 0.6);
        Decibels(Math.Abs(right[1640])).Should().BeApproximately(-26.9, 0.6);
    }

    [Fact]
    public void nothing_arrives_between_the_dry_signal_and_the_first_comb()
    {
        //Arrange
        //Act
        var (left, right) = ImpulseResponse();

        //Assert - MEASURED: no pre-delay, no early reflections and no diffusion in front of the combs.
        DecentSamplerEffectHarness.Peak(left, 1, 1115).Should().BeLessThan(1.0e-6);
        DecentSamplerEffectHarness.Peak(right, 1, 1138).Should().BeLessThan(1.0e-6);
    }

    [Theory]
    [InlineData("0.504", "0.000", 1.37, 1.34, 1.27, 1.30)]
    [InlineData("0.504", "1.000", 1.28, 0.99, 0.72, 0.40)]
    [InlineData("1.000", "0.000", 10.97, 11.25, 11.17, 11.09)]
    [InlineData("1.000", "0.504", 9.15, 6.88, 3.63, 1.48)]
    [InlineData("1.000", "1.000", 7.25, 3.96, 1.53, 0.56)]
    public void the_reverberation_time_per_band_matches_the_reference(
        string roomSize, string damping, double at1k, double at2k, double at4k, double at8k)
    {
        //Arrange
        // MEASURED: the reference's own per-band table, regressed from -5 dB to -25 dB below each
        // band's own peak. A fifth is generous for a reverberation time; the target was 20 %.
        var response = ImpulseResponse(roomSize, damping, seconds: 9);

        //Act
        //Assert
        foreach (var (frequency, expected) in
                 new[] { (1000.0, at1k), (2000.0, at2k), (4000.0, at4k), (8000.0, at8k) })
        {
            var measured = ReverberationTime(response.Left, frequency);
            measured.Should().BeInRange(expected * 0.8, expected * 1.2);
        }
    }

    private static double Decibels(double ratio) => 20.0 * Math.Log10(ratio);

    // The reverb's response to a single-sample impulse, wet all the way up, which is exactly how the
    // reference's structure was read.
    private static (float[] Left, float[] Right) ImpulseResponse(
        string roomSize = "1.0", string damping = "0.0", int seconds = 1)
    {
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"reverb\" roomSize=\"{roomSize}\" damping=\"{damping}\" wetLevel=\"1.0\" />");

        return DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate * seconds));
    }

    private const double EnvelopeHopSeconds = 0.025;

    // The time the band envelope takes to fall 60 dB, from a least-squares fit over the -5 to -25 dB
    // window below its own peak - the same window the reference's own table was regressed over.
    private static double ReverberationTime(float[] samples, double frequency)
    {
        var envelope = BandEnvelope(samples, frequency);
        var peak = double.NegativeInfinity;
        var peakIndex = 0;

        for (var i = 0; i < envelope.Length; i++)
        {
            if (envelope[i] > peak)
            {
                peak = envelope[i];
                peakIndex = i;
            }
        }

        var sumX = 0.0;
        var sumY = 0.0;
        var sumXy = 0.0;
        var sumXx = 0.0;
        var count = 0;

        for (var i = peakIndex; i < envelope.Length; i++)
        {
            var below = peak - envelope[i];

            if (below < 5.0)
            {
                continue;
            }

            if (below > 25.0)
            {
                break;
            }

            var x = i * EnvelopeHopSeconds;
            var y = envelope[i];

            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
            count++;
        }

        if (count < 4)
        {
            return double.PositiveInfinity;
        }

        var slope = ((count * sumXy) - (sumX * sumY)) / ((count * sumXx) - (sumX * sumX));
        return slope >= 0.0 ? double.PositiveInfinity : -60.0 / slope;
    }

    // A band energy envelope in decibels, one reading per hop, summed over Goertzel bins spanning a
    // third of an octave around the centre. The DRY impulse is excluded: it sits alone in front of the
    // first comb echo at lag 1116 and would otherwise be the envelope's peak.
    private static double[] BandEnvelope(float[] samples, double frequency)
    {
        var hop = (int)(EnvelopeHopSeconds * DecentSamplerEffectHarness.SampleRate);
        var start = 1116;
        var count = ((samples.Length - start) / hop) - 1;
        var envelope = new double[count];

        // A third-octave spread of bins around the centre. One bin alone lands wherever the comb
        // interference happens to put it; a whole octave is dominated by its low edge, which reads long.
        var bins = new double[7];
        for (var b = 0; b < bins.Length; b++)
        {
            var octave = (-1.0 / 6.0) + (b / (double)(bins.Length - 1) / 3.0);
            bins[b] = 2.0 * Math.Cos(
                2.0 * Math.PI * frequency * Math.Pow(2.0, octave) / DecentSamplerEffectHarness.SampleRate);
        }

        for (var h = 0; h < count; h++)
        {
            var offset = start + (h * hop);
            var total = 0.0;

            foreach (var coefficient in bins)
            {
                var s1 = 0.0;
                var s2 = 0.0;

                for (var i = 0; i < hop; i++)
                {
                    var s0 = samples[offset + i] + (coefficient * s1) - s2;
                    s2 = s1;
                    s1 = s0;
                }

                total += (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
            }

            envelope[h] = 10.0 * Math.Log10(Math.Max(total, 1.0e-30));
        }

        return envelope;
    }

    private static double TailEnergy(string roomSize)
    {
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"reverb\" roomSize=\"{roomSize}\" damping=\"0.0\" wetLevel=\"1.0\" />");

        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate * TailSeconds));

        // A second in: long past the burst, and inside the tail of every room size measured.
        return DecentSamplerEffectHarness.Rms(left, DecentSamplerEffectHarness.SampleRate, 44100);
    }

    private static double TailRms(string wetLevel)
    {
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"reverb\" roomSize=\"0.7\" damping=\"0.3\" wetLevel=\"{wetLevel}\" />");

        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        return DecentSamplerEffectHarness.Rms(left, 4410, 8820);
    }

    // The balance of high energy against low energy in the tail, in decibels, measured the way the
    // reference recordings were: 3-6 kHz against 200-800 Hz.
    private static double TailTilt(string damping)
    {
        var (effect, _) = DecentSamplerEffectHarness.Build(
            $"<effect type=\"reverb\" roomSize=\"0.5\" damping=\"{damping}\" wetLevel=\"1.0\" />");

        var (left, _) = DecentSamplerEffectHarness.Run(
            effect, DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.SampleRate));

        var high = BandEnergy(left, 3000, 6000);
        var low = BandEnergy(left, 200, 800);

        return 10.0 * Math.Log10(high / low);
    }

    // A one-bin Goertzel sum over a band, which is all the tilt comparison needs.
    private static double BandEnergy(float[] samples, double low, double high)
    {
        var total = 0.0;
        var offset = 4410;
        var length = 13230;

        for (var frequency = low; frequency <= high; frequency += 50.0)
        {
            var real = 0.0;
            var imaginary = 0.0;

            for (var i = 0; i < length; i++)
            {
                var angle = 2.0 * Math.PI * frequency * i / DecentSamplerEffectHarness.SampleRate;
                real += samples[offset + i] * Math.Cos(angle);
                imaginary += samples[offset + i] * Math.Sin(angle);
            }

            total += real * real + imaginary * imaginary;
        }

        return total;
    }
}

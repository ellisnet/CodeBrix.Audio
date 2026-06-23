using System;
using CodeBrix.Audio.Dsp;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="BiQuadFilter"/>.
/// </summary>
public class BiQuadFilterTests
{
    private static float Rms(BiQuadFilter filter, double frequency, int sampleRate, int samples)
    {
        double sumSquares = 0;
        for (int n = 0; n < samples; n++)
        {
            float input = (float)Math.Sin(2.0 * Math.PI * frequency * n / sampleRate);
            float output = filter.Transform(input);
            sumSquares += output * output;
        }
        return (float)Math.Sqrt(sumSquares / samples);
    }

    [Fact]
    public void Low_pass_filter_attenuates_a_high_frequency_more_than_a_low_one()
    {
        //Arrange
        const int sampleRate = 44100;
        var lowPassForLow = BiQuadFilter.LowPassFilter(sampleRate, 1000f, 0.707f);
        var lowPassForHigh = BiQuadFilter.LowPassFilter(sampleRate, 1000f, 0.707f);

        //Act
        float lowBandRms = Rms(lowPassForLow, 200, sampleRate, 8192);
        float highBandRms = Rms(lowPassForHigh, 10000, sampleRate, 8192);

        //Assert
        highBandRms.Should().BeLessThan(lowBandRms);
    }

    [Fact]
    public void High_pass_filter_attenuates_a_low_frequency_more_than_a_high_one()
    {
        //Arrange
        const int sampleRate = 44100;
        var highPassForLow = BiQuadFilter.HighPassFilter(sampleRate, 1000f, 0.707f);
        var highPassForHigh = BiQuadFilter.HighPassFilter(sampleRate, 1000f, 0.707f);

        //Act
        float lowBandRms = Rms(highPassForLow, 100, sampleRate, 8192);
        float highBandRms = Rms(highPassForHigh, 10000, sampleRate, 8192);

        //Assert
        lowBandRms.Should().BeLessThan(highBandRms);
    }
}

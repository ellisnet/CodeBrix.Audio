using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class SoundFontMathTests
{
    [Theory]
    [InlineData(0F, 1F, 1.0E-5)]
    [InlineData(1200F, 2F, 1.0E-5)]
    [InlineData(-1200F, 0.5F, 1.0E-5)]
    [InlineData(-7973F, 0.01F, 1.0E-2)]
    public void timecents_to_seconds_test(float x, float expected, double delta)
    {
        var actual = SoundFontMath.TimecentsToSeconds(x);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(0F, 1F * 8.176F, 1.0E-6)]
    [InlineData(1200F, 2F * 8.176F, 1.0E-6)]
    [InlineData(-1200F, 0.5F * 8.176F, 1.0E-6)]
    [InlineData(1500F, 20F, 1.0)]
    public void cents_to_hertz_test(float x, float expected, double delta)
    {
        var actual = SoundFontMath.CentsToHertz(x);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(0F, 1F, 1.0E-6)]
    [InlineData(1200F, 2F, 1.0E-6)]
    [InlineData(-1200F, 0.5F, 1.0E-6)]
    [InlineData(12000F, 1024F, 1.0E-6)]
    public void cents_to_multiplying_factor_test(float x, float expected, double delta)
    {
        var actual = SoundFontMath.CentsToMultiplyingFactor(x);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(0F, 1F, 1.0E-5)]
    [InlineData(6F, 1.99526F, 1.0E-5)]
    [InlineData(12F, 3.98107F, 1.0E-5)]
    [InlineData(-6F, 0.501187F, 1.0E-5)]
    [InlineData(-12F, 0.251189F, 1.0E-5)]
    public void decibels_to_linear_test(float x, float expected, double delta)
    {
        var actual = SoundFontMath.DecibelsToLinear(x);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(1F, 0F, 1.0E-4)]
    [InlineData(1.99526F, 6F, 1.0E-4)]
    [InlineData(3.98107F, 12F, 1.0E-4)]
    [InlineData(0.501187F, -6F, 1.0E-4)]
    [InlineData(0.251189F, -12F, 1.0E-4)]
    public void linear_to_decibels_test(float x, float expected, double delta)
    {
        var actual = SoundFontMath.LinearToDecibels(x);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(0, 60, 1F, 1.0E-4)]
    [InlineData(100, 60, 1F, 1.0E-4)]
    [InlineData(1000, 60, 1F, 1.0E-4)]
    [InlineData(100, 72, 0.5F, 1.0E-4)]
    [InlineData(100, 48, 2F, 1.0E-4)]
    [InlineData(50, 84, 0.5F, 1.0E-4)]
    [InlineData(50, 36, 2F, 1.0E-4)]
    public void key_number_to_multiplying_factor_test(int cents, int key, float expected, double delta)
    {
        var actual = SoundFontMath.KeyNumberToMultiplyingFactor(cents, key);
        expected.ShouldBeApproximately(actual, delta);
    }

    [Theory]
    [InlineData(1.0, Math.E, 1.0E-5)]
    [InlineData(2.0, Math.E * Math.E, 1.0E-5)]
    [InlineData(3.0, Math.E * Math.E * Math.E, 1.0E-5)]
    [InlineData(0.0, 1.0, 1.0E-5)]
    [InlineData(-1.0, 1.0 / Math.E, 1.0E-5)]
    [InlineData(-2.0, 1.0 / (Math.E * Math.E), 1.0E-5)]
    [InlineData(-3.0, 1.0 / (Math.E * Math.E * Math.E), 1.0E-5)]
    [InlineData(-6.9, 0.001007, 1.0E-5)]
    [InlineData(-7.0, 0.0, 1.0E-9)]
    public void exp_cutoff_test(double x, double expected, double delta)
    {
        var actual = SoundFontMath.ExpCutoff(x);
        expected.ShouldBeApproximately(actual, delta);
    }
}

using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class FadeInOutSampleProviderTests
{
    [Fact]
    public void CanFadeIn()
    {
        var source = new TestSampleProvider(10, 1); // 10 samples per second
        source.UseConstValue = true;
        source.ConstValue = 100;
        var fade = new FadeInOutSampleProvider(source);
        fade.BeginFadeIn(1000);
        float[] buffer = new float[20];
        int read = fade.Read(buffer.AsSpan(0, 20));
        Assert.Equal(20, read);
        Assert.Equal(0, buffer[0]); // start of fade-in
        Assert.Equal(50, buffer[5]); // half-way
        Assert.Equal(100, buffer[10]); // fully fade in
        Assert.Equal(100, buffer[15]); // fully fade in
    }

    [Fact]
    public void CanFadeOut()
    {
        var source = new TestSampleProvider(10, 1); // 10 samples per second
        source.UseConstValue = true;
        source.ConstValue = 100;
        var fade = new FadeInOutSampleProvider(source);
        fade.BeginFadeOut(1000);
        float[] buffer = new float[20];
        int read = fade.Read(buffer.AsSpan(0, 20));
        Assert.Equal(20, read);
        Assert.Equal(100, buffer[0]); // start of fade-out
        Assert.Equal(50, buffer[5]); // half-way
        Assert.Equal(0, buffer[10]); // fully fade out
        Assert.Equal(0, buffer[15]); // fully fade out
    }

    [Fact]
    public void FadeDurationCanBeLongerThanOneRead()
    {
        var source = new TestSampleProvider(10, 1); // 10 samples per second
        source.UseConstValue = true;
        source.ConstValue = 100;
        var fade = new FadeInOutSampleProvider(source);
        fade.BeginFadeIn(1000);
        float[] buffer = new float[4];
        int read = fade.Read(buffer.AsSpan(0, 4));
        Assert.Equal(4, read);
        Assert.Equal(0, buffer[0]); // start of fade-in
        Assert.Equal(10, buffer[1]);
        Assert.Equal(20, buffer[2], 0.0001);
        Assert.Equal(30, buffer[3], 0.0001);

        read = fade.Read(buffer.AsSpan(0, 4));
        Assert.Equal(4, read);
        Assert.Equal(40, buffer[0], 0.0001);
        Assert.Equal(50, buffer[1], 0.0001);
        Assert.Equal(60, buffer[2], 0.0001);
        Assert.Equal(70, buffer[3], 0.0001);

        read = fade.Read(buffer.AsSpan(0, 4));
        Assert.Equal(4, read);
        Assert.Equal(80, buffer[0], 0.0001);
        Assert.Equal(90, buffer[1], 0.0001);
        Assert.Equal(100, buffer[2], 0.0001);
        Assert.Equal(100, buffer[3]);
    }

    [Fact]
    public void WaveFormatReturnsSourceWaveFormat()
    {
        var source = new TestSampleProvider(10, 1); // 10 samples per second
        var fade = new FadeInOutSampleProvider(source);
        Assert.Same(source.WaveFormat, fade.WaveFormat);
    }

    [Fact]
    public void FadeWorksOverSamplePairs()
    {
        var source = new TestSampleProvider(10, 2); // 10 samples per second
        source.UseConstValue = true;
        source.ConstValue = 100;
        var fade = new FadeInOutSampleProvider(source);
        fade.BeginFadeIn(1000);
        float[] buffer = new float[20];
        int read = fade.Read(buffer.AsSpan(0, 20));
        Assert.Equal(20, read);
        Assert.Equal(0, buffer[0]); // start of fade-in
        Assert.Equal(0, buffer[1]); // start of fade-in
        Assert.Equal(50, buffer[10]); // half-way
        Assert.Equal(50, buffer[11]); // half-way
        Assert.Equal(90, buffer[18], 0.0001); // fully fade in
        Assert.Equal(90, buffer[19], 0.0001); // fully fade in
    }

    [Fact]
    public void BufferIsZeroedAfterFadeOut()
    {
        var source = new TestSampleProvider(10, 1); // 10 samples per second
        source.UseConstValue = true;
        source.ConstValue = 100;
        var fade = new FadeInOutSampleProvider(source);
        fade.BeginFadeOut(1000);
        float[] buffer = new float[20];
        int read = fade.Read(buffer.AsSpan(0, 20));
        Assert.Equal(20, read);
        Assert.Equal(100, buffer[0]); // start of fade-in
        Assert.Equal(50, buffer[5]); // half-way
        Assert.Equal(0, buffer[10]); // half-way
        read = fade.Read(buffer.AsSpan(0, 20));
        Assert.Equal(20, read);
        Assert.Equal(0, buffer[0]);
    }
}

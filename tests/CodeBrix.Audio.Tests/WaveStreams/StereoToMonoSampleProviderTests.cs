using System;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class StereoToMonoSampleProviderTests
{
    [Fact]
    public void RightChannelOnly()
    {
        var stereoSampleProvider = new TestSampleProvider(44100, 2);
        var mono = stereoSampleProvider.ToMono(0f, 1f);
        var samples = 1000;
        var buffer = new float[samples];
        var read = mono.Read(buffer.AsSpan(0, buffer.Length));
        Assert.Equal(buffer.Length, read);
        for (int sample = 0; sample < samples; sample++)
        {
            Assert.Equal(1 + 2*sample, buffer[sample]);
        }
    }

    [Fact]
    public void CorrectOutputFormat()
    {
        var stereoSampleProvider = new TestSampleProvider(44100, 2);
        var mono = stereoSampleProvider.ToMono(0f, 1f);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, mono.WaveFormat.Encoding);
        Assert.Equal(1, mono.WaveFormat.Channels);
        Assert.Equal(44100, mono.WaveFormat.SampleRate);
    }

    [Fact]
    public void CorrectOffset()
    {
        var stereoSampleProvider = new TestSampleProvider(44100, 2)
        {
            UseConstValue = true,
            ConstValue = 1
        };
        var mono = stereoSampleProvider.ToMono();

        var bufferLength = 30;
        var offset = 10;
        var samples = 10;

        // [10,20) in buffer will be filled with 1
        var buffer = new float[bufferLength];
        var read = mono.Read(buffer.AsSpan(offset, samples));
        Assert.Equal(samples, read);

        for (int i = 0; i < bufferLength; i++)
        {
            var sample = buffer[i];

            if (i < offset || i >= offset + samples)
            {
                Assert.Equal(0, sample);
            }
            else
            {
                Assert.NotEqual(0, sample);
            }
        }
    }
}

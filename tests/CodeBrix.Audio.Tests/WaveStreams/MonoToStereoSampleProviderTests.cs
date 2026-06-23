using System;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class MonoToStereoSampleProviderTests
{
    [Fact]
    public void LeftChannelOnly()
    {
        var stereoStream = new TestSampleProvider(44100,1).ToStereo(1.0f, 0.0f);
        var buffer = new float[2000];
        var read = stereoStream.Read(buffer.AsSpan(0, 2000));
        Assert.Equal(2000, read);
        for (int n = 0; n < read; n+=2)
        {
            Assert.Equal(n/2, buffer[n]);
            Assert.Equal(0, buffer[n+1]);
        }
    }
}

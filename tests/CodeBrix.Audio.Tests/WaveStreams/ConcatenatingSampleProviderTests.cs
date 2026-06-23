using System;
using System.Linq;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class ConcatenatingSampleProviderTests
{
    [Fact]
    public void CanPassASingleProvider()
    {
        // arrange
        const int expectedLength = 5000;
        var input = new TestSampleProvider(44100, 2, expectedLength);
        var concatenator = new ConcatenatingSampleProvider([input]);
        var buffer = new float[2000];
        var totalRead = 0;

        // act
        while (true)
        {
            var read = concatenator.Read(buffer.AsSpan());
            if (read == 0) break;
            totalRead += read;
            Assert.True(totalRead <= expectedLength);
        }
        Assert.True(totalRead == expectedLength);
    }

    [Fact]
    public void CanPassTwoProviders()
    {
        // arrange
        var expectedLength = 100;
        var input1 = new TestSampleProvider(44100, 2, 50);
        var input2 = new TestSampleProvider(44100, 2, 50);
        var concatenator = new ConcatenatingSampleProvider([input1, input2]);
        var buffer = new float[2000];

        var read = concatenator.Read(buffer.AsSpan());
        Assert.Equal(expectedLength, read);
        Assert.Equal(49, buffer[49]);
        Assert.Equal(0, buffer[50]);
        Assert.Equal(49, buffer[99]);
    }
}

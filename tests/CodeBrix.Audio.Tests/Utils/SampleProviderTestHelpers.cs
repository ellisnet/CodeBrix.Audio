using System;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.Utils;

public static class SampleProviderTestHelpers
{
    public static void AssertReadsExpected(this ISampleProvider sampleProvider, float[] expected)
    {
        AssertReadsExpected(sampleProvider, expected, expected.Length);
    }

    public static void AssertReadsExpected(this ISampleProvider sampleProvider, float[] expected, int readSize)
    {
        var buffer = new float[readSize];
        var read = sampleProvider.Read(buffer.AsSpan());
        Assert.Equal(expected.Length, read);
        for (int n = 0; n < read; n++)
        {
            Assert.Equal(expected[n], buffer[n]);
        }
    }
}

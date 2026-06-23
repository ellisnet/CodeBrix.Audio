using System;
using System.Runtime.InteropServices;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.WaveStreams;
public class StereoToMonoProvider16Tests
{
    [Fact]
    public void RightChannelOnly()
    {
        IWaveProvider stereoStream = new TestStereoProvider();
        StereoToMonoProvider16 mono = new StereoToMonoProvider16(stereoStream);
        mono.LeftVolume = 0.0f;
        mono.RightVolume = 1.0f;
        int samples = 1000;
        byte[] buffer = new byte[samples * 2];
        int read = mono.Read(buffer.AsSpan());
        Assert.Equal(buffer.Length, read);
        var shortBuffer = MemoryMarshal.Cast<byte, short>(buffer.AsSpan());
        short expected = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            short sampleVal = shortBuffer[sample];
            Assert.Equal(expected--, sampleVal);
        }
    }
}

class TestStereoProvider : WaveProvider16
{
    public TestStereoProvider()
        : base(44100, 2)
    { }

    short current;

    public override int Read(Span<short> buffer)
    {
        for (int sample = 0; sample < buffer.Length; sample+=2)
        {
            buffer[sample] = current;
            buffer[sample + 1] = (short)(0 - current);
            current++;
        }
        return buffer.Length;
    }
}

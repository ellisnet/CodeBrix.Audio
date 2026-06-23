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
public class MonoToStereoProvider16Tests
{
    [Fact]
    public void LeftChannelOnly()
    {
        IWaveProvider monoStream = new TestMonoProvider();
        MonoToStereoProvider16 stereo = new MonoToStereoProvider16(monoStream);
        stereo.LeftVolume = 1.0f;
        stereo.RightVolume = 0.0f;
        int samples = 1000;
        byte[] buffer = new byte[samples * 2];
        int read = stereo.Read(buffer.AsSpan());
        Assert.Equal(buffer.Length, read);
        var shortBuffer = MemoryMarshal.Cast<byte, short>(buffer.AsSpan());
        short expected = 0;
        for (int sample = 0; sample < samples; sample+=2)
        {
            short sampleLeft = shortBuffer[sample];
            short sampleRight = shortBuffer[sample+1];
            Assert.Equal(expected++, sampleLeft);
            Assert.Equal(0, sampleRight);
        }
    }
}


class TestMonoProvider : WaveProvider16
{
    short current;

    public override int Read(Span<short> buffer)
    {
        for (int sample = 0; sample < buffer.Length; sample++)
        {
            buffer[sample] = current++;
        }
        return buffer.Length;
    }
}

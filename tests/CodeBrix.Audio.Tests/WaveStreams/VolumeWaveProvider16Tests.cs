using System;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.WaveStreams;
public class VolumeWaveProvider16Tests
{
    [Fact]
    public void DefaultVolumeIs1()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 2));
        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        Assert.Equal(1.0f, vwp.Volume);
    }

    [Fact]
    public void PassesThroughSourceWaveFormat()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 2));
        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        Assert.Same(testProvider.WaveFormat, vwp.WaveFormat);
    }

    [Fact]
    public void PassesThroughDataUnchangedAtVolume1()
    {
        var testProvider= new TestWaveProvider(new WaveFormat(44100,16,2));
        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        byte[] buffer = new byte[20];
        int bytesRead = vwp.Read(buffer.AsSpan());
        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, buffer);
    }

    [Fact]
    public void HalfVolumeWorks()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 2));
        testProvider.ConstValue = 100;
        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        vwp.Volume = 0.5f;
        byte[] buffer = new byte[4];
        int bytesRead = vwp.Read(buffer.AsSpan());
        Assert.Equal(new byte[] { 50, 50, 50, 50 }, buffer);
    }

    [Fact]
    public void ZeroVolumeWorks()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 2));
        testProvider.ConstValue = 100;
        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        vwp.Volume = 0f;
        byte[] buffer = new byte[4];
        int bytesRead = vwp.Read(buffer.AsSpan());
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, buffer);
    }

    [Fact]
    public void DoubleVolumeWorks()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 1));
        testProvider.ConstValue = 2;
        short sampleValue = BitConverter.ToInt16(new byte[] { 2, 2 }, 0);
        sampleValue = (short)(sampleValue * 2);

        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        vwp.Volume = 2f;
        byte[] buffer = new byte[2];
        int bytesRead = vwp.Read(buffer.AsSpan());
        Assert.Equal(BitConverter.GetBytes(sampleValue), buffer);
    }

    [Fact]
    public void DoubleVolumeClips()
    {
        var testProvider = new TestWaveProvider(new WaveFormat(44100, 16, 1));
        testProvider.ConstValue = 100;
        short sampleValue = BitConverter.ToInt16(new byte[] { 100, 100 }, 0);
        sampleValue = Int16.MaxValue;

        VolumeWaveProvider16 vwp = new VolumeWaveProvider16(testProvider);
        vwp.Volume = 2f;
        byte[] buffer = new byte[2];
        int bytesRead = vwp.Read(buffer.AsSpan());
        Assert.Equal(BitConverter.GetBytes(sampleValue), buffer);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.WaveFormats;
public class AdpcmWaveFormatTests
{
    [Fact]
    public void StructureSizeIsCorrect()
    {
        WaveFormat waveFormat = new WaveFormat(8000, 16, 1);
        Assert.Equal(18, Marshal.SizeOf(waveFormat));
        AdpcmWaveFormat adpcmWaveFormat = new AdpcmWaveFormat(8000,1);
        Assert.Equal(18 + 32, Marshal.SizeOf(adpcmWaveFormat));
    }

    [Fact]
    public void StructureContentsAreCorrect()
    {
        AdpcmWaveFormat adpcmWaveFormat = new AdpcmWaveFormat(8000,1);
        Assert.Equal(WaveFormatEncoding.Adpcm, adpcmWaveFormat.Encoding);
        Assert.Equal(8000, adpcmWaveFormat.SampleRate);
        Assert.Equal(1, adpcmWaveFormat.Channels);
        Assert.Equal(4, adpcmWaveFormat.BitsPerSample);
        Assert.Equal(4096, adpcmWaveFormat.AverageBytesPerSecond);
        Assert.Equal(32, adpcmWaveFormat.ExtraSize);
        Assert.Equal(256, adpcmWaveFormat.BlockAlign);
        Assert.Equal(500, adpcmWaveFormat.SamplesPerBlock);
        Assert.Equal(7, adpcmWaveFormat.NumCoefficients);
        Assert.Equal(256, adpcmWaveFormat.Coefficients[0]);
        Assert.Equal(0, adpcmWaveFormat.Coefficients[1]);
        Assert.Equal(512, adpcmWaveFormat.Coefficients[2]);
        Assert.Equal(-256, adpcmWaveFormat.Coefficients[3]);
        Assert.Equal(0, adpcmWaveFormat.Coefficients[4]);
        Assert.Equal(0, adpcmWaveFormat.Coefficients[5]);
        Assert.Equal(192, adpcmWaveFormat.Coefficients[6]);
        Assert.Equal(64, adpcmWaveFormat.Coefficients[7]);
        Assert.Equal(240, adpcmWaveFormat.Coefficients[8]);
        Assert.Equal(0, adpcmWaveFormat.Coefficients[9]);
        Assert.Equal(460, adpcmWaveFormat.Coefficients[10]);
        Assert.Equal(-208, adpcmWaveFormat.Coefficients[11]);
        Assert.Equal(392, adpcmWaveFormat.Coefficients[12]);
        Assert.Equal(-232, adpcmWaveFormat.Coefficients[13]);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.Audio.Tests.Utils;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Wave.SampleProviders;
using CodeBrix.Audio.Wave;
using System.Diagnostics;
using CodeBrix.TestMocks.Mocking;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class MultiplexingSampleProviderTests
{
    [Fact]
    public void NullInputsShouldThrowException()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiplexingSampleProvider(null, 1));
    }

    [Fact]
    public void ZeroInputsShouldThrowException()
    {
        Assert.Throws<ArgumentException>(() => new MultiplexingSampleProvider([], 1));
    }

    [Fact]
    public void ZeroOutputsShouldThrowException()
    {
        var input1 = new Mock<ISampleProvider>();
        Assert.Throws<ArgumentException>(() => new MultiplexingSampleProvider([input1.Object], 0));
    }

    [Fact]
    public void InvalidWaveFormatShouldThowException()
    {
        var input1 = new Mock<ISampleProvider>();
        input1.Setup(x => x.WaveFormat).Returns(new WaveFormat(32000, 16, 1));
        Assert.Throws<ArgumentException>(() => new MultiplexingSampleProvider([input1.Object], 1));
    }

    [Fact]
    public void OneInOneOutShouldCopyWaveFormat()
    {
        var input1 = new Mock<ISampleProvider>();
        var inputWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(32000, 1);
        input1.Setup(x => x.WaveFormat).Returns(inputWaveFormat);
        var mp = new MultiplexingSampleProvider([input1.Object], 1);
        Assert.Equal(inputWaveFormat, mp.WaveFormat);
    }

    [Fact]
    public void OneInTwoOutShouldCopyWaveFormatButBeStereo()
    {
        var input1 = new Mock<ISampleProvider>();
        var inputWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(32000, 1);
        input1.Setup(x => x.WaveFormat).Returns(inputWaveFormat);
        var mp = new MultiplexingSampleProvider([input1.Object], 2);
        var expectedOutputWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(32000, 2);
        Assert.Equal(expectedOutputWaveFormat, mp.WaveFormat);
    }

    [Fact]
    public void OneInOneOutShouldCopyInReadMethod()
    {
        var input1 = new TestSampleProvider(32000, 1);
        float[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var mp = new MultiplexingSampleProvider([input1], 1);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void OneInTwoOutShouldConvertMonoToStereo()
    {
        var input1 = new TestSampleProvider(32000, 1);
        float[] expected = [0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9];
        var mp = new MultiplexingSampleProvider([input1], 2);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void TwoInOneOutShouldSelectLeftChannel()
    {
        var input1 = new TestSampleProvider(32000, 2);
        float[] expected = [0, 2, 4, 6, 8, 10, 12, 14, 16, 18];
        var mp = new MultiplexingSampleProvider([input1], 1);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void TwoInOneOutShouldCanBeConfiguredToSelectRightChannel()
    {
        var input1 = new TestSampleProvider(32000, 2);
        float[] expected = [1, 3, 5, 7, 9, 11, 13, 15, 17, 19];
        var mp = new MultiplexingSampleProvider([input1], 1);
        mp.ConnectInputToOutput(1, 0);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void StereoInTwoOutShouldCopyStereo()
    {
        var input1 = new TestSampleProvider(32000, 2);
        float[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17];
        var mp = new MultiplexingSampleProvider([input1], 2);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void TwoMonoInTwoOutShouldCreateStereo()
    {
        var input1 = new TestSampleProvider(32000, 1);
        var input2 = new TestSampleProvider(32000, 1) { Position = 100 };
        float[] expected = [0, 100, 1, 101, 2, 102, 3, 103, 4, 104, 5, 105];
        var mp = new MultiplexingSampleProvider([input1, input2], 2);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void StereoInTwoOutCanBeConfiguredToSwapLeftAndRight()
    {
        var input1 = new TestSampleProvider(32000, 2);
        float[] expected = [1, 0, 3, 2, 5, 4, 7, 6, 9, 8, 11, 10];
        var mp = new MultiplexingSampleProvider([input1], 2);
        mp.ConnectInputToOutput(0, 1);
        mp.ConnectInputToOutput(1, 0);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void HasConnectInputToOutputMethod()
    {
        var input1 = new TestSampleProvider(32000, 2);
        var mp = new MultiplexingSampleProvider([input1], 1);
        mp.ConnectInputToOutput(1, 0);
    }

    [Fact]
    public void ConnectInputToOutputThrowsExceptionForInvalidInput()
    {
        var input1 = new TestSampleProvider(32000, 2);
        var mp = new MultiplexingSampleProvider([input1], 1);
        Assert.Throws<ArgumentException>(() => mp.ConnectInputToOutput(2, 0));
    }

    [Fact]
    public void ConnectInputToOutputThrowsExceptionForInvalidOutput()
    {
        var input1 = new TestSampleProvider(32000, 2);
        var mp = new MultiplexingSampleProvider([input1], 1);
        Assert.Throws<ArgumentException>(() => mp.ConnectInputToOutput(1, 1));
    }

    [Fact]
    public void InputChannelCountIsCorrect()
    {
        var input1 = new TestSampleProvider(32000, 2);
        var input2 = new TestSampleProvider(32000, 1);
        var mp = new MultiplexingSampleProvider([input1, input2], 1);
        Assert.Equal(3, mp.InputChannelCount);
    }

    [Fact]
    public void OutputChannelCountIsCorrect()
    {
        var input1 = new TestSampleProvider(32000, 1);
        var mp = new MultiplexingSampleProvider([input1], 3);
        Assert.Equal(3, mp.OutputChannelCount);
    }

    [Fact]
    public void ThrowsExceptionIfSampleRatesDiffer()
    {
        var input1 = new TestSampleProvider(32000, 2);
        var input2 = new TestSampleProvider(44100, 1);
        Assert.Throws<ArgumentException>(() => new MultiplexingSampleProvider([input1, input2], 1));
    }

    [Fact]
    public void ReadReturnsZeroIfSingleInputHasReachedEnd()
    {
        var input1 = new TestSampleProvider(32000, 1, 0);
        float[] expected = [];
        var mp = new MultiplexingSampleProvider([input1], 1);
        float[] buffer = new float[10];
        var read = mp.Read(buffer.AsSpan());
        Assert.Equal(0, read);
    }

    [Fact]
    public void ReadReturnsCountIfOneInputHasEndedButTheOtherHasnt()
    {
        var input1 = new TestSampleProvider(32000, 1, 0);
        var input2 = new TestSampleProvider(32000, 1);
        float[] expected = [0, 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7];
        var mp = new MultiplexingSampleProvider([input1, input2], 2);
        mp.AssertReadsExpected(expected);
    }

    [Fact]
    public void ShouldZeroOutBufferIfInputStopsShort()
    {
        var input1 = new TestSampleProvider(32000, 1, 6);
        float[] expected = [0, 1, 2, 3, 4, 5, 0, 0, 0, 0];
        var mp = new MultiplexingSampleProvider([input1], 1);
        float[] buffer = new float[10];
        for (int n = 0; n < buffer.Length; n++)
        {
            buffer[n] = 99;
        }
        var read = mp.Read(buffer.AsSpan());
        Assert.Equal(6, read);
        Assert.Equal(expected, buffer);
    }

    [Fact(Skip = "Performance test - run manually")]
    public void PerformanceTest()
    {
        var input1 = new TestSampleProvider(32000, 1);
        var input2 = new TestSampleProvider(32000, 1);
        var input3 = new TestSampleProvider(32000, 1);
        var input4 = new TestSampleProvider(32000, 1);
        var mp = new MultiplexingSampleProvider([input1, input2, input3, input4], 4);
        mp.ConnectInputToOutput(0, 3);
        mp.ConnectInputToOutput(1, 2);
        mp.ConnectInputToOutput(2, 1);
        mp.ConnectInputToOutput(3, 0);

        float[] buffer = new float[input1.WaveFormat.AverageBytesPerSecond / 4];
        Stopwatch s = new Stopwatch();
        var duration = s.Time(() =>
        {
            // read one hour worth of audio
            for (int n = 0; n < 60 * 60; n++)
            {
                mp.Read(buffer.AsSpan());
            }
        });
        Console.WriteLine("Performance test took {0}ms", duration);
    }
}

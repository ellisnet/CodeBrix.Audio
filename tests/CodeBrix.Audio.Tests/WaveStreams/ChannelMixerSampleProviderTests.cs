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
public class ChannelMixerSampleProviderTests
{
    [Fact]
    public void NullSourceThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ChannelMixerSampleProvider(null, ChannelMixMatrix.MonoToStereo));
    }

    [Fact]
    public void NullMatrixThrows()
    {
        var source = new TestSampleProvider(44100, 1);
        Assert.Throws<ArgumentNullException>(
            () => new ChannelMixerSampleProvider(source, null));
    }

    [Fact]
    public void MatrixRowsMustMatchSourceChannels()
    {
        // StereoToMono expects 2 input rows, but the source is mono.
        var source = new TestSampleProvider(44100, 1);
        Assert.Throws<ArgumentException>(
            () => new ChannelMixerSampleProvider(source, ChannelMixMatrix.StereoToMono));
    }

    [Fact]
    public void ZeroOutputColumnsThrows()
    {
        var source = new TestSampleProvider(44100, 1);
        var matrix = new float[1, 0];
        Assert.Throws<ArgumentException>(
            () => new ChannelMixerSampleProvider(source, matrix));
    }

    [Fact]
    public void OutputFormatReflectsMatrixColumns()
    {
        var source = new TestSampleProvider(48000, 2);
        var mixer = new ChannelMixerSampleProvider(source, ChannelMixMatrix.StereoTo5_1);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, mixer.WaveFormat.Encoding);
        Assert.Equal(6, mixer.WaveFormat.Channels);
        Assert.Equal(48000, mixer.WaveFormat.SampleRate);
    }

    [Fact]
    public void MonoToStereoDuplicatesInput()
    {
        var source = new TestSampleProvider(44100, 1) { UseConstValue = true, ConstValue = 3 };
        var mixer = new ChannelMixerSampleProvider(source, ChannelMixMatrix.MonoToStereo);

        var buffer = new float[10];
        var read = mixer.Read(buffer.AsSpan());

        Assert.Equal(10, read);
        foreach (var sample in buffer)
        {
            Assert.Equal(3f, sample);
        }
    }

    [Fact]
    public void StereoToMonoAveragesChannels()
    {
        // TestSampleProvider emits a ramp 0,1,2,3,... so frame n has left=2n, right=2n+1.
        var source = new TestSampleProvider(44100, 2);
        var mixer = new ChannelMixerSampleProvider(source, ChannelMixMatrix.StereoToMono);

        var buffer = new float[4];
        var read = mixer.Read(buffer.AsSpan());

        Assert.Equal(4, read);
        for (int frame = 0; frame < 4; frame++)
        {
            var expected = (2 * frame + (2 * frame + 1)) * 0.5f;
            Assert.Equal(expected, buffer[frame]);
        }
    }

    [Fact]
    public void IdentityMatrixPassesChannelsThrough()
    {
        var identity = new float[,]
        {
            { 1.0f, 0.0f },
            { 0.0f, 1.0f },
        };
        var source = new TestSampleProvider(44100, 2);
        var mixer = new ChannelMixerSampleProvider(source, identity);

        var buffer = new float[8];
        var read = mixer.Read(buffer.AsSpan());

        Assert.Equal(8, read);
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal((float)i, buffer[i]);
        }
    }

    [Fact]
    public void ReadOnlyFillsWholeBlocks()
    {
        // Output is stereo (2 columns); a 5-sample buffer holds 2 whole frames + 1 leftover.
        var source = new TestSampleProvider(44100, 1) { UseConstValue = true, ConstValue = 1 };
        var mixer = new ChannelMixerSampleProvider(source, ChannelMixMatrix.MonoToStereo);

        var buffer = new float[5];
        var read = mixer.Read(buffer.AsSpan());

        Assert.Equal(4, read);
        Assert.Equal(0f, buffer[4]);
    }

    [Fact]
    public void ReadHonoursEndOfStream()
    {
        // Mono source with only 3 samples available.
        var source = new TestSampleProvider(44100, 1, length: 3);
        var mixer = new ChannelMixerSampleProvider(source, ChannelMixMatrix.MonoToStereo);

        var buffer = new float[20];
        var read = mixer.Read(buffer.AsSpan());

        // 3 input samples = 3 input frames -> 3 output frames -> 6 output samples.
        Assert.Equal(6, read);
    }
}

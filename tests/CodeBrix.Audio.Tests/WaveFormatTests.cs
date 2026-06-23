using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="WaveFormat"/>.
/// </summary>
public class WaveFormatTests
{
    [Fact]
    public void Pcm_constructor_computes_block_align_and_average_bytes()
    {
        //Arrange
        var format = new WaveFormat(44100, 16, 2);

        //Assert
        format.BlockAlign.Should().Be(4);
        format.AverageBytesPerSecond.Should().Be(44100 * 4);
        format.Encoding.Should().Be(WaveFormatEncoding.Pcm);
    }

    [Fact]
    public void CreateIeeeFloatWaveFormat_uses_32_bit_float_encoding()
    {
        //Arrange
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

        //Assert
        format.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        format.BitsPerSample.Should().Be(32);
        format.SampleRate.Should().Be(48000);
        format.Channels.Should().Be(1);
    }

    [Fact]
    public void Default_constructor_is_cd_quality_stereo()
        => new WaveFormat().BlockAlign.Should().Be(4);
}

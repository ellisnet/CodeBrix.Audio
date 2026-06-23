using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="Mp3Frame"/> header parsing.
/// </summary>
public class Mp3FrameTests
{
    [Fact]
    public void LoadFromStream_parses_the_mpeg1_layer3_header_fields()
    {
        //Arrange
        using var stream = new MemoryStream(TestAudio.BuildSilentMp3(frameCount: 2));

        //Act
        var frame = Mp3Frame.LoadFromStream(stream);

        //Assert
        frame.Should().NotBeNull();
        frame.SampleRate.Should().Be(44100);
        frame.BitRate.Should().Be(128000);
        frame.MpegVersion.Should().Be(MpegVersion.Version1);
        frame.MpegLayer.Should().Be(MpegLayer.Layer3);
        frame.ChannelMode.Should().Be(ChannelMode.Mono);
        frame.FrameLength.Should().Be(417);
    }

    [Fact]
    public void LoadFromStream_returns_null_at_end_of_stream()
    {
        //Arrange
        using var empty = new MemoryStream(new byte[0]);

        //Act
        var frame = Mp3Frame.LoadFromStream(empty);

        //Assert
        frame.Should().BeNull();
    }
}

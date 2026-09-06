using System.Text;
using CodeBrix.Audio.ModestSynth.Wavetable;
using CodeBrix.Audio.ModestSynth.Wavetable.Internal;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="ClmChunk" />: the Serum frame-size chunk is read when it says something
/// usable and ignored when it does not, never throwing either way.
/// </summary>
public class ClmChunkTests
{
    [Theory]
    [InlineData("<!>2048 00000000 wavetable (www.xferrecords.com)", 2048)]
    [InlineData("<!>1024 10000000 wavetable", 1024)]
    [InlineData("<!>256", 256)]
    [InlineData("<!> 512 flags", 512)]
    [InlineData("<!>2048", 2048)]
    public void TryParseFrameSize_reads_the_number_after_the_marker(string text, int expected)
    {
        //Act
        bool parsed = Parse(text, out int frameSize);

        //Assert
        parsed.Should().BeTrue();
        frameSize.Should().Be(expected);
    }

    [Fact]
    public void TryParseFrameSize_falls_back_to_the_first_number_when_the_marker_is_missing()
    {
        //Act
        bool parsed = Parse("2048 00000000 wavetable", out int frameSize);

        //Assert
        parsed.Should().BeTrue();
        frameSize.Should().Be(2048);
    }

    [Fact]
    public void TryParseFrameSize_ignores_trailing_padding_bytes()
    {
        //Arrange
        byte[] payload = new byte[32];
        byte[] text = Encoding.ASCII.GetBytes("<!>2048 flags");
        text.CopyTo(payload, 0);

        //Act
        bool parsed = ClmChunk.TryParseFrameSize(payload, WavetableFile.MinimumFrameSize,
            WavetableFile.MaximumFrameSize, out int frameSize);

        //Assert
        parsed.Should().BeTrue();
        frameSize.Should().Be(2048);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no digits at all")]
    [InlineData("<!>0 flags")]
    [InlineData("<!>1 flags")]
    [InlineData("<!>999999 flags")]
    [InlineData("<!>1234567890123 flags")]
    public void TryParseFrameSize_refuses_anything_that_is_not_a_plausible_frame_size(string text)
    {
        //Act
        bool parsed = Parse(text, out int frameSize);

        //Assert
        parsed.Should().BeFalse();
        frameSize.Should().Be(0);
    }

    [Fact]
    public void TryParseFrameSize_refuses_a_null_payload()
    {
        //Act
        bool parsed = ClmChunk.TryParseFrameSize(null, WavetableFile.MinimumFrameSize,
            WavetableFile.MaximumFrameSize, out int frameSize);

        //Assert
        parsed.Should().BeFalse();
        frameSize.Should().Be(0);
    }

    private static bool Parse(string text, out int frameSize)
        => ClmChunk.TryParseFrameSize(Encoding.ASCII.GetBytes(text), WavetableFile.MinimumFrameSize,
            WavetableFile.MaximumFrameSize, out frameSize);
}

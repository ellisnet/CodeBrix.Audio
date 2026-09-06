using System.IO;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Mp3;

/// <summary>
/// Tests for <see cref="XingHeader"/>, and in particular the LAME-style encoder extension that
/// follows the Xing fields and declares the encoder delay and padding a gapless reader trims.
/// The frames are built byte by byte here, so what is being tested is the parse and nothing else.
/// </summary>
public class XingHeaderTests
{
    private static XingHeader LoadFrom(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = Mp3Frame.LoadFromStream(stream);
        return XingHeader.LoadXingHeader(frame);
    }

    [Fact]
    public void Encoder_delay_and_padding_come_from_the_lame_extension()
    {
        //Arrange
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.5, 576, 1464);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.HasEncoderDelayInfo.Should().BeTrue();
        header.EncoderDelay.Should().Be(576);
        header.EncoderPadding.Should().Be(1464);
        header.EncoderTag.Should().Be("LAME3.100");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(529, 4095)]
    [InlineData(2880, 2304)]
    public void Delay_and_padding_survive_the_twelve_bit_packing(int delay, int padding)
    {
        //Arrange
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.2, delay, padding);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.EncoderDelay.Should().Be(delay);
        header.EncoderPadding.Should().Be(padding);
    }

    [Fact]
    public void The_ffmpeg_style_encoder_signature_is_accepted_too()
    {
        // ffmpeg's mp3 muxer writes the Xing frame itself and signs it "Lavc<version>" rather
        // than "LAME<version>". The layout is identical, and it is what machine-generated stem
        // exports carry.

        //Arrange
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.2, 576, 696, "Lavc60.31");

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.EncoderTag.Should().Be("Lavc60.31");
        header.EncoderDelay.Should().Be(576);
        header.EncoderPadding.Should().Be(696);
    }

    [Fact]
    public void An_info_tag_revision_beyond_the_defined_range_is_declined()
    {
        //Arrange - revision 2 is not a layout this parser knows, so the bytes after the
        //          encoder name mean nothing and must not be read as a delay
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.2, 576, 1464, "LAME3.100", 2);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.HasEncoderDelayInfo.Should().BeFalse();
        header.EncoderDelay.Should().Be(0);
        header.EncoderPadding.Should().Be(0);
    }

    [Fact]
    public void An_implausible_encoder_delay_is_declined()
    {
        //Arrange - 4000 samples of priming is not an encoder delay, it is a frame that never
        //          carried an encoder tag and happens to have bytes in the right place
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.2, 4000, 100);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.HasEncoderDelayInfo.Should().BeFalse();
    }

    [Fact]
    public void A_non_ascii_encoder_signature_is_declined()
    {
        //Arrange
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.2, 576, 1464);
        bytes[36 + 4 + 4 + 4 + 4 + 100 + 4 + 2] = 0x01; // third byte of the encoder name

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.HasEncoderDelayInfo.Should().BeFalse();
    }

    [Fact]
    public void A_xing_header_with_no_encoder_extension_reports_no_delay_information()
    {
        //Arrange
        var bytes = SyntheticMp3.CreateBytesWithInfoHeader(0.5);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.Should().NotBeNull();
        header.HasEncoderDelayInfo.Should().BeFalse();
        header.EncoderDelay.Should().Be(0);
        header.EncoderPadding.Should().Be(0);
        header.EncoderTag.Should().BeNull();
    }

    [Fact]
    public void A_frame_without_a_xing_header_is_not_one()
    {
        //Arrange
        var bytes = SyntheticMp3.CreateFrames(4);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.Should().BeNull();
    }

    [Fact]
    public void The_xing_fields_still_read_correctly_alongside_the_encoder_extension()
    {
        //Arrange
        int audioFrames = SyntheticMp3.AudioFramesForSeconds(0.5);
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.5, 576, 1464);

        //Act
        var header = LoadFrom(bytes);

        //Assert
        header.Frames.Should().Be(audioFrames);
        header.VbrScale.Should().Be(0);
    }
}

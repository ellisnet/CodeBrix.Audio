using System;
using System.Linq;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="FlacPacketCodecFactory"/>: which codec-private data it accepts, which it
/// declines (so the engine can offer the request to the next factory), and that it is one of the
/// built-in packet codecs the shared output reports.
/// </summary>
/// <remarks>
/// Nothing here opens the audio device; the probe tests assert as much where they touch the shared
/// output, which is why the class sits in the SharedAudioOutput collection.
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class FlacPacketCodecFactoryTests : IDisposable
{
    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public FlacPacketCodecFactoryTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void Factory_reports_its_identity_and_the_codec_it_serves()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();

        //Assert
        factory.FactoryId.Should().Be("CodeBrix.Audio.ManagedFlac.Packets");
        factory.SupportedCodecIds.Should().Equal("flac");
        factory.Priority.Should().Be(0);
    }

    [Fact]
    public void Factory_declines_another_codec()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));

        //Act
        var decoder = factory.CreateDecoder("vorbis", split.CodecPrivate, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_declines_codec_private_data_that_is_not_a_flac_header()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();

        //Act
        var junk = factory.CreateDecoder("flac", new byte[] { 1, 2, 3, 4 }, null);
        var empty = factory.CreateDecoder("flac", ReadOnlyMemory<byte>.Empty, null);

        //Assert
        junk.Should().BeNull();
        empty.Should().BeNull();
    }

    [Fact]
    public void Factory_declines_a_flac_header_whose_blocks_run_past_the_end()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        var truncated = split.CodecPrivate.AsSpan(0, 20).ToArray();

        //Act
        var decoder = factory.CreateDecoder("flac", truncated, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_declines_a_flac_header_with_no_block_flagged_as_last()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));

        // Just the marker and the STREAMINFO block, with its last-block flag cleared: the blocks
        // then claim to continue past the end of the data.
        var header = split.CodecPrivate.AsSpan(0, 4 + 4 + 34).ToArray();
        header[4] &= 0x7F;

        //Act
        var decoder = factory.CreateDecoder("flac", header, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_declines_a_streaminfo_no_frame_could_be_decoded_against()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        var body = (byte[])split.StreamInfoBody.Clone();

        // Maximum block size (bytes 2-3) of zero.
        body[2] = 0;
        body[3] = 0;

        //Act
        var decoder = factory.CreateDecoder("flac", body, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_accepts_the_flac_stream_header()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));

        //Act
        using var decoder = factory.CreateDecoder("flac", split.CodecPrivate, null);

        //Assert
        decoder.Should().NotBeNull();
        decoder.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(44100);
    }

    [Fact]
    public void Factory_accepts_a_bare_streaminfo_body()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));

        //Act
        using var decoder = factory.CreateDecoder("flac", split.StreamInfoBody, null);

        //Assert
        decoder.Should().NotBeNull();
        decoder.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(44100);
    }

    [Fact]
    public void Factory_matches_the_codec_id_case_insensitively()
    {
        //Arrange
        var factory = new FlacPacketCodecFactory();
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));

        //Act
        using var decoder = factory.CreateDecoder("FLAC", split.CodecPrivate, null);

        //Assert
        decoder.Should().NotBeNull();
    }

    [Fact]
    public void Factory_is_one_of_the_built_in_packet_codec_factories()
    {
        //Act
        var builtIns = ManagedCodecs.BuiltInPacketCodecFactories;

        //Assert
        builtIns.OfType<FlacPacketCodecFactory>().Count().Should().Be(1);
    }

    [Fact]
    public void The_shared_output_reports_flac_packets_as_supported_without_starting_anything()
    {
        //Arrange
        SharedAudioOutput.IsRunning.Should().BeFalse();

        //Act
        var supported = SharedAudioOutput.IsPacketCodecSupported("flac");
        var ids = SharedAudioOutput.SupportedPacketCodecIds;

        //Assert
        supported.Should().BeTrue();
        ids.Should().Contain("flac");
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }
}

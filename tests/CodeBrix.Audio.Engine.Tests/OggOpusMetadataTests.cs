using System;
using System.IO;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Metadata;
using CodeBrix.Audio.Engine.Metadata.Models;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Tests for reading Ogg Opus headers. This library does not DECODE Opus - that ships as a
/// separately licensed add-on package - but it reads Opus metadata, and two things in an
/// OpusHead are easy to report wrongly in a way no Vorbis file would ever expose.
/// </summary>
/// <remarks>
/// <para>
/// The rate first: OpusHead's "input sample rate" field is the rate the ENCODER was handed.
/// Opus always decodes at 48 kHz, and RFC 7845 marks that field informational. It matters
/// because the data providers build the decoder's target format from what is reported here, so
/// a 16 kHz voice note reported as 16 kHz would have its 48 kHz output resampled as though it
/// were 16 kHz - three times too slow, on a codec this library hands to somebody else to decode.
/// </para>
/// <para>
/// Then the duration: an Ogg Opus granule position runs on the 48 kHz clock and COUNTS THE
/// PRE-SKIP, the priming samples the decoder discards. A duration that does not subtract them
/// runs long by a few milliseconds - enough to leave a transport hanging past the end of a file.
/// </para>
/// </remarks>
public class OggOpusMetadataTests
{
    [Theory]
    [InlineData(TestAssets.OpusToneMonoFrom16000, 1)]
    [InlineData(TestAssets.OpusToneStereo, 2)]
    public void An_opus_stream_reports_the_rate_it_decodes_at_not_the_rate_it_was_encoded_from(
        string fixture, int expectedChannels)
    {
        //Arrange
        using var stream = TestAssets.Open(fixture);

        //Act
        var result = SoundMetadataReader.Read(stream);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CodecName.Should().Be("Opus");
        result.Value.FormatIdentifier.Should().Be("ogg");
        result.Value.ChannelCount.Should().Be(expectedChannels);

        // 48000 for BOTH fixtures, including the one whose OpusHead says 16000. ffprobe reports
        // 48000 for that file too.
        result.Value.SampleRate.Should().Be(48000);
    }

    [Theory]
    [InlineData(TestAssets.OpusToneMonoFrom16000)]
    [InlineData(TestAssets.OpusToneStereo)]
    public void An_opus_duration_excludes_the_pre_skip_priming_samples(string fixture)
    {
        //Arrange
        // Both fixtures: pre-skip 312, final granule 12312. ffmpeg decodes exactly 12000 frames,
        // so 0.25 s is the length that is actually heard; the un-subtracted 0.2565 s is not.
        using var stream = TestAssets.Open(fixture);

        //Act
        var result = SoundMetadataReader.Read(stream);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Duration.TotalSeconds
            .Should().BeApproximately(TestAssets.OpusFixtureDurationSeconds, 0.001);

        // Tight enough to fail if the pre-skip is dropped again: without it the answer is
        // 12312 / 48000 = 0.2565 s, which is 6.5 ms out.
        var unsubtracted = TestAssets.OpusFixtureLastGranule / 48000.0;
        result.Value.Duration.TotalSeconds.Should().NotBe(unsubtracted);
    }

    [Fact]
    public void An_opus_duration_is_read_under_the_fast_estimate_setting_too()
    {
        //Arrange
        // AudioFilePlayer's provider asks for FastEstimate. An Ogg stream has no usable
        // first-frame estimate, so the reader always does the tail read - the same divergence
        // from upstream that the Vorbis fixtures cover, checked here for Opus.
        using var stream = TestAssets.Open(TestAssets.OpusToneStereo);
        var options = new ReadOptions
        {
            ReadTags = false,
            ReadAlbumArt = false,
            DurationAccuracy = DurationAccuracy.FastEstimate
        };

        //Act
        var result = SoundMetadataReader.Read(stream, options);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Duration.TotalSeconds
            .Should().BeApproximately(TestAssets.OpusFixtureDurationSeconds, 0.001);
    }

    [Fact]
    public void A_pre_skip_longer_than_the_stream_does_not_report_a_negative_duration()
    {
        //Arrange
        // Pathological, but cheap to be safe about: a file whose granule position is smaller
        // than its own pre-skip has no playable audio at all, and must not report less than none.
        using var stream = BuildOpusStream(preSkip: 5000, channels: 1, lastGranule: 1000);

        //Act
        var result = SoundMetadataReader.Read(stream);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Duration.TotalSeconds.Should().Be(0);
    }

    [Fact]
    public void The_declared_input_rate_never_reaches_the_reported_sample_rate()
    {
        //Arrange
        // A synthetic stream lets the two rates disagree by an amount no encoder would produce,
        // so there is no chance of the assertion passing by coincidence.
        using var stream = BuildOpusStream(preSkip: 312, channels: 2, lastGranule: 96312,
                                           declaredInputRate: 8000);

        //Act
        var result = SoundMetadataReader.Read(stream);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.SampleRate.Should().Be(48000);
        result.Value.ChannelCount.Should().Be(2);
        result.Value.Duration.TotalSeconds.Should().BeApproximately(2.0, 0.001);
    }

    /// <summary>
    /// Builds a minimal Ogg Opus stream: an OpusHead page, an OpusTags page, and an end-of-stream
    /// page carrying <paramref name="lastGranule" />. Enough for the metadata reader, which never
    /// looks at the audio itself.
    /// </summary>
    private static MemoryStream BuildOpusStream(int preSkip, int channels, long lastGranule,
        int declaredInputRate = 48000)
    {
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;                                                   // version
        head[9] = (byte)channels;
        BitConverter.GetBytes((ushort)preSkip).CopyTo(head, 10);
        BitConverter.GetBytes(declaredInputRate).CopyTo(head, 12);
        // 16: output gain (0 dB), 18: channel mapping family 0.

        var tags = new byte[8 + 4 + 4];
        "OpusTags"u8.CopyTo(tags);
        // Vendor string length 0, user comment count 0 - both already zero.

        var audio = new byte[16];                                      // never decoded

        using var buffer = new MemoryStream();
        WritePage(buffer, head, headerType: 0x02, granulePosition: 0, pageSequence: 0);
        WritePage(buffer, tags, headerType: 0x00, granulePosition: 0, pageSequence: 1);
        WritePage(buffer, audio, headerType: 0x04, granulePosition: lastGranule, pageSequence: 2);

        return new MemoryStream(buffer.ToArray());
    }

    private static void WritePage(Stream destination, byte[] packet, byte headerType,
        long granulePosition, int pageSequence)
    {
        var page = new byte[27 + 1 + packet.Length];
        "OggS"u8.CopyTo(page);
        page[4] = 0;                                                   // stream structure version
        page[5] = headerType;
        BitConverter.GetBytes(granulePosition).CopyTo(page, 6);
        BitConverter.GetBytes(0x43425831).CopyTo(page, 14);            // stream serial number
        BitConverter.GetBytes(pageSequence).CopyTo(page, 18);
        // 22: CRC, left zero - the reader does not verify it.
        page[26] = 1;                                                  // one segment
        page[27] = (byte)packet.Length;
        packet.CopyTo(page, 28);

        destination.Write(page, 0, page.Length);
    }
}

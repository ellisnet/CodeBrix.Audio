using System;
using System.Collections.Generic;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Vorbis;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="VorbisPacketCodecFactory"/> and the packet decoder behind it: the seam that
/// decodes Vorbis delivered as container packets rather than as an Ogg stream.
/// </summary>
/// <remarks>
/// The measure of that seam is that it agrees with the stream path SAMPLE FOR SAMPLE on the same
/// audio, so most of these tests take an Ogg fixture apart into packets (see
/// <see cref="OggPacketReader"/>), decode the packets, and compare against what
/// <see cref="VorbisReader"/> makes of the same file.
/// </remarks>
public class VorbisPacketCodecFactoryTests
{
    // The three setup headers come first in every Ogg Vorbis stream; audio starts at packet 3.
    private const int FirstAudioPacket = 3;

    [Fact]
    public void Factory_declines_another_codec()
    {
        //Arrange
        var factory = new VorbisPacketCodecFactory();
        var codecPrivate = CodecPrivateFor(TestAssets.VorbisToneStereo);

        //Act
        var decoder = factory.CreateDecoder("opus", codecPrivate, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_declines_codec_private_data_that_is_not_xiph_laced()
    {
        //Arrange
        var factory = new VorbisPacketCodecFactory();

        //Act
        var decoder = factory.CreateDecoder("vorbis", new byte[] { 1, 2, 3, 4 }, null);

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Factory_reports_its_identity_and_the_codec_it_serves()
    {
        //Arrange
        var factory = new VorbisPacketCodecFactory();

        //Assert
        factory.FactoryId.Should().Be("CodeBrix.Audio.ManagedVorbis.Packets");
        factory.SupportedCodecIds.Should().Contain("vorbis");
        factory.Priority.Should().Be(0);
    }

    [Fact]
    public void Decoder_reports_the_streams_own_format()
    {
        //Arrange
        var factory = new VorbisPacketCodecFactory();

        //Act
        using var decoder = factory.CreateDecoder("vorbis", CodecPrivateFor(TestAssets.VorbisToneMono), null);

        //Assert
        decoder.Channels.Should().Be(1);
        decoder.SampleRate.Should().Be(22050);
        decoder.PreSkipSamples.Should().Be(0);
        decoder.MaxSamplesPerPacket.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(TestAssets.VorbisToneStereo)]
    [InlineData(TestAssets.VorbisToneMono)]
    [InlineData(TestAssets.VorbisSweep)]
    public void Decoding_packets_matches_the_stream_decoder_sample_for_sample(string fixture)
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(fixture));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var reference = DecodeWithStreamDecoder(fixture);

        //Act
        var decoded = DecodePackets(codecPrivate, packets, FirstAudioPacket, out var channels, out _);

        //Assert
        channels.Should().BeGreaterThan(0);
        (decoded.Count % channels).Should().Be(0);

        // The two lengths differ by less than one window, and only at the very end: Ogg's granule
        // position states exactly where the audio stops, so the stream path trims the encoder's
        // trailing padding, while the packet path hands over everything the packets contain and
        // leaves that trim to the container (which is where the information lives - a media
        // container states it in its own fields). Everywhere both paths produce audio, it is the
        // same audio, sample for sample.
        var common = Math.Min(decoded.Count, reference.Count);
        common.Should().BeGreaterThan(0);
        Math.Abs(decoded.Count - reference.Count).Should().BeLessThanOrEqualTo(2048 * channels);

        for (var i = 0; i < common; i++)
        {
            if (decoded[i] != reference[i])
            {
                Assert.Fail($"Sample {i} differs: packet path {decoded[i]}, stream path {reference[i]}.");
            }
        }
    }

    [Fact]
    public void No_packet_produces_more_than_MaxSamplesPerPacket()
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisSweep));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        var buffer = new float[decoder.MaxSamplesPerPacket];
        var largest = 0;

        //Act
        for (var i = FirstAudioPacket; i < packets.Count; i++)
        {
            var produced = decoder.DecodePacket(packets[i], buffer);
            if (produced > largest) largest = produced;
        }

        //Assert
        largest.Should().BeGreaterThan(0);
        largest.Should().BeLessThanOrEqualTo(decoder.MaxSamplesPerPacket);
    }

    [Fact]
    public void The_first_packet_after_construction_produces_no_samples()
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        var buffer = new float[decoder.MaxSamplesPerPacket];

        //Act
        var first = decoder.DecodePacket(packets[FirstAudioPacket], buffer);
        var second = decoder.DecodePacket(packets[FirstAudioPacket + 1], buffer);

        //Assert
        // Not a failure: Vorbis finalises a packet's samples only once the next one has been
        // overlapped onto it, so the first packet after a reset legitimately yields nothing.
        first.Should().Be(0);
        second.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_output_buffer_that_is_too_small_is_refused_by_name()
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        var tooSmall = new float[2];
        decoder.DecodePacket(packets[FirstAudioPacket], tooSmall);   // yields nothing, so it fits

        //Act
        var decodeIntoTooSmall = () => decoder.DecodePacket(packets[FirstAudioPacket + 1], tooSmall);

        //Assert
        decodeIntoTooSmall.Should().Throw<ArgumentException>()
            .WithMessage("*MaxSamplesPerPacket*");
    }

    [Fact]
    public void Decoding_resumes_exactly_after_a_reset_mid_stream()
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisSweep));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var whole = DecodePackets(codecPrivate, packets, FirstAudioPacket, out var channels, out var starts);

        // Restart from a packet well inside the stream, the way a seek does.
        var restartPacket = FirstAudioPacket + ((packets.Count - FirstAudioPacket) / 2);

        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        var buffer = new float[decoder.MaxSamplesPerPacket];

        // Decode from the beginning, then throw that state away.
        for (var i = FirstAudioPacket; i < restartPacket; i++)
        {
            decoder.DecodePacket(packets[i], buffer);
        }

        //Act
        decoder.Reset();
        var afterReset = decoder.DecodePacket(packets[restartPacket], buffer);

        var resumed = new List<float>();
        for (var i = restartPacket + 1; i < packets.Count; i++)
        {
            var produced = decoder.DecodePacket(packets[i], buffer);
            for (var s = 0; s < produced; s++)
            {
                resumed.Add(buffer[s]);
            }
        }

        //Assert
        // The packet fed straight after a reset has nothing to overlap with, exactly as the first
        // packet of the stream had not.
        afterReset.Should().Be(0);

        // From the packet after that, one packet of pre-roll is all Vorbis needs, so the audio is
        // identical to the uninterrupted decode - not merely close to it.
        resumed.Should().NotBeEmpty();
        var offset = starts[restartPacket + 1];
        for (var i = 0; i < resumed.Count; i++)
        {
            if (resumed[i] != whole[offset + i])
            {
                Assert.Fail($"Sample {i} after the reset differs: {resumed[i]} vs {whole[offset + i]}.");
            }
        }
    }

    [Fact]
    public void Reset_before_any_packet_is_harmless()
    {
        //Arrange
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        var buffer = new float[decoder.MaxSamplesPerPacket];

        //Act
        decoder.Reset();
        decoder.DecodePacket(packets[FirstAudioPacket], buffer);
        var produced = decoder.DecodePacket(packets[FirstAudioPacket + 1], buffer);

        //Assert
        produced.Should().BeGreaterThan(0);
    }

    // Decodes every packet from firstPacket onward, reporting where each packet's output starts.
    private static List<float> DecodePackets(byte[] codecPrivate, List<byte[]> packets, int firstPacket,
        out int channels, out int[] packetStarts)
    {
        var factory = new VorbisPacketCodecFactory();
        using var decoder = factory.CreateDecoder("vorbis", codecPrivate, null);
        channels = decoder.Channels;

        var buffer = new float[decoder.MaxSamplesPerPacket];
        var samples = new List<float>();
        packetStarts = new int[packets.Count];

        for (var i = firstPacket; i < packets.Count; i++)
        {
            packetStarts[i] = samples.Count;
            var produced = decoder.DecodePacket(packets[i], buffer);
            for (var s = 0; s < produced; s++)
            {
                samples.Add(buffer[s]);
            }
        }

        return samples;
    }

    // The reference: the same file through the ordinary Ogg stream path.
    private static List<float> DecodeWithStreamDecoder(string fixture)
    {
        using var reader = new VorbisReader(TestAssets.Path(fixture));
        var samples = new List<float>();
        var buffer = new float[reader.Channels * 4096];

        while (true)
        {
            var read = reader.ReadSamples(buffer);
            if (read <= 0) break;
            for (var i = 0; i < read; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples;
    }

    private static byte[] CodecPrivateFor(string fixture)
    {
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(fixture));
        return OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
    }
}

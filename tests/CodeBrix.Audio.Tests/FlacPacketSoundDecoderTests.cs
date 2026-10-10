using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Flac;
using CodeBrix.Audio.Tests.Utils;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="FlacPacketSoundDecoder"/>: FLAC delivered one frame per packet, the shape a
/// media container carries it in.
/// </summary>
/// <remarks>
/// FLAC is lossless, so the measure is exact: each fixture is split into codec-private data and
/// frames (see <see cref="FlacFramePacketizer"/>), the frames are decoded packet by packet, and the
/// result must equal what <see cref="CodeBrix.Audio.Wave.FlacFileReader"/> makes of the same file,
/// float for float.
/// </remarks>
public class FlacPacketSoundDecoderTests
{
    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in TestAssets.AllFlacFixtures) data.Add(fixture);
        return data;
    }

    private static IPacketSoundDecoder CreateDecoder(byte[] codecPrivate) =>
        new FlacPacketCodecFactory().CreateDecoder("flac", codecPrivate, null);

    private static List<float> DecodeFrames(IPacketSoundDecoder decoder, IReadOnlyList<byte[]> frames, int firstFrame = 0)
    {
        var output = new float[decoder.MaxSamplesPerPacket];
        var decoded = new List<float>();
        for (var i = firstFrame; i < frames.Count; i++)
        {
            var produced = decoder.DecodePacket(frames[i], output);
            for (var j = 0; j < produced; j++) decoded.Add(output[j]);
        }

        return decoded;
    }

    private static void ShouldMatchExactly(IReadOnlyList<float> actual, float[] expected, int expectedOffset)
    {
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i] != expected[expectedOffset + i])
            {
                Assert.Fail($"Sample {i} differs: packet path {actual[i]}, file reader {expected[expectedOffset + i]}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Decoding_packets_matches_the_file_reader_sample_for_sample(string fixture)
    {
        //Arrange
        var path = TestAssets.Path(fixture);
        var split = FlacFramePacketizer.Split(path);
        var reference = FlacFramePacketizer.DecodeWithFileReader(path);
        using var decoder = CreateDecoder(split.CodecPrivate);

        //Act
        var decoded = DecodeFrames(decoder, split.Frames);

        //Assert
        split.Frames.Count.Should().BeGreaterThan(1);
        decoded.Count.Should().Be(reference.Length);
        ShouldMatchExactly(decoded, reference, 0);
    }

    [Fact]
    public void A_bare_streaminfo_decodes_exactly_like_the_full_header()
    {
        //Arrange
        var path = TestAssets.Path("flac-tone-stereo-24bit-48000.flac");
        var split = FlacFramePacketizer.Split(path);
        var reference = FlacFramePacketizer.DecodeWithFileReader(path);
        using var decoder = CreateDecoder(split.StreamInfoBody);

        //Act
        var decoded = DecodeFrames(decoder, split.Frames);

        //Assert
        decoded.Count.Should().Be(reference.Length);
        ShouldMatchExactly(decoded, reference, 0);
    }

    [Theory]
    [InlineData("flac-tone-mono-16bit-22050.flac", 1, 22050)]
    [InlineData("flac-tone-stereo-16bit-44100-midside.flac", 2, 44100)]
    [InlineData("flac-tone-stereo-24bit-48000.flac", 2, 48000)]
    public void Decoder_reports_the_streams_own_format(string fixture, int channels, int sampleRate)
    {
        //Arrange
        var path = TestAssets.Path(fixture);
        var split = FlacFramePacketizer.Split(path);
        int maxBlockSize;
        using (var probe = new FlacDecoder(new MemoryStream(File.ReadAllBytes(path))))
        {
            maxBlockSize = probe.StreamInfo.MaximumBlockSize;
        }

        //Act
        using var decoder = CreateDecoder(split.CodecPrivate);

        //Assert
        decoder.Channels.Should().Be(channels);
        decoder.SampleRate.Should().Be(sampleRate);
        decoder.SampleFormat.Should().Be(SampleFormat.F32);
        decoder.PreSkipSamples.Should().Be(0);
        decoder.MaxSamplesPerPacket.Should().Be(maxBlockSize * channels);
        decoder.SupportsLossConcealment.Should().BeFalse();
    }

    [Fact]
    public void Every_packet_produces_its_whole_block_including_the_first()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];

        //Act / Assert
        for (var i = 0; i < split.Frames.Count; i++)
        {
            var produced = decoder.DecodePacket(split.Frames[i], output);
            produced.Should().Be(split.BlockSizes[i] * split.Channels);
            produced.Should().BeLessThanOrEqualTo(decoder.MaxSamplesPerPacket);
        }
    }

    [Fact]
    public void Reset_mid_stream_then_jumping_to_a_later_frame_decodes_it_exactly()
    {
        //Arrange
        var path = TestAssets.Path("flac-noise-stereo-16bit-44100.flac");
        var split = FlacFramePacketizer.Split(path);
        var reference = FlacFramePacketizer.DecodeWithFileReader(path);
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        var jumpTo = split.Frames.Count / 2;
        var jumpOffset = 0;
        for (var i = 0; i < jumpTo; i++) jumpOffset += split.BlockSizes[i] * split.Channels;

        decoder.DecodePacket(split.Frames[0], output);
        decoder.DecodePacket(split.Frames[1], output);

        //Act
        decoder.Reset();
        var decoded = DecodeFrames(decoder, split.Frames, jumpTo);

        //Assert
        decoded.Count.Should().Be(reference.Length - jumpOffset);
        ShouldMatchExactly(decoded, reference, jumpOffset);
    }

    [Fact]
    public void An_empty_packet_returns_nothing_and_leaves_the_next_packet_unaffected()
    {
        //Arrange
        var path = TestAssets.Path(TestAssets.FlacToneStereo);
        var split = FlacFramePacketizer.Split(path);
        var reference = FlacFramePacketizer.DecodeWithFileReader(path);
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];

        //Act
        var forEmpty = decoder.DecodePacket(ReadOnlySpan<byte>.Empty, output);
        var forFirst = decoder.DecodePacket(split.Frames[0], output);

        //Assert
        forEmpty.Should().Be(0);
        forFirst.Should().Be(split.BlockSizes[0] * split.Channels);
        ShouldMatchExactly(new ArraySegment<float>(output, 0, forFirst), reference, 0);
    }

    [Fact]
    public void Concealing_a_loss_produces_silence_of_exactly_the_lost_length()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        Array.Fill(output, 0.5f);

        //Act
        var produced = decoder.ConcealLoss(100, output);
        var capped = decoder.ConcealLoss(int.MaxValue, output);

        //Assert
        produced.Should().Be(100 * split.Channels);
        for (var i = 0; i < produced; i++) output[i].Should().Be(0f);
        capped.Should().Be(decoder.MaxSamplesPerPacket);
    }

    [Fact]
    public void A_truncated_packet_throws_and_the_decoder_carries_on_with_the_next()
    {
        //Arrange
        var path = TestAssets.Path(TestAssets.FlacToneStereo);
        var split = FlacFramePacketizer.Split(path);
        var reference = FlacFramePacketizer.DecodeWithFileReader(path);
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        var truncated = split.Frames[0].AsSpan(0, split.Frames[0].Length / 2).ToArray();
        var oneByte = new byte[] { 0xFF };

        //Act
        var decodeTruncated = () => decoder.DecodePacket(truncated, output);
        var decodeOneByte = () => decoder.DecodePacket(oneByte, output);

        //Assert
        decodeTruncated.Should().Throw<InvalidDataException>();
        decodeOneByte.Should().Throw<InvalidDataException>();
        var produced = decoder.DecodePacket(split.Frames[0], output);
        produced.Should().Be(split.BlockSizes[0] * split.Channels);
        ShouldMatchExactly(new ArraySegment<float>(output, 0, produced), reference, 0);
    }

    [Fact]
    public void A_damaged_packet_fails_its_crc_and_throws()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        var damaged = (byte[])split.Frames[1].Clone();
        damaged[damaged.Length / 2] ^= 0x10;

        //Act
        var decode = () => decoder.DecodePacket(damaged, output);

        //Assert
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_packet_holding_more_than_one_frame_throws()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        var twoFrames = new byte[split.Frames[0].Length + split.Frames[1].Length];
        split.Frames[0].CopyTo(twoFrames, 0);
        split.Frames[1].CopyTo(twoFrames, split.Frames[0].Length);

        //Act
        var decode = () => decoder.DecodePacket(twoFrames, output);

        //Assert
        decode.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void An_output_buffer_too_small_for_the_packet_throws_ArgumentException()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        using var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[16];

        //Act
        var decode = () => decoder.DecodePacket(split.Frames[0], output);

        //Assert
        decode.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_disposed_decoder_returns_nothing()
    {
        //Arrange
        var split = FlacFramePacketizer.Split(TestAssets.Path(TestAssets.FlacToneStereo));
        var decoder = CreateDecoder(split.CodecPrivate);
        var output = new float[decoder.MaxSamplesPerPacket];
        decoder.Dispose();

        //Act
        var produced = decoder.DecodePacket(split.Frames[0], output);

        //Assert
        produced.Should().Be(0);
    }

    [Fact]
    public void A_hand_built_verbatim_frame_decodes_to_its_samples()
    {
        //Arrange
        short[] samples = [0, 1000, -1000, short.MaxValue, short.MinValue, 12345, -2, 7];
        using var decoder = CreateDecoder(HandBuiltFlac.StreamInfo(bitsPerSample: 16, maxBlockSize: samples.Length));
        var output = new float[decoder.MaxSamplesPerPacket];
        var frame = HandBuiltFlac.VerbatimMonoFrame(samples, sampleSizeCode: 4, bitsPerSample: 16);

        //Act
        var produced = decoder.DecodePacket(frame, output);

        //Assert
        produced.Should().Be(samples.Length);
        for (var i = 0; i < samples.Length; i++) output[i].Should().Be(samples[i] / 32768f);
    }

    [Fact]
    public void A_frame_that_states_its_own_bit_depth_is_scaled_by_it()
    {
        //Arrange
        // STREAMINFO says 16-bit, but the frame header states 8-bit (sample size code 1), which the
        // FLAC format allows a frame to do: its samples are 8-bit values and full scale is 128.
        short[] samples = [64, -128, 127, 0];
        using var decoder = CreateDecoder(HandBuiltFlac.StreamInfo(bitsPerSample: 16, maxBlockSize: 16));
        var output = new float[decoder.MaxSamplesPerPacket];
        var frame = HandBuiltFlac.VerbatimMonoFrame(samples, sampleSizeCode: 1, bitsPerSample: 8);

        //Act
        var produced = decoder.DecodePacket(frame, output);

        //Assert
        produced.Should().Be(samples.Length);
        output[0].Should().Be(0.5f);
        output[1].Should().Be(-1f);
        output[2].Should().Be(127f / 128f);
        output[3].Should().Be(0f);
    }

    [Fact]
    public void A_frame_larger_than_the_streaminfo_maximum_is_rejected()
    {
        //Arrange
        short[] samples = new short[32];
        using var decoder = CreateDecoder(HandBuiltFlac.StreamInfo(bitsPerSample: 16, maxBlockSize: 16));
        var output = new float[1024];
        var frame = HandBuiltFlac.VerbatimMonoFrame(samples, sampleSizeCode: 4, bitsPerSample: 16);

        //Act
        var decode = () => decoder.DecodePacket(frame, output);

        //Assert
        decode.Should().Throw<InvalidDataException>();
    }

    /// <summary>
    /// Builds minimal FLAC data by hand - a mono STREAMINFO and VERBATIM-subframe frames - for the
    /// cases no encoder-made fixture covers.
    /// </summary>
    private static class HandBuiltFlac
    {
        /// <summary>A bare 34-byte mono, 44.1 kHz STREAMINFO body.</summary>
        public static byte[] StreamInfo(int bitsPerSample, int maxBlockSize)
        {
            var writer = new BitWriter();
            writer.Write(16, 16);                       // minimum block size
            writer.Write((uint)maxBlockSize, 16);       // maximum block size
            writer.Write(0, 24);                        // minimum frame size: unknown
            writer.Write(0, 24);                        // maximum frame size: unknown
            writer.Write(44100, 20);                    // sample rate
            writer.Write(0, 3);                         // channels - 1
            writer.Write((uint)(bitsPerSample - 1), 5); // bits per sample - 1
            writer.Write(0, 4);                         // total samples: unknown (high bits)
            writer.Write(0, 32);                        // total samples: unknown (low bits)
            for (var i = 0; i < 16; i++) writer.Write(0, 8); // MD5: not computed
            return writer.ToArray();
        }

        /// <summary>One mono frame holding <paramref name="samples"/> in a VERBATIM subframe.</summary>
        public static byte[] VerbatimMonoFrame(short[] samples, int sampleSizeCode, int bitsPerSample)
        {
            var writer = new BitWriter();
            writer.Write(0x3FFE, 14);                   // sync code
            writer.Write(0, 1);                         // reserved
            writer.Write(0, 1);                         // fixed-blocksize strategy
            writer.Write(7, 4);                         // block size: 16-bit (size - 1) follows
            writer.Write(0, 4);                         // sample rate: from STREAMINFO
            writer.Write(0, 4);                         // channel assignment: mono
            writer.Write((uint)sampleSizeCode, 3);      // sample size
            writer.Write(0, 1);                         // reserved
            writer.Write(0, 8);                         // frame number 0, UTF-8 coded
            writer.Write((uint)(samples.Length - 1), 16);
            writer.Write(writer.Crc8(), 8);             // header CRC-8

            writer.Write(0, 1);                         // subframe: zero pad
            writer.Write(1, 6);                         // VERBATIM
            writer.Write(0, 1);                         // no wasted bits
            foreach (var sample in samples)
                writer.Write((uint)sample & ((1u << bitsPerSample) - 1), bitsPerSample);

            writer.PadToByte();
            writer.Write(writer.Crc16(), 16);           // frame CRC-16
            return writer.ToArray();
        }
    }

    /// <summary>A most-significant-bit-first writer, the mirror of the library's bit reader.</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> bytes = [];
        private int current;
        private int bitCount;

        public void Write(uint value, int bits)
        {
            for (var i = bits - 1; i >= 0; i--)
            {
                current = (current << 1) | (int)((value >> i) & 1);
                bitCount++;
                if (bitCount == 8)
                {
                    bytes.Add((byte)current);
                    current = 0;
                    bitCount = 0;
                }
            }
        }

        public void PadToByte()
        {
            while (bitCount != 0) Write(0, 1);
        }

        public uint Crc8()
        {
            byte crc = 0;
            foreach (var value in bytes) crc = FlacCrc.Update8(crc, value);
            return crc;
        }

        public uint Crc16()
        {
            ushort crc = 0;
            foreach (var value in bytes) crc = FlacCrc.Update16(crc, value);
            return crc;
        }

        public byte[] ToArray() => bytes.ToArray();
    }
}

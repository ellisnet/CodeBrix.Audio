using System;
using CodeBrix.Audio.Codecs;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Codecs;
public class MuLawDecoderTests
{
    [Fact]
    public void BatchDecodeMatchesSingleSampleDecode()
    {
        var source = new byte[256];
        for (int i = 0; i < 256; i++) source[i] = (byte)((i * 47 + 17) & 0xFF);

        var batch = new short[source.Length];
        MuLawDecoder.Decode(source, batch);

        var expected = new short[source.Length];
        for (int i = 0; i < source.Length; i++)
            expected[i] = MuLawDecoder.MuLawToLinearSample(source[i]);

        Assert.Equal(expected, batch);
    }

    [Fact]
    public void EncodeDecodeRoundTripsWithinCodecRange()
    {
        var source = new byte[256];
        for (int i = 0; i < 256; i++) source[i] = (byte)i;

        var decoded = new short[256];
        MuLawDecoder.Decode(source, decoded);

        for (int i = 0; i < 256; i++)
        {
            byte reEncoded = MuLawEncoder.LinearToMuLawSample(decoded[i]);
            Assert.Equal(source[i], reEncoded);
        }
    }

    [Fact]
    public void DestinationShorterThanSourceThrows()
    {
        var source = new byte[100];
        var destination = new short[50];
        Assert.Throws<ArgumentException>(() => MuLawDecoder.Decode(source, destination));
    }

    [Fact]
    public void LargerDestinationIsAllowed()
    {
        var source = new byte[] { 0xAA, 0x55, 0xFF };
        var destination = new short[5];
        destination[3] = 123;
        destination[4] = 456;

        MuLawDecoder.Decode(source, destination);

        Assert.Equal(MuLawDecoder.MuLawToLinearSample(0xAA), destination[0]);
        Assert.Equal(MuLawDecoder.MuLawToLinearSample(0x55), destination[1]);
        Assert.Equal(MuLawDecoder.MuLawToLinearSample(0xFF), destination[2]);
        Assert.Equal((short)123, destination[3]);
        Assert.Equal((short)456, destination[4]);
    }

    [Fact]
    public void EmptySourceIsNoOp()
    {
        var destination = new short[] { 1, 2, 3 };
        MuLawDecoder.Decode(ReadOnlySpan<byte>.Empty, destination);
        Assert.Equal(new short[] { 1, 2, 3 }, destination);
    }
}

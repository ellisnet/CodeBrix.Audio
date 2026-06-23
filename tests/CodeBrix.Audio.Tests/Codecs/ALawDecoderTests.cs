using System;
using CodeBrix.Audio.Codecs;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Codecs;
public class ALawDecoderTests
{
    [Fact]
    public void BatchDecodeMatchesSingleSampleDecode()
    {
        // Every one of the 256 a-law byte values, in a mixed order, to exercise the whole table.
        var source = new byte[256];
        for (int i = 0; i < 256; i++) source[i] = (byte)((i * 53 + 11) & 0xFF);

        var batch = new short[source.Length];
        ALawDecoder.Decode(source, batch);

        var expected = new short[source.Length];
        for (int i = 0; i < source.Length; i++)
            expected[i] = ALawDecoder.ALawToLinearSample(source[i]);

        Assert.Equal(expected, batch);
    }

    [Fact]
    public void EncodeDecodeRoundTripsWithinCodecRange()
    {
        // a-law is lossy, so exact equality doesn't hold — but re-encoding the decoded sample
        // must yield the same a-law byte we started with.
        var source = new byte[256];
        for (int i = 0; i < 256; i++) source[i] = (byte)i;

        var decoded = new short[256];
        ALawDecoder.Decode(source, decoded);

        for (int i = 0; i < 256; i++)
        {
            byte reEncoded = ALawEncoder.LinearToALawSample(decoded[i]);
            Assert.Equal(source[i], reEncoded);
        }
    }

    [Fact]
    public void DestinationShorterThanSourceThrows()
    {
        var source = new byte[100];
        var destination = new short[50];
        Assert.Throws<ArgumentException>(() => ALawDecoder.Decode(source, destination));
    }

    [Fact]
    public void LargerDestinationIsAllowed()
    {
        // Only source.Length samples should be written — trailing slots remain untouched.
        var source = new byte[] { 0xAA, 0x55, 0xFF };
        var destination = new short[5];
        destination[3] = 123;
        destination[4] = 456;

        ALawDecoder.Decode(source, destination);

        Assert.Equal(ALawDecoder.ALawToLinearSample(0xAA), destination[0]);
        Assert.Equal(ALawDecoder.ALawToLinearSample(0x55), destination[1]);
        Assert.Equal(ALawDecoder.ALawToLinearSample(0xFF), destination[2]);
        Assert.Equal((short)123, destination[3]);
        Assert.Equal((short)456, destination[4]);
    }

    [Fact]
    public void EmptySourceIsNoOp()
    {
        var destination = new short[] { 1, 2, 3 };
        ALawDecoder.Decode(ReadOnlySpan<byte>.Empty, destination);
        Assert.Equal(new short[] { 1, 2, 3 }, destination);
    }
}

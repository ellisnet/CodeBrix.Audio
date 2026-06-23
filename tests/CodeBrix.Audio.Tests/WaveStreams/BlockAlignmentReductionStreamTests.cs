using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.WaveStreams;
public class BlockAlignmentReductionStreamTests
{
    [Fact]
    public void CanCreateBlockAlignmentReductionStream()
    {
        BlockAlignedWaveStream inputStream = new BlockAlignedWaveStream(726, 80000);
        BlockAlignReductionStream blockStream = new BlockAlignReductionStream(inputStream);
        Assert.Equal(726, inputStream.BlockAlign);
        Assert.Equal(2, blockStream.BlockAlign);
    }

    [Fact]
    public void CanReadNonBlockAlignedLengths()
    {
        BlockAlignedWaveStream inputStream = new BlockAlignedWaveStream(726, 80000);
        BlockAlignReductionStream blockStream = new BlockAlignReductionStream(inputStream);
        
        byte[] inputBuffer = new byte[1024];
        int read = blockStream.Read(inputBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(1024, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1024, 0);

        read = blockStream.Read(inputBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(2048, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1024, 1024);
    }

    [Fact]
    public void CanRepositionToNonBlockAlignedPositions()
    {
        BlockAlignedWaveStream inputStream = new BlockAlignedWaveStream(726, 80000);
        BlockAlignReductionStream blockStream = new BlockAlignReductionStream(inputStream);

        byte[] inputBuffer = new byte[1024];
        int read = blockStream.Read(inputBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(1024, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1024, 0);

        read = blockStream.Read(inputBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(2048, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1024, 1024);

        // can reposition correctly
        blockStream.Position = 1000;
        read = blockStream.Read(inputBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(2024, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1024, 1000);
    }

    [Fact]
    public void CanRepositionAfterNonBlockAlignedRead()
    {
        // Reproduces #368: an arbitrary-length read (the whole point of
        // this helper) leaves the internal position non-block-aligned.
        // A subsequent valid, block-aligned reposition must not throw
        // "Position must be block aligned" - the setter must validate
        // the incoming value, not the stale current position.
        BlockAlignedWaveStream inputStream = new BlockAlignedWaveStream(726, 80000);
        BlockAlignReductionStream blockStream = new BlockAlignReductionStream(inputStream);

        byte[] inputBuffer = new byte[1023]; // odd -> position becomes non-block-aligned
        int read = blockStream.Read(inputBuffer, 0, 1023);
        Assert.Equal(1023, read);
        Assert.Equal(1023, blockStream.Position);
        CheckReadBuffer(inputBuffer, 1023, 0);

        // 2048 is a multiple of BlockAlign (2) so this must succeed even
        // though the current position (1023) is not block aligned.
        Assert.Null(Record.Exception(() => blockStream.Position = 2048));
        Assert.Equal(2048, blockStream.Position);

        byte[] readBuffer = new byte[1024];
        read = blockStream.Read(readBuffer, 0, 1024);
        Assert.Equal(1024, read);
        Assert.Equal(3072, blockStream.Position);
        CheckReadBuffer(readBuffer, 1024, 2048);
    }

    [Fact]
    public void RepositionToNonBlockAlignedPositionThrows()
    {
        // The setter must reject a non-block-aligned target value.
        // BlockAlign is 2 (16-bit mono) so an odd position is invalid.
        BlockAlignedWaveStream inputStream = new BlockAlignedWaveStream(726, 80000);
        BlockAlignReductionStream blockStream = new BlockAlignReductionStream(inputStream);

        Assert.Throws<ArgumentException>(() => blockStream.Position = 1001);
    }

    private void CheckReadBuffer(byte[] readBuffer, int count, int startPosition)
    {
        for (int n = 0; n < count; n++)
        {
            byte expected = (byte)((startPosition + n) % 256);
            Assert.Equal(expected, readBuffer[n]);
        }
    }
}

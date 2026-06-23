using System;
using System.Linq;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class BufferedWaveProviderTests
{
    [Fact]
    public void CanClearBeforeWritingSamples()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        bwp.ClearBuffer();
        Assert.Equal(0, bwp.BufferedBytes);
    }
    
    [Fact]
    public void BufferedBytesAreReturned()
    {
        var bytesToBuffer = 1000;
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        var data = Enumerable.Range(1, bytesToBuffer).Select(n => (byte)(n % 256)).ToArray();
        bwp.AddSamples(data, 0, data.Length);
        Assert.Equal(bytesToBuffer, bwp.BufferedBytes);
        var readBuffer = new byte[bytesToBuffer];
        var bytesRead = bwp.Read(readBuffer.AsSpan());
        Assert.Equal(bytesToBuffer, bytesRead);
        Assert.Equal(data, readBuffer);
        Assert.Equal(0, bwp.BufferedBytes);
    }

    [Fact]
    public void EmptyBufferCanReturnZeroFromRead()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat());
        bwp.ReadFully = false;
        var buffer = new byte[44100];
        var read = bwp.Read(buffer.AsSpan());
        Assert.Equal(0, read);
    }

    [Fact]
    public void PartialReadsPossibleWithReadFullyFalse()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat());
        bwp.ReadFully = false;
        var buffer = new byte[44100];
        bwp.AddSamples(buffer, 0, 2000);
        var read = bwp.Read(buffer.AsSpan());
        Assert.Equal(2000, read);
        Assert.Equal(0, bwp.BufferedBytes);
    }

    [Fact]
    public void FullReadsByDefault()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat());
        var buffer = new byte[44100];
        bwp.AddSamples(buffer, 0, 2000);
        var read = bwp.Read(buffer.AsSpan());
        Assert.Equal(buffer.Length, read);
        Assert.Equal(0, bwp.BufferedBytes);
    }

    [Fact]
    public void WhenBufferHasMoreThanNeededReadFully()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat());
        var buffer = new byte[44100];
        bwp.AddSamples(buffer, 0, 5000);
        var read = bwp.Read(buffer.AsSpan(0, 2000));
        Assert.Equal(2000, read);
        Assert.Equal(3000, bwp.BufferedBytes);
    }

    // ── Constructor defaults ──────────────────────────────────────────────

    [Fact]
    public void DefaultBufferLengthIsFiveSeconds()
    {
        var format = new WaveFormat(44100, 16, 2);
        var bwp = new BufferedWaveProvider(format);
        Assert.Equal(format.AverageBytesPerSecond * 5, bwp.BufferLength);
    }

    [Fact]
    public void ReadFullyIsTrueByDefault()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat());
        Assert.True(bwp.ReadFully);
    }

    [Fact]
    public void WaveFormatPropertyReturnsConstructorArgument()
    {
        var format = new WaveFormat(44100, 16, 2);
        var bwp = new BufferedWaveProvider(format);
        Assert.Same(format, bwp.WaveFormat);
    }

    // ── BufferLength property ─────────────────────────────────────────────

    [Fact]
    public void BufferLengthReflectsConstructorDuration()
    {
        var format = new WaveFormat(44100, 16, 2);
        var bwp = new BufferedWaveProvider(format, TimeSpan.FromSeconds(2));
        Assert.Equal((int)(2.0 * format.AverageBytesPerSecond), bwp.BufferLength);
    }

    // ── BufferDuration property ───────────────────────────────────────────

    [Fact]
    public void BufferDurationReflectsConstructorArgument()
    {
        var format = new WaveFormat(44100, 16, 2);
        var bwp = new BufferedWaveProvider(format, TimeSpan.FromSeconds(3));
        Assert.Equal(3.0, bwp.BufferDuration.TotalSeconds, 0.001);
    }

    // ── DiscardOnBufferOverflow ───────────────────────────────────────────

    [Fact]
    public void AddSamplesThrowsWhenBufferFullAndDiscardDisabled()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2), TimeSpan.FromMilliseconds(100));
        bwp.AddSamples(new byte[bwp.BufferLength], 0, bwp.BufferLength);
        Assert.Throws<InvalidOperationException>(() => bwp.AddSamples(new byte[1], 0, 1));
    }

    [Fact]
    public void AddSamplesDiscardsWhenBufferFullAndDiscardEnabled()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2), TimeSpan.FromMilliseconds(100));
        bwp.DiscardOnBufferOverflow = true;
        bwp.AddSamples(new byte[bwp.BufferLength], 0, bwp.BufferLength);
        Assert.Null(Record.Exception(() => bwp.AddSamples(new byte[100], 0, 100)));
        Assert.Equal(bwp.BufferLength, bwp.BufferedBytes);
    }

    // ── ClearBuffer ───────────────────────────────────────────────────────

    [Fact]
    public void ClearBufferResetsBufferedBytesToZero()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        bwp.AddSamples(new byte[500], 0, 500);
        bwp.ClearBuffer();
        Assert.Equal(0, bwp.BufferedBytes);
    }

    // ── Offset parameters ─────────────────────────────────────────────────

    [Fact]
    public void AddSamplesRespectsSourceOffset()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        bwp.ReadFully = false;
        bwp.AddSamples(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 4, 4);
        var readBuffer = new byte[4];
        bwp.Read(readBuffer.AsSpan());
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, readBuffer);
    }

    [Fact]
    public void ReadRespectsDestinationOffset()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        bwp.ReadFully = false;
        bwp.AddSamples(new byte[] { 1, 2, 3, 4 }, 0, 4);
        var readBuffer = new byte[8];
        var bytesRead = bwp.Read(readBuffer.AsSpan(4, 4));
        Assert.Equal(4, bytesRead);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, readBuffer);
    }

    // ── Miscellaneous ─────────────────────────────────────────────────────

    [Fact]
    public void MultipleAddSamplesCallsAccumulate()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        bwp.AddSamples(new byte[200], 0, 200);
        bwp.AddSamples(new byte[300], 0, 300);
        Assert.Equal(500, bwp.BufferedBytes);
    }

    [Fact]
    public void ReadFullyZeroFillsBufferWhenNothingEverWritten()
    {
        var bwp = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
        var buffer = new byte[] { 1, 2, 3, 4 };
        var bytesRead = bwp.Read(buffer.AsSpan());
        Assert.Equal(4, bytesRead);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, buffer);
    }
}

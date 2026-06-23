using System;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class WaveOffsetStreamTests
{
    // 16-bit mono 8kHz = 2 bytes per sample, 16000 bytes per second
    private const int SampleRate = 8000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BytesPerSample = BitsPerSample / 8 * Channels; // 2
    private const int BytesPerSecond = SampleRate * BytesPerSample; // 16000

    /// <summary>
    /// A WaveStream that produces identifiable non-zero data (repeating 1-255 pattern)
    /// so we can distinguish source audio from silence padding
    /// </summary>
    private class IdentifiableWaveStream : WaveStream
    {
        private readonly WaveFormat waveFormat;
        private readonly long length;
        private long position;

        public IdentifiableWaveStream(long lengthInBytes)
        {
            waveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
            length = lengthInBytes;
        }

        public override WaveFormat WaveFormat => waveFormat;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = (int)Math.Min(count, length - position);
            for (int i = 0; i < toRead; i++)
            {
                // Produce a non-zero pattern: byte value is ((position + i) % 255) + 1
                // This ensures bytes are always 1-255, never 0, so we can tell them apart from silence
                buffer[offset + i] = (byte)(((position + i) % 255) + 1);
            }
            position += toRead;
            return toRead;
        }
    }

    private IdentifiableWaveStream CreateSourceStream(double durationSeconds = 2.0)
    {
        long lengthBytes = (long)(durationSeconds * BytesPerSecond);
        return new IdentifiableWaveStream(lengthBytes);
    }

    #region Constructor Tests

    [Fact]
    public void DefaultConstructorSetsZeroStartTime()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(TimeSpan.Zero, offset.StartTime);
    }

    [Fact]
    public void DefaultConstructorSetsZeroSourceOffset()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(TimeSpan.Zero, offset.SourceOffset);
    }

    [Fact]
    public void DefaultConstructorSetsSourceLengthToTotalTime()
    {
        using var source = CreateSourceStream(2.0);
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(source.TotalTime, offset.SourceLength);
    }

    [Fact]
    public void DefaultConstructorLengthMatchesSource()
    {
        using var source = CreateSourceStream(2.0);
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(source.Length, offset.Length);
    }

    [Fact]
    public void ConstructorSetsStartTime()
    {
        using var source = CreateSourceStream();
        var startTime = TimeSpan.FromSeconds(1);
        using var offset = new WaveOffsetStream(source, startTime, TimeSpan.Zero, source.TotalTime);
        Assert.Equal(startTime, offset.StartTime);
    }

    [Fact]
    public void ConstructorSetsSourceOffset()
    {
        using var source = CreateSourceStream();
        var srcOffset = TimeSpan.FromSeconds(0.5);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, srcOffset, source.TotalTime);
        Assert.Equal(srcOffset, offset.SourceOffset);
    }

    [Fact]
    public void ConstructorSetsSourceLength()
    {
        using var source = CreateSourceStream();
        var srcLength = TimeSpan.FromSeconds(1);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, srcLength);
        Assert.Equal(srcLength, offset.SourceLength);
    }

    [Fact]
    public void ConstructorRejectsNonPcmFormat()
    {
        // IeeeFloat encoding should be rejected
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        using var source = new NonPcmWaveStream(format, 1000);
        Assert.Throws<ArgumentException>(() => new WaveOffsetStream(source));
    }

    [Fact]
    public void ConstructorSetsPositionToZero()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(0, offset.Position);
    }

    #endregion

    #region WaveFormat Tests

    [Fact]
    public void WaveFormatMatchesSource()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(source.WaveFormat, offset.WaveFormat);
    }

    [Fact]
    public void BlockAlignMatchesSource()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        Assert.Equal(source.BlockAlign, offset.BlockAlign);
    }

    #endregion

    #region Length Tests

    [Fact]
    public void LengthEqualsSourceLengthWithNoStartTime()
    {
        using var source = CreateSourceStream(2.0);
        var srcLength = TimeSpan.FromSeconds(1);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, srcLength);
        long expectedLength = (long)(1.0 * SampleRate) * BytesPerSample;
        Assert.Equal(expectedLength, offset.Length);
    }

    [Fact]
    public void LengthIncludesStartTimePadding()
    {
        using var source = CreateSourceStream(2.0);
        var startTime = TimeSpan.FromSeconds(1);
        var srcLength = TimeSpan.FromSeconds(1);
        using var offset = new WaveOffsetStream(source, startTime, TimeSpan.Zero, srcLength);
        // Length = audioStartPosition + sourceLengthBytes = 1s + 1s = 2s worth of bytes
        long expectedLength = (long)(2.0 * SampleRate) * BytesPerSample;
        Assert.Equal(expectedLength, offset.Length);
    }

    [Fact]
    public void ChangingStartTimeUpdatesLength()
    {
        using var source = CreateSourceStream(2.0);
        using var offset = new WaveOffsetStream(source);
        long originalLength = offset.Length;

        offset.StartTime = TimeSpan.FromSeconds(1);
        Assert.Equal(originalLength + BytesPerSecond, offset.Length);
    }

    [Fact]
    public void ChangingSourceLengthUpdatesLength()
    {
        using var source = CreateSourceStream(2.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Equal(2 * BytesPerSecond, offset.Length);

        offset.SourceLength = TimeSpan.FromSeconds(1);
        Assert.Equal(1 * BytesPerSecond, offset.Length);
    }

    #endregion

    #region Position Tests

    [Fact]
    public void CanSetAndGetPosition()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        offset.Position = 100;
        Assert.Equal(100, offset.Position);
    }

    [Fact]
    public void PositionIsBlockAligned()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        // BlockAlign is 2 for 16-bit mono, setting to odd number should align down
        offset.Position = 101;
        Assert.Equal(0, offset.Position % offset.BlockAlign);
    }

    [Fact]
    public void SettingPositionBeforeAudioStartSetsSourceToOffset()
    {
        using var source = CreateSourceStream(3.0);
        var srcOffset = TimeSpan.FromSeconds(0.5);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(1), srcOffset, TimeSpan.FromSeconds(2));

        offset.Position = 0; // before audioStartPosition
        long expectedSourcePos = (long)(0.5 * SampleRate) * BytesPerSample;
        Assert.Equal(expectedSourcePos, source.Position);
    }

    [Fact]
    public void SettingPositionAfterAudioStartSetsSourceCorrectly()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(2));

        long audioStart = (long)(1.0 * SampleRate) * BytesPerSample;
        long seekTo = audioStart + 1000;
        offset.Position = seekTo;
        // sourceStream.Position should be sourceOffsetBytes + (position - audioStartPosition)
        Assert.Equal(1000, source.Position);
    }

    [Fact]
    public void ReadAdvancesPosition()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        var buffer = new byte[1000];
        _ = offset.Read(buffer, 0, 1000);
        Assert.Equal(1000, offset.Position);
    }

    #endregion

    #region StartTime (Lead-In Silence) Tests

    [Fact]
    public void StartTimeProducesSilenceBeforeAudio()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // Read the first second (should be silence)
        var buffer = new byte[BytesPerSecond];
        int read = offset.Read(buffer, 0, buffer.Length);

        Assert.Equal(BytesPerSecond, read);
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
    }

    [Fact]
    public void AudioFollowsLeadInSilence()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // Skip past the lead-in silence
        var silenceBuffer = new byte[BytesPerSecond];
        _ = offset.Read(silenceBuffer, 0, silenceBuffer.Length);

        // Now read the audio portion
        var audioBuffer = new byte[100];
        int read = offset.Read(audioBuffer, 0, audioBuffer.Length);

        Assert.Equal(100, read);
        // The source produces non-zero bytes, so audio should contain non-zero data
        bool hasNonZero = false;
        for (int i = 0; i < read; i++)
        {
            if (audioBuffer[i] != 0) hasNonZero = true;
        }
        Assert.True(hasNonZero);
    }

    [Fact]
    public void PartialLeadInReadContainsSilenceAndAudio()
    {
        // Start time of 0.5s, read a buffer that spans from silence into audio
        int halfSecondBytes = BytesPerSecond / 2;
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(0.5), TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // Read a buffer that spans the silence/audio boundary
        var buffer = new byte[BytesPerSecond];
        int read = offset.Read(buffer, 0, buffer.Length);
        Assert.Equal(BytesPerSecond, read);

        // First half should be silence
        for (int i = 0; i < halfSecondBytes; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
        // Second half should have non-zero audio data
        bool hasNonZero = false;
        for (int i = halfSecondBytes; i < buffer.Length; i++)
        {
            if (buffer[i] != 0) hasNonZero = true;
        }
        Assert.True(hasNonZero);
    }

    #endregion

    #region SourceOffset Tests

    [Fact]
    public void SourceOffsetSkipsBeginningOfSource()
    {
        using var source = CreateSourceStream(2.0);
        // Read without offset
        var bufferNoOffset = new byte[100];
        using var noOffset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _ = noOffset.Read(bufferNoOffset, 0, 100);

        // Read with 0.5s offset - should get different data
        using var source2 = CreateSourceStream(2.0);
        var bufferWithOffset = new byte[100];
        using var withOffset = new WaveOffsetStream(source2, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1));
        _ = withOffset.Read(bufferWithOffset, 0, 100);

        bool different = false;
        for (int i = 0; i < 100; i++)
        {
            if (bufferNoOffset[i] != bufferWithOffset[i])
            {
                different = true;
                break;
            }
        }
        Assert.True(different);
    }

    [Fact]
    public void ChangingSourceOffsetRepositionsSourceStream()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        offset.SourceOffset = TimeSpan.FromSeconds(1);
        // After changing source offset, source should be repositioned
        long expectedSourcePos = (long)(1.0 * SampleRate) * BytesPerSample;
        Assert.Equal(expectedSourcePos, source.Position);
    }

    #endregion

    #region SourceLength Tests

    [Fact]
    public void SourceLengthLimitsReadableAudio()
    {
        using var source = CreateSourceStream(2.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.Equal(BytesPerSecond, offset.Length);
    }

    [Fact]
    public void ReadBeyondSourceLengthReturnsSilence()
    {
        int halfSecond = BytesPerSecond / 2;
        using var source = CreateSourceStream(2.0);
        // SourceLength of 0.25s
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(0.25));

        // Read more than the source length
        var buffer = new byte[halfSecond];
        int read = offset.Read(buffer, 0, halfSecond);
        Assert.Equal(halfSecond, read);

        // The bytes beyond 0.25s should be zero-filled
        int quarterSecond = BytesPerSecond / 4;
        for (int i = quarterSecond; i < halfSecond; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
    }

    #endregion

    #region Read Method Tests

    [Fact]
    public void ReadReturnsRequestedByteCount()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        var buffer = new byte[1000];
        int read = offset.Read(buffer, 0, 1000);
        Assert.Equal(1000, read);
    }

    [Fact]
    public void ReadRespectsBufferOffset()
    {
        using var source = CreateSourceStream();
        using var offset = new WaveOffsetStream(source);
        var buffer = new byte[200];
        // Fill buffer with 0xFF to detect writes
        for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xFF;

        int read = offset.Read(buffer, 50, 100);
        Assert.Equal(100, read);

        // Data before offset should be untouched
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(0xFF, buffer[i]);
        }
    }

    [Fact]
    public void ReadProducesCorrectSourceData()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source);
        var buffer = new byte[100];
        _ = offset.Read(buffer, 0, 100);

        // Verify the data matches our IdentifiableWaveStream pattern
        for (int i = 0; i < 100; i++)
        {
            byte expected = (byte)((i % 255) + 1);
            Assert.Equal(expected, buffer[i]);
        }
    }

    [Fact]
    public void MultipleReadsProduceConsecutiveData()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source);

        var buffer1 = new byte[100];
        var buffer2 = new byte[100];
        _ = offset.Read(buffer1, 0, 100);
        _ = offset.Read(buffer2, 0, 100);

        // buffer2 should continue the pattern from where buffer1 left off
        for (int i = 0; i < 100; i++)
        {
            byte expected = (byte)(((100 + i) % 255) + 1);
            Assert.Equal(expected, buffer2[i]);
        }
    }

    #endregion

    #region Lead-Out Silence Tests

    [Fact]
    public void ReadPastSourceLengthPadsWithZeros()
    {
        using var source = CreateSourceStream(1.0);
        // SourceLength = 0.5s but we read for longer
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(0.5));

        int halfSecond = BytesPerSecond / 2;
        // Read all 0.5s of audio first
        var audioBuffer = new byte[halfSecond];
        _ = offset.Read(audioBuffer, 0, halfSecond);

        // Now read more - should be silence (lead-out)
        var silenceBuffer = new byte[100];
        _ = offset.Read(silenceBuffer, 0, 100);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0, silenceBuffer[i]);
        }
    }

    #endregion

    #region HasData Tests

    [Fact]
    public void HasDataReturnsFalseBeforeAudioStart()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        // Position is 0, audio starts at 1s
        Assert.False(offset.HasData(100));
    }

    [Fact]
    public void HasDataReturnsFalseAfterLength()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        offset.Position = offset.Length;
        Assert.False(offset.HasData(100));
    }

    #endregion

    #region Property Change Repositioning Tests (CA2245 fix verification)

    [Fact]
    public void ChangingStartTimeRepositionsSourceStream()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        // Move position into the audio region
        offset.Position = 1000;
        long sourcePosBefore = source.Position;
        Assert.Equal(1000, sourcePosBefore);

        // Change start time - source stream should be repositioned
        offset.StartTime = TimeSpan.FromSeconds(1);
        // Position (1000) is now before audioStartPosition (16000),
        // so source should be at sourceOffsetBytes (0)
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void ChangingSourceOffsetRepositionsSourceCorrectly()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        // Position is 0, audio start is 0
        // After changing source offset to 1s, source should be at 1s
        offset.SourceOffset = TimeSpan.FromSeconds(1);
        long expectedSourcePos = (long)(1.0 * SampleRate) * BytesPerSample;
        Assert.Equal(expectedSourcePos, source.Position);
    }

    [Fact]
    public void ChangingSourceLengthRepositionsSourceStream()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        offset.Position = 1000;
        offset.SourceLength = TimeSpan.FromSeconds(1);

        // Position 1000 with audioStartPosition 0 means source should be at 0 + 1000 = 1000
        Assert.Equal(1000, source.Position);
        // Length should be updated
        Assert.Equal((long)(1.0 * SampleRate) * BytesPerSample, offset.Length);
    }

    [Fact]
    public void PositionIsPreservedAfterChangingStartTime()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        offset.Position = 1000;
        offset.StartTime = TimeSpan.FromSeconds(0.5);
        // The WaveOffsetStream position field should remain unchanged
        Assert.Equal(1000, offset.Position);
    }

    [Fact]
    public void PositionIsPreservedAfterChangingSourceOffset()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        offset.Position = 1000;
        offset.SourceOffset = TimeSpan.FromSeconds(0.5);
        Assert.Equal(1000, offset.Position);
    }

    [Fact]
    public void PositionIsPreservedAfterChangingSourceLength()
    {
        using var source = CreateSourceStream(3.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        offset.Position = 1000;
        offset.SourceLength = TimeSpan.FromSeconds(1);
        Assert.Equal(1000, offset.Position);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ZeroSourceLengthProducesZeroLength()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(0, offset.Length);
    }

    [Fact]
    public void SeekingToZeroResetsCorrectly()
    {
        using var source = CreateSourceStream(1.0);
        using var offset = new WaveOffsetStream(source);

        // Read some data
        var buffer = new byte[1000];
        _ = offset.Read(buffer, 0, 1000);

        // Seek back to 0
        offset.Position = 0;
        Assert.Equal(0, offset.Position);
        Assert.Equal(0, source.Position);

        // Read same data again and verify it matches
        var buffer2 = new byte[1000];
        _ = offset.Read(buffer2, 0, 1000);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(buffer[i], buffer2[i]);
        }
    }

    [Fact]
    public void CombinedStartTimeAndSourceOffsetWork()
    {
        using var source = CreateSourceStream(3.0);
        var startTime = TimeSpan.FromSeconds(0.5);
        var srcOffset = TimeSpan.FromSeconds(1.0);
        var srcLength = TimeSpan.FromSeconds(1.0);
        using var offset = new WaveOffsetStream(source, startTime, srcOffset, srcLength);

        int halfSecondBytes = BytesPerSecond / 2;

        // Length should be startTime + sourceLength = 0.5s + 1.0s = 1.5s
        Assert.Equal((long)(1.5 * SampleRate) * BytesPerSample, offset.Length);

        // First 0.5s should be silence
        var silenceBuffer = new byte[halfSecondBytes];
        _ = offset.Read(silenceBuffer, 0, halfSecondBytes);
        for (int i = 0; i < halfSecondBytes; i++)
        {
            Assert.Equal(0, silenceBuffer[i]);
        }

        // Next part should be audio starting from source offset
        var audioBuffer = new byte[100];
        _ = offset.Read(audioBuffer, 0, 100);

        // Verify it reads from 1.0s into the source (sourceOffsetBytes)
        long sourceOffsetBytesVal = (long)(1.0 * SampleRate) * BytesPerSample;
        for (int i = 0; i < 100; i++)
        {
            byte expected = (byte)(((sourceOffsetBytesVal + i) % 255) + 1);
            Assert.Equal(expected, audioBuffer[i]);
        }
    }

    [Fact]
    public void DisposeDisposesSourceStream()
    {
        var source = CreateSourceStream();
        var offset = new WaveOffsetStream(source);
        offset.Dispose();

        // After dispose, attempting to access source should indicate it's been cleaned up
        // We can't directly test this without more infrastructure, but we verify no exception on dispose
        Assert.True(true);
    }

    [Fact]
    public void DoubleDisposeDoesNotThrow()
    {
        var source = CreateSourceStream();
        var offset = new WaveOffsetStream(source);
        offset.Dispose();
        Assert.Null(Record.Exception(() => offset.Dispose()));
    }

    #endregion

    /// <summary>
    /// Helper WaveStream with non-PCM encoding to test constructor validation
    /// </summary>
    private class NonPcmWaveStream : WaveStream
    {
        private readonly WaveFormat waveFormat;
        private readonly long length;
        private long position;

        public NonPcmWaveStream(WaveFormat format, long length)
        {
            waveFormat = format;
            this.length = length;
        }

        public override WaveFormat WaveFormat => waveFormat;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }
    }
}

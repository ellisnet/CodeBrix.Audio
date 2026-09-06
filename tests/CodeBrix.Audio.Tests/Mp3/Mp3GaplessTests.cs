using System;
using System.Collections.Generic;
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
/// Tests for the MP3 reader's gapless handling: the encoder delay and padding declared in the
/// Xing/LAME header are removed, so that sample zero is the first sample of the audio that was
/// encoded and the length is the length of that audio.
/// </summary>
/// <remarks>
/// The fixture ships with the .wav it was encoded from, which is what makes these assertions
/// possible: a correct trim puts the two in step sample for sample. See
/// tests/Assets/audio/AUDIO-FIXTURES.txt.
/// </remarks>
public class Mp3GaplessTests
{
    private const string Mp3Fixture = "mp3-gapless-sweep-stereo-44100.mp3";
    private const string WavFixture = "mp3-gapless-sweep-stereo-44100.wav";

    /// <summary>
    /// The decoder's own latency, which the gapless convention adds to the encoder's declared
    /// delay. Not a number any file carries; every implementation assumes it.
    /// </summary>
    private const int DecoderDelaySamples = 529;

    /// <summary>One MP3 granule: the tolerance the trim has to beat, and does, by all of it.</summary>
    private const int OneGranule = 576;

    [Fact]
    public void Reader_reports_the_encoder_delay_and_padding_the_file_declares()
    {
        //Arrange
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        var header = reader.XingHeader;

        //Assert
        header.HasEncoderDelayInfo.Should().BeTrue();
        reader.EncoderDelay.Should().Be(576);
        reader.EncoderPadding.Should().BeGreaterThan(0);
        reader.EncoderPadding.Should().Be(header.EncoderPadding);
    }

    [Fact]
    public void Trimmed_length_is_exactly_the_length_of_the_wav_it_was_encoded_from()
    {
        //Arrange
        var wav = ReadMono(TestAssets.Path(WavFixture), out _);
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        long frames = reader.Length / BytesPerFrame(reader);

        //Assert
        frames.Should().Be(wav.Length);
    }

    [Fact]
    public void Trimmed_decode_lines_up_with_the_source_wav()
    {
        //Arrange
        var wav = ReadMono(TestAssets.Path(WavFixture), out _);
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        var decoded = DecodeMono(reader);
        int lag = BestLag(wav, decoded, 2 * (576 + DecoderDelaySamples));

        //Assert
        decoded.Length.Should().Be(wav.Length);
        Math.Abs(lag).Should().BeLessThan(OneGranule);
        lag.Should().Be(0);
    }

    [Fact]
    public void Turning_the_trim_off_puts_the_priming_and_the_padding_back()
    {
        //Arrange
        var wav = ReadMono(TestAssets.Path(WavFixture), out _);
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));
        reader.GaplessTrimming = false;
        int expectedLead = reader.EncoderDelay + DecoderDelaySamples;

        //Act
        var decoded = DecodeMono(reader);
        int lag = BestLag(wav, decoded, 2 * expectedLead);

        //Assert
        decoded.Length.Should().Be(wav.Length + reader.EncoderDelay + reader.EncoderPadding);
        lag.Should().Be(-expectedLead);
    }

    [Fact]
    public void Gapless_trimming_cannot_be_turned_off_once_the_stream_has_been_read()
    {
        //Arrange
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));
        var buffer = new byte[4096];
        _ = reader.Read(buffer, 0, buffer.Length);

        //Act
        Action act = () => reader.GaplessTrimming = false;

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Setting_gapless_trimming_to_what_it_already_is_is_not_a_change()
    {
        //Arrange
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));
        var buffer = new byte[4096];
        _ = reader.Read(buffer, 0, buffer.Length);

        //Act
        Action act = () => reader.GaplessTrimming = true;

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Position_zero_is_the_first_sample_of_the_original_audio()
    {
        //Arrange
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        long positionBeforeReading = reader.Position;
        var buffer = new byte[BytesPerFrame(reader)];
        _ = reader.Read(buffer, 0, buffer.Length);

        //Assert
        positionBeforeReading.Should().Be(0);
        reader.Position.Should().Be(BytesPerFrame(reader));
    }

    [Fact]
    public void Reading_stops_at_the_end_of_the_audio_rather_than_at_the_end_of_the_padding()
    {
        //Arrange
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        var decoded = DecodeMono(reader);

        //Assert
        reader.Position.Should().Be(reader.Length);
        ((long)decoded.Length).Should().Be(reader.Length / BytesPerFrame(reader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1152)]
    [InlineData(4 * 1152)]
    [InlineData(9 * 1152)]
    [InlineData(12345)]
    public void Seeking_lands_on_the_sample_it_names(int targetFrame)
    {
        // Both of the seek repairs are fenced here. A file with a Xing header used to be unable
        // to seek at all - its length was known from the header, the frame index was therefore
        // never scanned, and every seek fell through to "start again from the beginning". And a
        // seek to an exact frame boundary used to stop scanning one entry short of the frame it
        // needed, with the same result.

        //Arrange
        var sequential = DecodeSequentially(TestAssets.Path(Mp3Fixture), out int bytesPerFrame, out int channels);
        using var reader = new Mp3FileReader(TestAssets.Path(Mp3Fixture));

        //Act
        reader.Position = (long)targetFrame * bytesPerFrame;
        var afterSeek = ReadFrames(reader, 512);

        //Assert
        afterSeek.Length.Should().BeGreaterThan(0);
        SumOfSquaredDifference(sequential, targetFrame * channels, afterSeek).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void Seeking_lands_on_the_sample_it_names_in_a_file_with_no_xing_header()
    {
        //Arrange - the same audio with the header frame cut off, so the reader has to build its
        //          frame index by scanning and its length by estimating
        var headerless = WithoutTheHeaderFrame(File.ReadAllBytes(TestAssets.Path(Mp3Fixture)));
        var sequential = DecodeSequentially(new MemoryStream(headerless), out int bytesPerFrame, out int channels);
        using var stream = new MemoryStream(headerless);
        using var reader = new Mp3FileReader(stream);

        //Act
        reader.Position = 4L * 1152 * bytesPerFrame;
        var afterSeek = ReadFrames(reader, 512);

        //Assert
        reader.XingHeader.Should().BeNull();
        afterSeek.Length.Should().BeGreaterThan(0);
        SumOfSquaredDifference(sequential, 4 * 1152 * channels, afterSeek).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void Length_excludes_the_declared_delay_and_padding_to_the_sample()
    {
        //Arrange - a hand-built file, so the arithmetic can be checked against known numbers
        //          without a real codec in the way
        const int delay = 576;
        const int padding = 1464;
        int audioFrames = SyntheticMp3.AudioFramesForSeconds(2.0);
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(2.0, delay, padding);
        using var stream = new MemoryStream(bytes);
        using var reader = new Mp3FileReaderBase(stream, format => new FakeMp3FrameDecompressor(format));
        int bytesPerFrame = BytesPerFrame(reader);

        //Act
        long trimmedFrames = reader.Length / bytesPerFrame;

        //Assert
        reader.EncoderDelay.Should().Be(delay);
        reader.EncoderPadding.Should().Be(padding);
        trimmedFrames.Should().Be((audioFrames * 1152L) - delay - padding);
    }

    [Fact]
    public void A_file_that_declares_no_delay_or_padding_is_not_trimmed()
    {
        //Arrange
        int audioFrames = SyntheticMp3.AudioFramesForSeconds(2.0);
        var bytes = SyntheticMp3.CreateBytesWithInfoHeader(2.0);
        using var stream = new MemoryStream(bytes);
        using var reader = new Mp3FileReaderBase(stream, format => new FakeMp3FrameDecompressor(format));

        //Act
        long frames = reader.Length / BytesPerFrame(reader);

        //Assert
        reader.EncoderDelay.Should().Be(0);
        frames.Should().Be(audioFrames * 1152L);
    }

    [Fact]
    public void A_padding_smaller_than_the_decoder_delay_does_not_make_the_length_negative()
    {
        //Arrange - the trim can never ask for more audio than the decoder produced
        int audioFrames = SyntheticMp3.AudioFramesForSeconds(0.5);
        var bytes = SyntheticMp3.CreateBytesWithLameHeader(0.5, 576, 1);
        using var stream = new MemoryStream(bytes);
        using var reader = new Mp3FileReaderBase(stream, format => new FakeMp3FrameDecompressor(format));

        //Act
        long frames = reader.Length / BytesPerFrame(reader);

        //Assert
        frames.Should().Be((audioFrames * 1152L) - 576 - DecoderDelaySamples);
    }

    [Fact]
    public void A_frame_the_bit_reservoir_cannot_satisfy_is_silence_rather_than_a_hole()
    {
        // A Layer III frame can point back into earlier frames for its main data. When that
        // reach is longer than what the reservoir holds, the frame cannot be decoded - but
        // dropping it shortens the stream and pulls everything after it earlier in time, which
        // is drift, not a glitch. It has to come out as silence of the right length instead.

        //Arrange - the trim is off so that decoded sample N is decoded frame N, which is what
        //          makes it possible to point at the frame that was patched
        const int patchedFrame = 1;
        var original = File.ReadAllBytes(TestAssets.Path(Mp3Fixture));
        var patched = WithUnsatisfiableMainDataBegin(original, patchedFrame);
        var before = DecodeUntrimmed(original, out int channels);

        //Act
        var after = DecodeUntrimmed(patched, out _);

        //Assert
        after.Length.Should().Be(before.Length);
        PeakBetween(before, (patchedFrame - 1) * 1152 * channels, 1152 * channels).Should().BeGreaterThan(0f);
        PeakBetween(after, (patchedFrame - 1) * 1152 * channels, 1152 * channels).Should().Be(0f);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static int BytesPerFrame(WaveStream reader) =>
        reader.WaveFormat.BitsPerSample / 8 * reader.WaveFormat.Channels;

    /// <summary>Every sample of a file, interleaved, decoded through the ordinary reader.</summary>
    private static float[] DecodeSequentially(string path, out int bytesPerFrame, out int channels)
    {
        using var reader = new Mp3FileReader(path);
        bytesPerFrame = BytesPerFrame(reader);
        channels = reader.WaveFormat.Channels;
        return ReadFrames(reader, int.MaxValue);
    }

    /// <summary>
    /// Every sample of a file with the gapless trim off, so that decoded frame N really is the
    /// Nth audio frame of the file rather than the Nth frame minus the encoder's priming.
    /// </summary>
    private static float[] DecodeUntrimmed(byte[] bytes, out int channels)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new Mp3FileReader(stream);
        reader.GaplessTrimming = false;
        channels = reader.WaveFormat.Channels;
        return ReadFrames(reader, int.MaxValue);
    }

    private static float[] DecodeSequentially(Stream stream, out int bytesPerFrame, out int channels)
    {
        using (stream)
        {
            using var reader = new Mp3FileReader(stream);
            bytesPerFrame = BytesPerFrame(reader);
            channels = reader.WaveFormat.Channels;
            return ReadFrames(reader, int.MaxValue);
        }
    }

    private static float[] ReadFrames(WaveStream reader, int maximumFrames)
    {
        int bytesPerFrame = BytesPerFrame(reader);
        var samples = new List<float>();
        var buffer = new byte[8192 - (8192 % bytesPerFrame)];
        int read;
        while (samples.Count / reader.WaveFormat.Channels < maximumFrames
               && (read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i + 4 <= read; i += 4) { samples.Add(BitConverter.ToSingle(buffer, i)); }
        }
        return samples.ToArray();
    }

    private static float[] DecodeMono(Mp3FileReader reader)
    {
        int channels = reader.WaveFormat.Channels;
        var interleaved = ReadFrames(reader, int.MaxValue);
        var mono = new float[interleaved.Length / channels];
        for (int f = 0; f < mono.Length; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) { sum += interleaved[(f * channels) + c]; }
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float[] ReadMono(string path, out int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;
        var buffer = new float[channels * 4096];
        var mono = new List<float>();
        int read;
        while ((read = reader.Read(buffer.AsSpan())) > 0)
        {
            for (int i = 0; i + channels <= read; i += channels)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) { sum += buffer[i + c]; }
                mono.Add(sum / channels);
            }
        }
        return mono.ToArray();
    }

    /// <summary>The lag, in samples, at which <paramref name="candidate"/> best matches
    /// <paramref name="reference"/>. Zero means they start at the same instant.</summary>
    private static int BestLag(float[] reference, float[] candidate, int maximumLag)
    {
        double best = double.NegativeInfinity;
        int bestLag = 0;
        int length = Math.Min(reference.Length, candidate.Length);
        for (int lag = -maximumLag; lag <= maximumLag; lag++)
        {
            double sum = 0.0;
            for (int i = Math.Max(0, lag); i < length && i - lag < candidate.Length; i++)
            {
                sum += reference[i] * candidate[i - lag];
            }
            if (sum > best) { best = sum; bestLag = lag; }
        }
        return bestLag;
    }

    private static double SumOfSquaredDifference(float[] reference, int offset, float[] candidate)
    {
        double total = 0.0;
        for (int i = 0; i < candidate.Length && offset + i < reference.Length; i++)
        {
            double difference = reference[offset + i] - candidate[i];
            total += difference * difference;
        }
        return total;
    }

    private static float PeakBetween(float[] samples, int start, int count)
    {
        float peak = 0f;
        for (int i = start; i < start + count && i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        return peak;
    }

    /// <summary>The file offsets of every MP3 frame in a buffer, in order.</summary>
    private static List<long> FrameOffsets(byte[] bytes)
    {
        var offsets = new List<long>();
        using var stream = new MemoryStream(bytes);
        Mp3Frame frame;
        while ((frame = Mp3Frame.LoadFromStream(stream, readData: false)) != null)
        {
            offsets.Add(frame.FileOffset);
        }
        return offsets;
    }

    /// <summary>The same bytes with the leading Xing/Info frame removed.</summary>
    private static byte[] WithoutTheHeaderFrame(byte[] bytes)
    {
        var offsets = FrameOffsets(bytes);
        int start = (int)offsets[1];
        var result = new byte[bytes.Length - start];
        Array.Copy(bytes, start, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// The same bytes with one frame's main_data_begin - the first nine bits of the side
    /// information - set to its maximum of 511, which is further back than the reservoir can
    /// possibly hold that early in a file.
    /// </summary>
    private static byte[] WithUnsatisfiableMainDataBegin(byte[] bytes, int frameIndex)
    {
        var offsets = FrameOffsets(bytes);
        var result = (byte[])bytes.Clone();
        int sideInfo = (int)offsets[frameIndex] + 4;
        result[sideInfo] = 0xFF;
        result[sideInfo + 1] |= 0x80;
        return result;
    }
}

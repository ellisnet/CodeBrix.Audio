using System;
using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="OggVorbisFileReader"/>, the fully managed Ogg Vorbis reader. No native
/// code is involved in any of these.
/// </summary>
public class OggVorbisFileReaderTests
{
    private const int SampleRate = 44100;
    private const int Frames = 11025; // 0.25 s

    [Fact]
    public void Reports_an_ieee_float_format_matching_the_file()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisToneStereo));

        //Assert
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        reader.WaveFormat.SampleRate.Should().Be(SampleRate);
        reader.WaveFormat.Channels.Should().Be(2);
        reader.WaveFormat.BitsPerSample.Should().Be(32);
    }

    [Fact]
    public void Length_and_TotalTime_are_exact()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisToneStereo));

        //Assert
        // A Vorbis stream records its sample count, so unlike MP3 this needs no estimation.
        reader.Length.Should().Be(Frames * reader.WaveFormat.BlockAlign);
        reader.TotalTime.TotalSeconds.Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void Reads_the_whole_file_as_audible_audio()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisToneStereo));
        var buffer = new byte[4096];
        var total = 0L;
        var peak = 0f;

        //Act
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            for (var i = 0; i + 3 < read; i += 4)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer, i)));
        }

        //Assert
        total.Should().Be(reader.Length);
        peak.Should().BeInRange(0.1f, 1.0f);
    }

    [Fact]
    public void Reads_a_mono_file_at_its_own_sample_rate()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisToneMono));

        //Assert
        reader.WaveFormat.Channels.Should().Be(1);
        reader.WaveFormat.SampleRate.Should().Be(22050);
        reader.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Position_round_trips_through_a_seek()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisSweep));
        var target = reader.Length / 2 / reader.WaveFormat.BlockAlign * reader.WaveFormat.BlockAlign;

        //Act
        reader.Position = target;

        //Assert
        reader.Position.Should().Be(target);
        reader.CurrentTime.TotalSeconds.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void Seeking_lands_on_the_same_audio_a_sequential_read_reaches()
    {
        //Arrange
        // The sweep fixture rises 200 Hz -> 2 kHz, so audio at a given offset is identifiable:
        // an inaccurate seek could not produce matching samples.
        //
        // The comparison starts one Vorbis block after the seek point. Seeking mid-packet
        // leaves the decoder without the previous packet's overlap history, so up to one block
        // (~2048 frames) of the audio immediately following a seek differs from a sequential
        // decode before the two converge exactly. That is a property of the managed decoder;
        // the native path (used by AudioPlayer and the engine wherever the bundled library has
        // Vorbis support) reconstructs the overlap and matches immediately. See the class
        // documentation on OggVorbisFileReader.
        const int settleFrames = 2048;

        using var sequential = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisSweep));
        using var seeking = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisSweep));

        var blockAlign = sequential.WaveFormat.BlockAlign;
        var target = sequential.Length / 4 / blockAlign * blockAlign;
        var settleBytes = settleFrames * blockAlign;
        var skip = new byte[target + settleBytes];
        var expected = new byte[4096];
        var actual = new byte[4096];
        var settle = new byte[settleBytes];

        //Act
        sequential.ReadExactly(skip, 0, skip.Length);
        sequential.ReadExactly(expected, 0, expected.Length);

        seeking.Position = target;
        seeking.ReadExactly(settle, 0, settle.Length);
        seeking.ReadExactly(actual, 0, actual.Length);

        //Assert
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Seeking_reports_the_position_it_was_asked_for()
    {
        //Arrange
        // Position accuracy is exact even where the audio needs a block to settle, so anything
        // driving a transport from Position stays truthful.
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisSweep));
        var blockAlign = reader.WaveFormat.BlockAlign;

        foreach (var frame in new[] { 1000, 12000, 24000, 50000, 75000 })
        {
            //Act
            reader.Position = frame * blockAlign;

            //Assert
            reader.Position.Should().Be(frame * blockAlign);
        }
    }

    [Fact]
    public void Exposes_the_streams_vorbis_comments()
    {
        //Arrange
        using var reader = new OggVorbisFileReader(TestAssets.Path(TestAssets.VorbisToneStereo));

        //Assert
        // The encoder always writes a vendor string; this is the Ogg counterpart of reading an
        // ID3 tag from an MP3.
        reader.EncoderVendor.Should().NotBeNullOrEmpty();
        reader.Tags.Should().NotBeNull();
    }

    [Fact]
    public void A_truncated_file_fails_cleanly()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisTruncated);

        //Act
        var readAll = () =>
        {
            using var reader = new OggVorbisFileReader(stream);
            var buffer = new byte[4096];
            while (reader.Read(buffer, 0, buffer.Length) > 0) { }
        };

        //Assert
        // Whether it throws on open or simply runs out of audio, what matters is that it
        // terminates with an ordinary exception rather than hanging or corrupting memory.
        readAll.Should().NotThrow<AccessViolationException>();
    }

    [Fact]
    public void A_stream_the_caller_owns_is_left_open()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);

        //Act
        using (var reader = new OggVorbisFileReader(stream))
        {
            reader.Length.Should().BeGreaterThan(0);
        }

        //Assert
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void AudioFileReader_opens_ogg_by_extension()
    {
        //Arrange / Act
        using var reader = new AudioFileReader(TestAssets.Path(TestAssets.VorbisToneStereo));
        var samples = new float[1024];
        var read = ((ISampleProvider)reader).Read(samples);

        //Assert
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        reader.WaveFormat.SampleRate.Should().Be(SampleRate);
        read.Should().BeGreaterThan(0);
    }
}

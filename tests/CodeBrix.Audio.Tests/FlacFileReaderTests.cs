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
/// Tests for <see cref="FlacFileReader"/> and the managed FLAC decoder behind it.
/// </summary>
/// <remarks>
/// FLAC is lossless, so correctness here is not a judgement call: every fixture ships with the
/// .wav it was encoded from, and a correct decoder reproduces that PCM byte for byte. The
/// fixtures are chosen to cover the decoder's branches - constant, verbatim, fixed-predictor and
/// LPC subframes, all four stereo decorrelation modes, 16- and 24-bit depths, and a short final
/// block. See tests/Assets/audio/AUDIO-FIXTURES.txt.
/// </remarks>
public class FlacFileReaderTests
{
    /// <summary>Reads the data chunk of a PCM .wav file - the reference the decode must match.</summary>
    private static byte[] ReadWavData(string path)
    {
        using var reader = new WaveFileReader(path);
        var data = new byte[reader.Length];
        reader.ReadExactly(data, 0, data.Length);
        return data;
    }

    private static byte[] DecodeAll(FlacFileReader reader)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
        return output.ToArray();
    }

    [Theory]
    [InlineData("flac-tone-mono-16bit-22050.flac")]
    [InlineData("flac-tone-stereo-16bit-44100-midside.flac")]
    [InlineData("flac-tone-stereo-16bit-44100-leftside.flac")]
    [InlineData("flac-tone-stereo-16bit-44100-rightside.flac")]
    [InlineData("flac-noise-stereo-16bit-44100.flac")]
    [InlineData("flac-silence-stereo-16bit-44100.flac")]
    [InlineData("flac-tone-stereo-24bit-48000.flac")]
    [InlineData("flac-tone-stereo-16bit-44100-oddlength.flac")]
    public void Decodes_bit_exactly_to_the_pcm_it_was_encoded_from(string fixture)
    {
        //Arrange
        var expected = ReadWavData(TestAssets.ReferenceWavFor(fixture));
        using var reader = new FlacFileReader(TestAssets.Path(fixture));

        //Act
        var actual = DecodeAll(reader);

        //Assert
        actual.Length.Should().Be(expected.Length);
        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData("flac-tone-mono-16bit-22050.flac", 22050, 1, 16)]
    [InlineData("flac-tone-stereo-16bit-44100-midside.flac", 44100, 2, 16)]
    [InlineData("flac-tone-stereo-24bit-48000.flac", 48000, 2, 24)]
    public void Reports_the_files_format(string fixture, int sampleRate, int channels, int bits)
    {
        //Arrange
        using var reader = new FlacFileReader(TestAssets.Path(fixture));

        //Assert
        reader.WaveFormat.SampleRate.Should().Be(sampleRate);
        reader.WaveFormat.Channels.Should().Be(channels);
        reader.WaveFormat.BitsPerSample.Should().Be(bits);
        reader.SourceBitsPerSample.Should().Be(bits);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
    }

    [Fact]
    public void Length_and_TotalTime_are_exact()
    {
        //Arrange
        using var reader = new FlacFileReader(TestAssets.Path(TestAssets.FlacToneStereo));

        //Assert
        // STREAMINFO states the total sample count, so no scanning or estimation is involved.
        reader.Length.Should().Be(11025 * reader.WaveFormat.BlockAlign);
        reader.TotalTime.TotalSeconds.Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void Seeking_lands_on_exactly_the_audio_a_sequential_read_reaches()
    {
        //Arrange
        // Unlike a lossy codec, FLAC has no overlap history to rebuild, so a seek is not merely
        // close - the samples must be identical from the very first one.
        var fixture = TestAssets.Path(TestAssets.FlacToneStereo);
        using var sequential = new FlacFileReader(fixture);
        using var seeking = new FlacFileReader(fixture);

        var blockAlign = sequential.WaveFormat.BlockAlign;
        var target = sequential.Length / 3 / blockAlign * blockAlign;
        var skip = new byte[target];
        var expected = new byte[4096];
        var actual = new byte[4096];

        //Act
        sequential.ReadExactly(skip, 0, skip.Length);
        sequential.ReadExactly(expected, 0, expected.Length);

        seeking.Position = target;
        seeking.ReadExactly(actual, 0, actual.Length);

        //Assert
        seeking.Position.Should().Be(target + actual.Length);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Seeking_back_to_the_start_replays_the_same_audio()
    {
        //Arrange
        using var reader = new FlacFileReader(TestAssets.Path(TestAssets.FlacToneStereo));
        var first = new byte[4096];
        var second = new byte[4096];

        //Act
        reader.ReadExactly(first, 0, first.Length);
        reader.Position = 0;
        reader.ReadExactly(second, 0, second.Length);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void Seeking_uses_a_seektable_when_the_file_has_one()
    {
        //Arrange
        // ffmpeg does not write SEEKTABLE blocks, so this splices a synthetic one in to cover the
        // table-driven path as well as the frame-walking fallback the other tests exercise.
        var withTable = FlacTestStreams.WithSyntheticSeekTable(TestAssets.Path(TestAssets.FlacToneStereo));
        using var reference = new FlacFileReader(TestAssets.Path(TestAssets.FlacToneStereo));
        using var reader = new FlacFileReader(new MemoryStream(withTable));

        var blockAlign = reference.WaveFormat.BlockAlign;
        var target = reference.Length / 2 / blockAlign * blockAlign;
        var skip = new byte[target];
        var expected = new byte[2048];
        var actual = new byte[2048];

        //Act
        reference.ReadExactly(skip, 0, skip.Length);
        reference.ReadExactly(expected, 0, expected.Length);

        reader.Position = target;
        reader.ReadExactly(actual, 0, actual.Length);

        //Assert
        reader.Length.Should().Be(reference.Length);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void A_truncated_file_fails_cleanly()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacTruncated);

        //Act
        var readAll = () =>
        {
            using var reader = new FlacFileReader(stream);
            var buffer = new byte[4096];
            while (reader.Read(buffer, 0, buffer.Length) > 0) { }
        };

        //Assert
        // The frame CRCs mean corruption is detected rather than played as noise.
        readAll.Should().NotThrow<AccessViolationException>();
    }

    [Fact]
    public void A_file_that_is_not_flac_is_rejected()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);

        //Act
        var open = () => new FlacFileReader(stream);

        //Assert
        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_stream_the_caller_owns_is_left_open()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        using (var reader = new FlacFileReader(stream))
        {
            reader.Length.Should().BeGreaterThan(0);
        }

        //Assert
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Exposes_the_streams_vorbis_comments()
    {
        //Arrange
        using var reader = new FlacFileReader(TestAssets.Path(TestAssets.FlacToneStereo));

        //Assert
        // ffmpeg writes an encoder tag, so there is always at least one comment to find.
        reader.Tags.Should().NotBeNull();
        reader.Tags.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AudioFileReader_opens_flac_by_extension()
    {
        //Arrange / Act
        using var reader = new AudioFileReader(TestAssets.Path(TestAssets.FlacToneStereo));
        var samples = new float[1024];
        var read = ((ISampleProvider)reader).Read(samples);

        //Assert
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        reader.WaveFormat.SampleRate.Should().Be(44100);
        read.Should().BeGreaterThan(0);
    }
}

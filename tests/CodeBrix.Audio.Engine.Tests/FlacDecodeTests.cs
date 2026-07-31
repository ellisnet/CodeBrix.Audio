using System;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Metadata;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Device-less tests for FLAC decoding through the native codebrix_miniaudio backend.
/// </summary>
/// <remarks>
/// FLAC has always been available on this path - miniaudio compiles in dr_flac and the codec
/// factory has always advertised it - but nothing exercised it. These tests pin that down, and
/// they are the reference the managed FLAC decoder in CodeBrix.Audio is measured against.
/// </remarks>
public class FlacDecodeTests
{
    private const int SampleRate = 44100;
    private const int Frames = 11025; // 0.25 s

    [Fact]
    public void Reports_the_files_channels_and_sample_rate()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, SampleRate);

        //Assert
        decoder.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(SampleRate);
    }

    [Fact]
    public void Length_is_exact()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, SampleRate);

        //Assert
        // FLAC records the total sample count in its STREAMINFO block, so unlike Vorbis this has
        // always been exact - no memory/pull-mode trick required.
        decoder.Length.Should().Be(Frames * 2);
    }

    [Fact]
    public void Decodes_the_whole_file_as_audible_audio()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, SampleRate);
        var buffer = new float[4096];
        var total = 0;
        var peak = 0f;

        //Act
        int n;
        while ((n = decoder.Decode(buffer)) > 0)
        {
            for (var i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
            total += n;
        }

        //Assert
        total.Should().Be(Frames * 2);
        peak.Should().BeInRange(0.1f, 1.0f);
    }

    [Fact]
    public void Seek_lands_on_the_same_audio_a_sequential_decode_reaches()
    {
        //Arrange
        using var sequentialStream = TestAssets.Open(TestAssets.FlacToneStereo);
        using var sequential = new MiniAudioDecoder(sequentialStream, SampleFormat.F32, 2, SampleRate);
        using var seekStream = TestAssets.Open(TestAssets.FlacToneStereo);
        using var seeking = new MiniAudioDecoder(seekStream, SampleFormat.F32, 2, SampleRate);

        var target = (sequential.Length / 4) & ~1;
        var skip = new float[target];
        var expected = new float[2048];
        var actual = new float[2048];

        //Act
        var skipped = 0;
        while (skipped < target)
        {
            var n = sequential.Decode(skip.AsSpan(skipped, target - skipped));
            if (n <= 0) break;
            skipped += n;
        }

        sequential.Decode(expected);
        seeking.Seek(target).Should().BeTrue();
        seeking.Decode(actual);

        //Assert
        for (var i = 0; i < expected.Length; i++)
            actual[i].Should().BeApproximately(expected[i], 1e-6f);
    }

    [Fact]
    public void The_codec_factory_offers_a_decoder_for_the_flac_format_id()
    {
        //Arrange
        var factory = new MiniAudioCodecFactory();
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);
        var format = new AudioFormat { Format = SampleFormat.F32, Channels = 2, SampleRate = SampleRate };

        //Act
        using var decoder = factory.CreateDecoder(stream, "flac", format);

        //Assert
        factory.SupportedFormatIds.Should().Contain("flac");
        decoder.Should().NotBeNull();
        decoder!.Length.Should().Be(Frames * 2);
    }

    [Fact]
    public void Metadata_reports_the_exact_duration()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);
        var options = new ReadOptions { ReadTags = false, DurationAccuracy = DurationAccuracy.FastEstimate };

        //Act
        var result = SoundMetadataReader.Read(stream, options);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.FormatIdentifier.Should().Be("flac");
        result.Value.SampleRate.Should().Be(SampleRate);
        result.Value.ChannelCount.Should().Be(2);
        result.Value.Duration.TotalSeconds.Should().BeApproximately(0.25, 0.001);
    }
}

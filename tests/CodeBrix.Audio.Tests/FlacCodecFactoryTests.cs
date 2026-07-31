using System;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="FlacCodecFactory"/> - the fully managed FLAC decoder the audio engine
/// falls back to - including a direct comparison against the native decoder.
/// </summary>
public class FlacCodecFactoryTests
{
    private static AudioFormat Format(int channels, int sampleRate) => new()
    {
        Format = SampleFormat.F32,
        Channels = channels,
        Layout = AudioFormat.GetLayoutFromChannels(channels),
        SampleRate = sampleRate
    };

    [Fact]
    public void Sits_below_the_native_factory_so_it_is_only_a_fallback()
    {
        //Arrange
        var factory = new FlacCodecFactory();

        //Assert
        factory.Priority.Should().BeLessThan(0);
        factory.SupportedFormatIds.Should().Equal("flac");
        factory.FactoryId.Should().Be("CodeBrix.Audio.ManagedFlac");
    }

    [Fact]
    public void Creates_a_decoder_for_a_flac_stream()
    {
        //Arrange
        var factory = new FlacCodecFactory();
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        using var decoder = factory.CreateDecoder(stream, "flac", Format(2, 44100));

        //Assert
        decoder.Should().NotBeNull();
        decoder!.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(44100);
        decoder.Length.Should().Be(11025 * 2);
    }

    [Fact]
    public void Declines_a_stream_that_is_not_flac()
    {
        //Arrange
        var factory = new FlacCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);

        //Act
        var decoder = factory.CreateDecoder(stream, "flac", Format(2, 44100));

        //Assert
        decoder.Should().BeNull();
    }

    [Theory]
    [InlineData("flac-tone-stereo-16bit-44100-midside.flac", 2, 44100)]
    [InlineData("flac-tone-stereo-24bit-48000.flac", 2, 48000)]
    [InlineData("flac-noise-stereo-16bit-44100.flac", 2, 44100)]
    [InlineData("flac-tone-mono-16bit-22050.flac", 1, 22050)]
    public void Agrees_with_the_native_decoder_sample_for_sample(string fixture, int channels, int rate)
    {
        //Arrange
        // Two independent implementations of the same lossless format: the managed decoder
        // written here, and dr_flac inside the bundled native library. On a lossless format
        // "close enough" is not a thing - if these disagree, one of them is wrong.
        var format = Format(channels, rate);

        using var managedStream = TestAssets.Open(fixture);
        using var managed = new FlacCodecFactory().CreateDecoder(managedStream, "flac", format);

        using var nativeStream = TestAssets.Open(fixture);
        using var native = new MiniAudioCodecFactory().CreateDecoder(nativeStream, "flac", format);

        var managedBuffer = new float[4096];
        var nativeBuffer = new float[4096];
        var compared = 0;

        //Act / Assert
        while (true)
        {
            var managedRead = managed!.Decode(managedBuffer);
            var nativeRead = native!.Decode(nativeBuffer);

            managedRead.Should().Be(nativeRead);
            if (managedRead == 0) break;

            for (var i = 0; i < managedRead; i++)
            {
                // Both scale integer PCM to float by the same full-scale divisor, so the values
                // are identical bit patterns rather than merely similar.
                managedBuffer[i].Should().BeApproximately(nativeBuffer[i], 1e-7f);
            }

            compared += managedRead;
        }

        compared.Should().Be(managed!.Length);
    }

    [Fact]
    public void Resamples_a_22khz_file_to_the_engines_48khz_output()
    {
        //Arrange
        var factory = new FlacCodecFactory();
        using var stream = TestAssets.Open("flac-tone-mono-16bit-22050.flac");

        //Act
        using var decoder = factory.CreateDecoder(stream, "flac", Format(2, 48000));
        var buffer = new float[8192];
        var total = 0;
        int n;
        while ((n = decoder!.Decode(buffer)) > 0) total += n;

        //Assert
        decoder!.SampleRate.Should().Be(48000);
        decoder.Channels.Should().Be(2);
        decoder.Length.Should().BeInRange(23900, 24100);
        total.Should().BeInRange(decoder.Length - 64, decoder.Length + 64);
    }

    [Fact]
    public void Seeking_returns_to_identical_audio()
    {
        //Arrange
        // FLAC is lossless and has no overlap history, so a seek back to the start must replay
        // exactly the same samples.
        var factory = new FlacCodecFactory();
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);
        using var decoder = factory.CreateDecoder(stream, "flac", Format(2, 44100));
        var first = new float[2048];
        var second = new float[2048];

        //Act
        decoder!.Decode(first);
        var seeked = decoder.Seek(0);
        decoder.Decode(second);

        //Assert
        seeked.Should().BeTrue();
        second.Should().Equal(first);
    }

    [Fact]
    public void Probing_detects_the_files_own_format()
    {
        //Arrange
        var factory = new FlacCodecFactory();
        using var stream = TestAssets.Open("flac-tone-stereo-24bit-48000.flac");

        //Act
        using var decoder = factory.TryCreateDecoder(stream, out var detected);

        //Assert
        decoder.Should().NotBeNull();
        detected.Channels.Should().Be(2);
        detected.SampleRate.Should().Be(48000);
        detected.Format.Should().Be(SampleFormat.F32);
    }
}

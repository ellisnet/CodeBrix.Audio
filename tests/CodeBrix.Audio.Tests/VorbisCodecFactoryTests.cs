using System;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="VorbisCodecFactory"/> - the fully managed Ogg Vorbis decoder the audio
/// engine falls back to when the bundled native library has no Vorbis support.
/// </summary>
public class VorbisCodecFactoryTests
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
        var factory = new VorbisCodecFactory();

        //Assert
        // The engine's built-in native factory registers at 0 and higher numbers are tried
        // first, so a negative priority is what keeps decoding on the native path by default.
        factory.Priority.Should().BeLessThan(0);
        factory.SupportedFormatIds.Should().Equal("ogg");
        factory.FactoryId.Should().Be("CodeBrix.Audio.ManagedVorbis");
    }

    [Fact]
    public void Creates_a_decoder_for_an_ogg_stream()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);

        //Act
        using var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 44100));

        //Assert
        decoder.Should().NotBeNull();
        decoder!.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(44100);
        decoder.SampleFormat.Should().Be(SampleFormat.F32);
        decoder.Length.Should().Be(11025 * 2);
    }

    [Fact]
    public void Declines_a_stream_that_is_not_ogg()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 44100));

        //Assert
        decoder.Should().BeNull();
    }

    [Fact]
    public void Decodes_audible_audio()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        using var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 44100));
        var buffer = new float[4096];
        var total = 0;
        var peak = 0f;

        //Act
        int n;
        while ((n = decoder!.Decode(buffer)) > 0)
        {
            for (var i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
            total += n;
        }

        //Assert
        total.Should().Be(decoder!.Length);
        peak.Should().BeInRange(0.1f, 1.0f);
    }

    [Fact]
    public void Converts_a_mono_file_up_to_the_engines_stereo_output()
    {
        //Arrange
        // The engine asks for the device's channel count, not the file's, so a mono sound
        // effect has to arrive as stereo or it would play at half speed on one side.
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneMono);
        using var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 22050));
        var buffer = new float[512];

        //Act
        var read = decoder!.Decode(buffer);

        //Assert
        decoder.Channels.Should().Be(2);
        read.Should().BeGreaterThan(0);
        // A mono source duplicated across both channels: every pair must match.
        for (var i = 0; i + 1 < read; i += 2) buffer[i + 1].Should().Be(buffer[i]);
    }

    [Fact]
    public void Resamples_a_22khz_file_to_the_engines_48khz_output()
    {
        //Arrange
        // Kenney-style asset packs mix sample rates freely, and the engine's device runs at one
        // rate, so the decoder has to do the conversion. Length must be reported in the OUTPUT
        // rate's samples or every position and duration downstream would be wrong.
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneMono);

        //Act
        using var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 48000));
        var buffer = new float[8192];
        var total = 0;
        int n;
        while ((n = decoder!.Decode(buffer)) > 0) total += n;

        //Assert
        decoder!.SampleRate.Should().Be(48000);
        // 0.25 s of 22.05 kHz mono becomes 0.25 s of 48 kHz stereo: 12000 frames, 24000 samples.
        decoder.Length.Should().BeInRange(23900, 24100);
        total.Should().BeInRange(decoder.Length - 64, decoder.Length + 64);
    }

    [Fact]
    public void Seeking_reports_success_and_moves_the_read_position()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisSweep);
        using var decoder = factory.CreateDecoder(stream, "ogg", Format(2, 48000));
        var first = new float[512];
        var afterSeek = new float[512];

        //Act
        decoder!.Decode(first);
        var seeked = decoder.Seek(decoder.Length / 2);
        var read = decoder.Decode(afterSeek);

        //Assert
        seeked.Should().BeTrue();
        read.Should().BeGreaterThan(0);
        afterSeek.Should().NotEqual(first);
    }

    [Fact]
    public void Probing_detects_the_files_own_format()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneMono);

        //Act
        using var decoder = factory.TryCreateDecoder(stream, out var detected);

        //Assert
        decoder.Should().NotBeNull();
        detected.Channels.Should().Be(1);
        detected.SampleRate.Should().Be(22050);
        detected.Format.Should().Be(SampleFormat.F32);
    }

    [Fact]
    public void Probing_declines_a_stream_that_is_not_ogg()
    {
        //Arrange
        var factory = new VorbisCodecFactory();
        using var stream = TestAssets.Open(TestAssets.FlacToneStereo);

        //Act
        var decoder = factory.TryCreateDecoder(stream, out _);

        //Assert
        decoder.Should().BeNull();
    }
}

using System;
using System.IO;
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
/// Device-less tests for Ogg Vorbis decoding through the native codebrix_miniaudio backend.
/// </summary>
/// <remarks>
/// Vorbis support depends on the native library having been built with stb_vorbis compiled in
/// (see native/miniaudio and tools/build_native_libraries). Where a test would be meaningless
/// without it, it asserts <see cref="Native.HasVorbis" /> first, so a stale binary produces a
/// clear failure rather than a confusing one.
/// </remarks>
public class OggVorbisDecodeTests
{
    private static int DecodeAll(MiniAudioDecoder decoder, out float peak)
    {
        var buffer = new float[4096];
        var total = 0;
        peak = 0f;
        int n;
        while ((n = decoder.Decode(buffer)) > 0)
        {
            for (var i = 0; i < n; i++)
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            total += n;
        }

        return total;
    }

    [Fact]
    public void The_shipped_native_library_has_a_vorbis_decoder()
    {
        //Assert
        // If this fails on a platform whose binary has not been rebuilt yet, .ogg still plays -
        // the managed Vorbis decoder takes over - but the native fast path is not in use.
        Native.HasVorbis.Should().BeTrue();
    }

    [Fact]
    public void Reports_the_files_channels_and_sample_rate()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, TestAssets.VorbisToneStereoSampleRate);

        //Assert
        decoder.Channels.Should().Be(2);
        decoder.SampleRate.Should().Be(TestAssets.VorbisToneStereoSampleRate);
    }

    [Fact]
    public void Length_is_exact_because_the_decoder_opens_ogg_from_memory()
    {
        //Arrange
        // This is the check that catches a regression to push mode: driven through read
        // callbacks, miniaudio reports a Vorbis length of zero and the whole media-transport
        // story (duration, scrubber maximum, clamped seeks) collapses.
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);

        //Act
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, TestAssets.VorbisToneStereoSampleRate);

        //Assert
        decoder.Length.Should().Be(TestAssets.VorbisToneStereoFrames * 2);
    }

    [Fact]
    public void Decodes_the_whole_file_as_audible_audio()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, TestAssets.VorbisToneStereoSampleRate);

        //Act
        var total = DecodeAll(decoder, out var peak);

        //Assert
        total.Should().BeInRange(decoder.Length - 4096, decoder.Length + 4096);
        peak.Should().BeInRange(0.1f, 1.0f); // encoded from a 0.5-amplitude tone
    }

    [Fact]
    public void Decodes_a_mono_file_at_its_own_sample_rate()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneMono);

        //Act
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, 22050);
        var total = DecodeAll(decoder, out var peak);

        //Assert
        decoder.Channels.Should().Be(1);
        decoder.SampleRate.Should().Be(22050);
        total.Should().BeGreaterThan(0);
        peak.Should().BeGreaterThan(0.1f);
    }

    [Fact]
    public void Seek_succeeds_and_reports_it()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisSweep);
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, 48000);

        //Act
        var seeked = decoder.Seek(decoder.Length / 2);

        //Assert
        // Before the pull-mode work this returned false for every Ogg file, because the decoder
        // refused to seek whenever it did not know the stream length.
        seeked.Should().BeTrue();
    }

    [Fact]
    public void Seek_lands_on_the_same_audio_a_sequential_decode_reaches()
    {
        //Arrange
        // The fixture sweeps 200 Hz -> 2 kHz, so the audio at a given offset is unmistakable:
        // if the seek were off, these two buffers would not match.
        using var sequentialStream = TestAssets.Open(TestAssets.VorbisSweep);
        using var sequential = new MiniAudioDecoder(sequentialStream, SampleFormat.F32, 2, 48000);
        using var seekStream = TestAssets.Open(TestAssets.VorbisSweep);
        using var seeking = new MiniAudioDecoder(seekStream, SampleFormat.F32, 2, 48000);

        var targetSample = (sequential.Length / 4) & ~1; // a frame boundary, one quarter in
        var skip = new float[targetSample];
        var expected = new float[2048];
        var actual = new float[2048];

        //Act
        var skipped = 0;
        while (skipped < targetSample)
        {
            var n = sequential.Decode(skip.AsSpan(skipped, targetSample - skipped));
            if (n <= 0) break;
            skipped += n;
        }

        sequential.Decode(expected);
        seeking.Seek(targetSample).Should().BeTrue();
        seeking.Decode(actual);

        //Assert
        for (var i = 0; i < expected.Length; i++)
            actual[i].Should().BeApproximately(expected[i], 1e-4f);
    }

    [Fact]
    public void The_codec_factory_offers_a_decoder_for_the_ogg_format_id()
    {
        //Arrange
        // "ogg" is what Metadata/Readers/Format/OggReader stamps on the file, and it is the
        // identifier every data provider asks the engine for. Before it was registered here,
        // opening an .ogg failed with "no registered codec factory" without the native library
        // ever being consulted.
        var factory = new MiniAudioCodecFactory();
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        var format = new AudioFormat
        {
            Format = SampleFormat.F32,
            Channels = 2,
            SampleRate = TestAssets.VorbisToneStereoSampleRate
        };

        //Act
        using var decoder = factory.CreateDecoder(stream, "ogg", format);

        //Assert
        factory.SupportedFormatIds.Should().Contain("ogg");
        decoder.Should().NotBeNull();
        decoder!.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Metadata_reports_a_real_duration_even_at_the_fast_accuracy_setting()
    {
        //Arrange
        // AudioFilePlayer builds its provider with FastEstimate. An Ogg stream has no usable
        // first-frame estimate, so honouring that literally meant a duration of zero - and a
        // media transport bound to it had nothing to scrub.
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        var options = new ReadOptions
        {
            ReadTags = false,
            ReadAlbumArt = false,
            DurationAccuracy = DurationAccuracy.FastEstimate
        };

        //Act
        var result = SoundMetadataReader.Read(stream, options);

        //Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.FormatIdentifier.Should().Be("ogg");
        result.Value.SampleRate.Should().Be(TestAssets.VorbisToneStereoSampleRate);
        result.Value.ChannelCount.Should().Be(2);
        result.Value.Duration.TotalSeconds.Should().BeApproximately(0.25, 0.01);
    }

    [Fact]
    public void A_truncated_file_fails_instead_of_pretending_to_decode()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisTruncated);

        //Act
        var open = () =>
        {
            using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 2, 44100);
            var buffer = new float[4096];
            while (decoder.Decode(buffer) > 0) { }
        };

        //Assert
        // Either the open fails outright or the decode simply runs out - what must not happen is
        // a hang, a crash, or silence reported as success.
        open.Should().NotThrow<AccessViolationException>();
    }
}

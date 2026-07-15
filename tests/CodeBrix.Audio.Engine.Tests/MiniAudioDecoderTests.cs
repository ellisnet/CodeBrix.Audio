using System;
using System.IO;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Device-less tests for the native miniaudio decode path (<see cref="MiniAudioDecoder"/>).
/// These exercise the renamed codebrix_miniaudio native library end-to-end without opening
/// any audio device, so they are safe to run headless / in CI.
/// </summary>
public class MiniAudioDecoderTests
{
    private const int SampleRate = 44100;
    private const int Frames = 44100; // 1 second, mono => frames == samples

    private static MemoryStream MonoWav() =>
        new(TestAudio.BuildSineWavPcm16(SampleRate, channels: 1, frames: Frames));

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
    public void Reports_expected_channels_and_sample_rate()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Assert
        decoder.Channels.Should().Be(1);
        decoder.SampleRate.Should().Be(SampleRate);
    }

    [Fact]
    public void Length_reports_total_frame_count()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Assert
        decoder.Length.Should().BeInRange(Frames - 16, Frames + 16);
    }

    [Fact]
    public void Decodes_all_samples_within_unit_range()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Act
        var total = DecodeAll(decoder, out var peak);

        //Assert
        total.Should().BeInRange(Frames - 16, Frames + 16);
        peak.Should().BeInRange(0.1f, 1.0f); // sine synthesized at 0.5 amplitude
    }

    [Fact]
    public void Seek_to_start_allows_full_redecode()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Act
        DecodeAll(decoder, out _);
        var seeked = decoder.Seek(0);
        var second = DecodeAll(decoder, out _);

        //Assert
        seeked.Should().BeTrue();
        second.Should().BeInRange(Frames - 16, Frames + 16);
    }

    [Fact]
    public void Seek_past_midpoint_leaves_the_remaining_tail()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Act
        var seeked = decoder.Seek(Frames / 2);
        var remaining = DecodeAll(decoder, out _);

        //Assert
        seeked.Should().BeTrue();
        remaining.Should().BeInRange(Frames / 2 - 64, Frames / 2 + 64);
    }

    [Fact]
    public void EndOfStreamReached_fires_after_full_decode()
    {
        //Arrange
        using var stream = MonoWav();
        using var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);
        var raised = false;
        decoder.EndOfStreamReached += (_, _) => raised = true;

        //Act
        DecodeAll(decoder, out _);

        //Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void Dispose_marks_the_decoder_disposed()
    {
        //Arrange
        var stream = MonoWav();
        var decoder = new MiniAudioDecoder(stream, SampleFormat.F32, 1, SampleRate);

        //Act
        decoder.Dispose();

        //Assert
        decoder.IsDisposed.Should().BeTrue();
        stream.Dispose();
    }
}

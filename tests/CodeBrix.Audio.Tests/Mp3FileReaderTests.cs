using System;
using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="Mp3FileReader"/>, exercising the fully managed
/// (NLayer-derived) MP3 decode path end to end.
/// </summary>
public class Mp3FileReaderTests
{
    [Fact]
    public void Reader_decodes_mp3_to_ieee_float_pcm()
    {
        //Arrange
        using var stream = new MemoryStream(TestAudio.BuildSilentMp3());

        //Act
        using var reader = new Mp3FileReader(stream);

        //Assert
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        reader.WaveFormat.SampleRate.Should().Be(44100);
        reader.WaveFormat.Channels.Should().Be(1);
    }

    [Fact]
    public void Reader_produces_silent_samples_for_a_silent_stream()
    {
        //Arrange
        using var stream = new MemoryStream(TestAudio.BuildSilentMp3());
        using var reader = new Mp3FileReader(stream);
        var provider = reader.ToSampleProvider();

        //Act
        var buffer = new float[4096];
        int totalRead = 0;
        float peak = 0f;
        int read;
        while ((read = provider.Read(buffer)) > 0)
        {
            for (int n = 0; n < read; n++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[n]));
            }
            totalRead += read;
        }

        //Assert
        totalRead.Should().BeGreaterThan(0);
        peak.Should().BeLessThan(0.0001f);
    }
}

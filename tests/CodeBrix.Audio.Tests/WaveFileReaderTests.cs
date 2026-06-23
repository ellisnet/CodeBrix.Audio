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
/// Tests for <see cref="WaveFileReader"/>.
/// </summary>
public class WaveFileReaderTests
{
    [Fact]
    public void Reader_exposes_the_source_wave_format()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        TestAudio.WriteSineWaveFile(path, seconds: 0.1);

        //Act
        using var reader = new WaveFileReader(path);

        //Assert
        reader.WaveFormat.SampleRate.Should().Be(TestAudio.SampleRate);
        reader.WaveFormat.Channels.Should().Be(1);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
        reader.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Reader_is_repositionable()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        TestAudio.WriteSineWaveFile(path, seconds: 0.2);

        //Act
        using var reader = new WaveFileReader(path);
        long length = reader.Length;
        reader.Position = length / 2;

        //Assert
        reader.Position.Should().Be(length / 2);
        reader.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Reader_round_trips_the_written_samples_within_quantisation_error()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        float[] written = TestAudio.WriteSineWaveFile(path, frequency: 1000, seconds: 0.05);

        //Act
        using var reader = new WaveFileReader(path);
        var sampleProvider = reader.ToSampleProvider();
        var read = new float[written.Length];
        int got = sampleProvider.Read(read);

        //Assert
        got.Should().Be(written.Length);
        for (int n = 0; n < written.Length; n++)
        {
            System.Math.Abs(read[n] - written[n]).Should().BeLessThan(0.001f);
        }
        reader.Dispose();
        File.Delete(path);
    }
}

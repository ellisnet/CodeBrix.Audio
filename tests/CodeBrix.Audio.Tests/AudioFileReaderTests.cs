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
/// Tests for <see cref="AudioFileReader"/>.
/// </summary>
public class AudioFileReaderTests
{
    [Fact]
    public void Reads_a_wav_file_as_ieee_float_samples()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        float[] written = TestAudio.WriteSineWaveFile(path, frequency: 1000, seconds: 0.05);

        //Act
        using var reader = new AudioFileReader(path);
        var buffer = new float[written.Length];
        int read = reader.Read(buffer);

        //Assert
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        read.Should().Be(written.Length);
        for (int n = 0; n < written.Length; n++)
        {
            Math.Abs(buffer[n] - written[n]).Should().BeLessThan(0.001f);
        }
        reader.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Volume_scales_the_returned_samples()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        float[] written = TestAudio.WriteSineWaveFile(path, frequency: 1000, seconds: 0.05);

        //Act
        using var reader = new AudioFileReader(path) { Volume = 0.5f };
        var buffer = new float[written.Length];
        reader.Read(buffer);

        //Assert
        for (int n = 0; n < written.Length; n++)
        {
            Math.Abs(buffer[n] - written[n] * 0.5f).Should().BeLessThan(0.001f);
        }
        reader.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Unsupported_extension_throws()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".flac");
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });

        //Act
        Action act = () => new AudioFileReader(path);

        //Assert
        act.Should().Throw<InvalidOperationException>();
        File.Delete(path);
    }
}

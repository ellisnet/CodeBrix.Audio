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
/// Tests for <see cref="WaveFileWriter"/>.
/// </summary>
public class WaveFileWriterTests
{
    [Fact]
    public void WriteSamples_produces_a_readable_riff_wav_file()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");

        //Act
        TestAudio.WriteSineWaveFile(path, seconds: 0.1);

        //Assert
        File.Exists(path).Should().BeTrue();
        using (var fs = File.OpenRead(path))
        {
            var header = new byte[4];
            fs.ReadExactly(header);
            System.Text.Encoding.ASCII.GetString(header).Should().Be("RIFF");
        }
        File.Delete(path);
    }

    [Fact]
    public void WriteSamples_records_the_expected_number_of_sample_frames()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        float[] written = TestAudio.WriteSineWaveFile(path, seconds: 0.2);

        //Act
        using var reader = new WaveFileReader(path);
        long frames = reader.SampleCount;

        //Assert
        frames.Should().Be(written.Length);
        reader.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Writer_reports_the_format_it_was_constructed_with()
    {
        //Arrange
        using var ms = new MemoryStream();
        var format = new WaveFormat(22050, 16, 1);

        //Act
        using var writer = new WaveFileWriter(ms, format);

        //Assert
        writer.WaveFormat.SampleRate.Should().Be(22050);
        writer.WaveFormat.Channels.Should().Be(1);
    }
}

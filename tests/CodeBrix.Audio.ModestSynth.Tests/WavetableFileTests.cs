using System;
using System.IO;
using CodeBrix.Audio.ModestSynth.Wavetable;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="WavetableFile" />: what it makes of a .wav file, how the Serum
/// <c>clm&#160;</c> chunk overrides a requested frame size, and what it reports instead of
/// throwing when a file is not there.
/// </summary>
public class WavetableFileTests : IDisposable
{
    private readonly string folder;

    public WavetableFileTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "modestsynth-wt-" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public void TryLoad_cuts_a_plain_file_into_frames_of_the_requested_size()
    {
        //Arrange
        string path = WriteTable(null, 32, 2);

        //Act
        bool loaded = WavetableFile.TryLoad(path, WavetableFixtures.FrameSize, out WavetableFile file,
            out string problem);

        //Assert
        loaded.Should().BeTrue();
        problem.Should().BeNull();
        file.FrameSize.Should().Be(WavetableFixtures.FrameSize);
        file.FrameCount.Should().Be(WavetableFixtures.FrameCount);
        file.SampleCount.Should().Be(WavetableFixtures.FrameCount * WavetableFixtures.FrameSize);
        file.HasClmChunk.Should().BeFalse();
        file.DeclaredFrameSize.Should().Be(0);
    }

    [Fact]
    public void TryLoad_falls_back_to_the_formats_own_frame_size_when_none_is_asked_for()
    {
        //Arrange
        string path = WriteTable(null, 32, 1);

        //Act
        WavetableFile.TryLoad(path, 0, out WavetableFile file, out _);

        //Assert
        file.FrameSize.Should().Be(WavetableFile.DefaultFrameSize);
    }

    [Fact]
    public void TryLoad_reads_the_frame_size_out_of_a_clm_chunk()
    {
        //Arrange
        string path = WriteTable(WavetableFixtures.ClmText(1024), 32, 1);

        //Act
        WavetableFile.TryLoad(path, 0, out WavetableFile file, out _);

        //Assert
        file.HasClmChunk.Should().BeTrue();
        file.DeclaredFrameSize.Should().Be(1024);
        file.FrameSize.Should().Be(1024);
        file.FrameCount.Should().Be(8);
    }

    [Fact]
    public void TryLoad_lets_a_clm_chunk_override_an_explicit_frame_size()
    {
        //Arrange
        string path = WriteTable(WavetableFixtures.ClmText(512), 32, 1);

        //Act
        WavetableFile.TryLoad(path, 2048, out WavetableFile file, out _);

        //Assert
        file.FrameSize.Should().Be(512);
        file.FrameCount.Should().Be(16);
    }

    [Fact]
    public void TryLoad_ignores_a_clm_chunk_whose_text_names_nothing_usable()
    {
        //Arrange
        string path = WriteTable("not a serum chunk at all", 32, 1);

        //Act
        WavetableFile.TryLoad(path, 1024, out WavetableFile file, out _);

        //Assert
        file.HasClmChunk.Should().BeFalse();
        file.FrameSize.Should().Be(1024);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void TryLoad_reads_every_bit_depth(int bitsPerSample)
    {
        //Arrange
        string path = WriteTable(null, bitsPerSample, 1);

        //Act
        bool loaded = WavetableFile.TryLoad(path, WavetableFixtures.FrameSize, out WavetableFile file,
            out string problem);

        //Assert
        loaded.Should().BeTrue();
        file.FrameCount.Should().Be(WavetableFixtures.FrameCount);
    }

    [Fact]
    public void TryLoad_reads_a_stereo_file_as_one_channel()
    {
        //Arrange
        string path = WriteTable(null, 32, 2);

        //Act
        WavetableFile.TryLoad(path, WavetableFixtures.FrameSize, out WavetableFile file, out _);

        //Assert
        file.SourceChannels.Should().Be(2);
        file.FrameCount.Should().Be(WavetableFixtures.FrameCount);
        file.GetFrame(WavetableFixtures.SquareFrame)[0].Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void TryLoad_keeps_a_stereo_files_left_channel_and_drops_the_right()
    {
        //Arrange
        // MEASURED (round 3, item 44): the reference plays a stereo wavetable as its LEFT channel on
        // both outputs and discards the right one silently. An average would give silence here.
        float[] left = WavetableFixtures.BuildFourFrames();
        float[] right = new float[left.Length];
        for (int i = 0; i < left.Length; i++) { right[i] = -left[i]; }

        string path = Path.Combine(folder, "stereo-" + Path.GetRandomFileName() + ".wav");
        WavetableFixtures.WriteStereoWav(path, left, right, 44100);

        //Act
        bool loaded = WavetableFile.TryLoad(path, WavetableFixtures.FrameSize, out WavetableFile file, out _);

        //Assert
        loaded.Should().BeTrue();
        file.SourceChannels.Should().Be(2);

        ReadOnlySpan<float> frame = file.GetFrame(WavetableFixtures.SquareFrame);
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i].Should().BeApproximately(left[(WavetableFixtures.SquareFrame * WavetableFixtures.FrameSize) + i], 1e-6f);
        }
    }

    [Fact]
    public void TryLoad_keeps_the_files_own_samples()
    {
        //Arrange
        float[] expected = WavetableFixtures.BuildFourFrames();
        string path = WriteTable(null, 32, 1);

        //Act
        WavetableFile.TryLoad(path, WavetableFixtures.FrameSize, out WavetableFile file, out _);
        ReadOnlySpan<float> frame = file.GetFrame(WavetableFixtures.SawFrame);

        //Assert
        for (int i = 0; i < WavetableFixtures.FrameSize; i += 97)
        {
            frame[i].Should().BeApproximately(
                expected[(WavetableFixtures.SawFrame * WavetableFixtures.FrameSize) + i], 1e-6f);
        }
    }

    [Fact]
    public void TryLoad_drops_the_samples_left_over_after_the_last_whole_frame()
    {
        //Arrange
        float[] samples = new float[(3 * 256) + 17];
        for (int i = 0; i < samples.Length; i++) { samples[i] = (float)Math.Sin(i * 0.01); }
        string path = Path.Combine(folder, "ragged.wav");
        WavetableFixtures.WriteWav(path, samples, 44100, 32, 1, null);

        //Act
        WavetableFile.TryLoad(path, 256, out WavetableFile file, out _);

        //Assert
        file.FrameCount.Should().Be(3);
        file.SampleCount.Should().Be(768);
    }

    [Fact]
    public void TryLoad_reports_a_missing_file_and_does_not_throw()
    {
        //Arrange
        string path = Path.Combine(folder, "no-such-table.wav");

        //Act
        bool loaded = WavetableFile.TryLoad(path, 2048, out WavetableFile file, out string problem);

        //Assert
        loaded.Should().BeFalse();
        file.Should().BeNull();
        problem.Should().Contain("Missing file reference: <oscillator> @wavetableFile=");
        problem.Should().Contain("no-such-table.wav");
    }

    [Fact]
    public void TryLoad_reports_a_blank_path()
    {
        //Act
        bool loaded = WavetableFile.TryLoad("   ", 2048, out _, out string problem);

        //Assert
        loaded.Should().BeFalse();
        problem.Should().Contain("No file reference: <oscillator> @wavetableFile is not set");
    }

    [Fact]
    public void TryLoad_reports_a_file_that_is_not_a_wav_at_all()
    {
        //Arrange
        string path = Path.Combine(folder, "rubbish.wav");
        File.WriteAllText(path, "this is not a RIFF file");

        //Act
        bool loaded = WavetableFile.TryLoad(path, 2048, out _, out string problem);

        //Assert
        loaded.Should().BeFalse();
        problem.Should().Contain("Unreadable file reference: <oscillator> @wavetableFile=");
    }

    [Fact]
    public void TryLoad_reports_a_file_holding_less_than_one_frame()
    {
        //Arrange
        float[] samples = new float[100];
        string path = Path.Combine(folder, "tiny.wav");
        WavetableFixtures.WriteWav(path, samples, 44100, 32, 1, null);

        //Act
        bool loaded = WavetableFile.TryLoad(path, 2048, out _, out string problem);

        //Assert
        loaded.Should().BeFalse();
        problem.Should().Contain("Unusable wavetable: <oscillator> @wavetableFile=");
        problem.Should().Contain("fewer than one frame of 2048");
    }

    [Fact]
    public void FromSamples_builds_a_table_without_a_file()
    {
        //Act
        WavetableFile file = WavetableFile.FromSamples(WavetableFixtures.BuildFourFrames(),
            WavetableFixtures.FrameSize, "in code");

        //Assert
        file.SourcePath.Should().Be("in code");
        file.FrameCount.Should().Be(4);
        file.HasClmChunk.Should().BeFalse();
        file.SourceSampleRate.Should().Be(0);
    }

    [Fact]
    public void FromSamples_rejects_less_than_one_whole_frame()
    {
        //Act
        Action act = () => WavetableFile.FromSamples(new float[10], 2048, "short");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetFrame_rejects_a_frame_the_table_does_not_have()
    {
        //Arrange
        WavetableFile file = WavetableFile.FromSamples(WavetableFixtures.BuildFourFrames(),
            WavetableFixtures.FrameSize, "in code");

        //Act
        Action act = () => file.GetFrame(4);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ApproximateSizeInBytes_is_about_four_and_a_half_times_the_frame_data()
    {
        //Arrange
        WavetableFile file = WavetableFile.FromSamples(WavetableFixtures.BuildFourFrames(),
            WavetableFixtures.FrameSize, "in code");

        //Act
        double ratio = file.ApproximateSizeInBytes / (double)(file.SampleCount * sizeof(float));

        //Assert
        ratio.Should().BeInRange(4.0, 5.0);
    }

    [Fact]
    public void A_frame_size_that_is_not_a_power_of_two_still_plays()
    {
        //Arrange
        float[] samples = new float[3 * 1500];
        for (int frame = 0; frame < 3; frame++)
        {
            for (int i = 0; i < 1500; i++)
            {
                samples[(frame * 1500) + i] = (float)Math.Sin(2.0 * Math.PI * (frame + 1) * i / 1500.0);
            }
        }

        //Act
        WavetableFile file = WavetableFile.FromSamples(samples, 1500, "odd");

        //Assert
        file.FrameSize.Should().Be(1500);
        file.FrameCount.Should().Be(3);
        file.ApproximateSizeInBytes.Should().BeGreaterThan(file.SampleCount * (long)sizeof(float));
    }

    private string WriteTable(string clmText, int bitsPerSample, int channels)
    {
        string path = Path.Combine(folder, "table-" + Path.GetRandomFileName() + ".wav");
        WavetableFixtures.WriteWav(path, WavetableFixtures.BuildFourFrames(), 44100, bitsPerSample,
            channels, clmText);
        return path;
    }
}

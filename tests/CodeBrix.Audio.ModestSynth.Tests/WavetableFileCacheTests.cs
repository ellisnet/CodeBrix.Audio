using System;
using System.IO;
using CodeBrix.Audio.ModestSynth.Wavetable;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="WavetableFileCache" />: one decode per file, shared by every voice, and a
/// failure that is not remembered.
/// </summary>
[Collection(WavetableCacheCollection.Name)]
public class WavetableFileCacheTests : IDisposable
{
    private readonly string folder;

    public WavetableFileCacheTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "modestsynth-wtc-" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        WavetableFileCache.Clear();
    }

    public void Dispose()
    {
        WavetableFileCache.Clear();

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
    public void GetOrLoad_returns_the_same_table_the_second_time()
    {
        //Arrange
        string path = WriteTable();

        //Act
        WavetableFile first = WavetableFileCache.GetOrLoad(path, WavetableFixtures.FrameSize);
        WavetableFile second = WavetableFileCache.GetOrLoad(path, WavetableFixtures.FrameSize);

        //Assert
        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        WavetableFileCache.Count.Should().Be(1);
    }

    [Fact]
    public void GetOrLoad_keeps_one_entry_per_requested_frame_size()
    {
        //Arrange
        string path = WriteTable();

        //Act
        WavetableFile big = WavetableFileCache.GetOrLoad(path, 2048);
        WavetableFile small = WavetableFileCache.GetOrLoad(path, 1024);

        //Assert
        big.FrameSize.Should().Be(2048);
        small.FrameSize.Should().Be(1024);
        WavetableFileCache.Count.Should().Be(2);
    }

    [Fact]
    public void GetOrLoad_returns_null_for_a_file_that_is_not_there()
    {
        //Act
        WavetableFile file = WavetableFileCache.GetOrLoad(Path.Combine(folder, "absent.wav"), 2048);

        //Assert
        file.Should().BeNull();
        WavetableFileCache.Count.Should().Be(0);
    }

    [Fact]
    public void TryGetOrLoad_does_not_remember_a_failure()
    {
        //Arrange
        string path = Path.Combine(folder, "late.wav");

        //Act
        bool firstTry = WavetableFileCache.TryGetOrLoad(path, WavetableFixtures.FrameSize, out _,
            out string problem);
        WavetableFixtures.WriteWav(path, WavetableFixtures.BuildFourFrames(), 44100, 32, 1, null);
        bool secondTry = WavetableFileCache.TryGetOrLoad(path, WavetableFixtures.FrameSize,
            out WavetableFile file, out _);

        //Assert
        firstTry.Should().BeFalse();
        problem.Should().Contain("Missing file reference");
        secondTry.Should().BeTrue();
        file.Should().NotBeNull();
    }

    [Fact]
    public void TryGetOrLoad_reports_a_blank_path_without_caching_anything()
    {
        //Act
        bool loaded = WavetableFileCache.TryGetOrLoad(null, 2048, out WavetableFile file,
            out string problem);

        //Assert
        loaded.Should().BeFalse();
        file.Should().BeNull();
        problem.Should().Contain("No file reference");
        WavetableFileCache.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        //Arrange
        WavetableFileCache.GetOrLoad(WriteTable(), WavetableFixtures.FrameSize);

        //Act
        WavetableFileCache.Clear();

        //Assert
        WavetableFileCache.Count.Should().Be(0);
        WavetableFileCache.ApproximateSizeInBytes.Should().Be(0L);
    }

    [Fact]
    public void ApproximateSizeInBytes_counts_what_is_held()
    {
        //Arrange
        string path = WriteTable();

        //Act
        WavetableFile file = WavetableFileCache.GetOrLoad(path, WavetableFixtures.FrameSize);

        //Assert
        WavetableFileCache.ApproximateSizeInBytes.Should().Be(file.ApproximateSizeInBytes);
    }

    private string WriteTable()
    {
        string path = Path.Combine(folder, "cache-" + Path.GetRandomFileName() + ".wav");
        WavetableFixtures.WriteWav(path, WavetableFixtures.BuildFourFrames(), 44100, 32, 1, null);
        return path;
    }
}

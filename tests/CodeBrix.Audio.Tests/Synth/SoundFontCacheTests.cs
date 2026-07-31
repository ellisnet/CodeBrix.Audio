using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="SoundFontCache"/>, whose job is to make sure a multi-megabyte SoundFont is
/// loaded once per process rather than once per track.
/// </summary>
public class SoundFontCacheTests
{
    [Fact]
    public void get_returns_the_same_instance_for_the_same_path()
    {
        //Arrange
        using var cache = new SoundFontCache();
        var path = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);

        //Act
        var first = cache.Get(path);
        var second = cache.Get(path);

        //Assert
        second.Should().BeSameAs(first);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void get_normalizes_the_path_before_caching()
    {
        //Arrange
        using var cache = new SoundFontCache();
        var path = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);
        var roundabout = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path), ".", System.IO.Path.GetFileName(path));

        //Act
        var first = cache.Get(path);
        var second = cache.Get(roundabout);

        //Assert
        second.Should().BeSameAs(first);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void get_rejects_a_null_path()
    {
        //Arrange
        using var cache = new SoundFontCache();

        //Act
        var act = () => cache.Get(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void get_or_add_shares_a_soundfont_that_did_not_come_from_a_file()
    {
        //Arrange
        using var cache = new SoundFontCache();
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);

        //Act
        var added = cache.GetOrAdd("embedded:test", soundFont);
        var fetched = cache.GetOrAdd("embedded:test", SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName));

        //Assert
        added.Should().BeSameAs(soundFont);
        fetched.Should().BeSameAs(soundFont);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void contains_reports_what_is_held()
    {
        //Arrange
        using var cache = new SoundFontCache();
        var path = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);

        //Act
        var beforeLoad = cache.Contains(path);
        cache.Get(path);
        var afterLoad = cache.Contains(path);

        //Assert
        beforeLoad.Should().BeFalse();
        afterLoad.Should().BeTrue();
    }

    [Fact]
    public void clear_drops_everything_but_leaves_the_cache_usable()
    {
        //Arrange
        using var cache = new SoundFontCache();
        var path = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);
        cache.Get(path);

        //Act
        cache.Clear();
        var afterClear = cache.Count;
        cache.Get(path);

        //Assert
        afterClear.Should().Be(0);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void an_instance_handed_out_before_disposal_keeps_working()
    {
        //Arrange
        var cache = new SoundFontCache();
        var soundFont = cache.Get(SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName));

        //Act
        cache.Dispose();

        //Assert
        // Disposing the cache drops references so the SoundFonts can be collected; it does not
        // invalidate one a player is still holding.
        soundFont.Instruments.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void get_throws_once_the_cache_is_disposed()
    {
        //Arrange
        var cache = new SoundFontCache();
        var path = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);
        cache.Dispose();

        //Act
        var act = () => cache.Get(path);

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}

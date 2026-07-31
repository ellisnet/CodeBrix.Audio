using System;
using System.IO;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers <see cref="SfzInstrumentCache"/>: one load per path, caller-keyed sharing, and disposal
/// semantics - the same contract as <c>SoundFontCache</c>.
/// </summary>
public class SfzInstrumentCacheTests
{
    [Fact]
    public void the_same_path_returns_the_same_instance()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);
        File.WriteAllText(Path.Combine(fixture.Directory, "kit.sfz"), "<region> sample=a.wav");
        using var cache = new SfzInstrumentCache();

        //Act
        var first = cache.Get(Path.Combine(fixture.Directory, "kit.sfz"));
        var second = cache.Get(Path.Combine(fixture.Directory, "kit.sfz"));

        //Assert
        second.Should().BeSameAs(first);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void get_or_add_shares_an_existing_instrument_under_a_key()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);
        var instrument = fixture.Load("<region> sample=a.wav");
        using var cache = new SfzInstrumentCache();

        //Act
        var added = cache.GetOrAdd("built-in", instrument);
        var again = cache.GetOrAdd("built-in", fixture.Load("<region> sample=a.wav"));

        //Assert
        added.Should().BeSameAs(instrument);
        again.Should().BeSameAs(instrument, "the first instrument under a key wins");
        cache.Contains("built-in").Should().BeTrue();
    }

    [Fact]
    public void a_disposed_cache_refuses_further_loads()
    {
        //Arrange
        var cache = new SfzInstrumentCache();
        cache.Dispose();

        //Act
        var act = () => cache.Get("anything.sfz");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}

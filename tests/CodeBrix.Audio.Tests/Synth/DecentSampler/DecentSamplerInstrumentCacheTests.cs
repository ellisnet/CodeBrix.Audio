using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers the instrument cache: one load per path, keys that are compared as paths, and disposal.
/// </summary>
public class DecentSamplerInstrumentCacheTests
{
    [Fact]
    public void the_same_path_is_loaded_once()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        var path = fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset());
        using var cache = new DecentSamplerInstrumentCache();

        //Act
        var first = cache.Get(path);
        var second = cache.Get(path);

        //Assert
        first.Should().BeSameAs(second);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void a_relative_and_an_absolute_path_are_the_same_instrument()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        var path = fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset());
        using var cache = new DecentSamplerInstrumentCache();

        //Act
        var first = cache.Get(path);

        //Assert
        cache.Contains(path).Should().BeTrue();
        cache.Get(System.IO.Path.GetFullPath(path)).Should().BeSameAs(first);
    }

    [Fact]
    public void an_instrument_can_be_shared_under_a_chosen_key()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset());
        using var cache = new DecentSamplerInstrumentCache();

        //Act
        var added = cache.GetOrAdd("chosen", instrument);
        var again = cache.GetOrAdd("chosen", instrument);

        //Assert
        added.Should().BeSameAs(instrument);
        again.Should().BeSameAs(instrument);
        cache.Contains("chosen").Should().BeTrue();
    }

    [Fact]
    public void a_disposed_cache_refuses_further_use()
    {
        //Arrange
        var cache = new DecentSamplerInstrumentCache();
        cache.Dispose();

        //Act
        var act = () => cache.Get("anything.dspreset");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void clearing_empties_the_cache()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        var path = fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset());
        using var cache = new DecentSamplerInstrumentCache();
        cache.Get(path);

        //Act
        cache.Clear();

        //Assert
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void a_null_path_is_rejected()
    {
        //Arrange
        using var cache = new DecentSamplerInstrumentCache();

        //Act
        var act = () => cache.Get(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}

using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// One decode per file, however many zones and however many presets want it - and impulse responses
/// that stay in memory whatever the memory policy decides.
/// </summary>
public class DecentSamplerSampleSharingTests
{
    private const string PresetA = """
        <DecentSampler>
          <groups>
            <group>
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="63" />
              <sample path="Samples/tone.wav" rootNote="72" loNote="64" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    private const string PresetB = """
        <DecentSampler>
          <groups>
            <group volume="0.5">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void two_zones_over_one_file_decode_it_once()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 10000);

        //Act
        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(PresetA));

        //Assert
        instrument.Zones.Count.Should().Be(2);
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void two_instruments_from_one_cache_share_the_decode()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 10000);

        var first = fixtures.WritePreset(PresetA, "a.dspreset");
        var second = fixtures.WritePreset(PresetB, "b.dspreset");

        using var cache = new DecentSamplerInstrumentCache();

        //Act
        var a = cache.Get(first);
        var b = cache.Get(second);

        //Assert
        cache.SharesSampleData.Should().BeTrue();
        cache.SharedSampleCount.Should().Be(1);
        cache.SharedSampleByteCount.Should().Be(10000 * sizeof(float));
        a.DecodedByteCount.Should().Be(b.DecodedByteCount);
    }

    [Fact]
    public void a_cache_that_does_not_share_decodes_the_file_for_each_instrument()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 10000);

        var first = fixtures.WritePreset(PresetA, "a.dspreset");
        var second = fixtures.WritePreset(PresetB, "b.dspreset");

        using var cache = new DecentSamplerInstrumentCache(shareSampleData: false);

        //Act
        cache.Get(first);
        cache.Get(second);

        //Assert
        cache.SharesSampleData.Should().BeFalse();
        cache.SharedSampleCount.Should().Be(0);
    }

    [Fact]
    public void two_instruments_from_one_cache_share_a_streamed_preload_head()
    {
        //Arrange
        const string streamed = """
            <DecentSampler>
              <groups>
                <group playbackMode="disk_streaming">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 100000);

        var first = fixtures.WritePreset(streamed, "a.dspreset");
        var second = fixtures.WritePreset(streamed.Replace("0.5", "0.4", StringComparison.Ordinal), "b.dspreset");

        using var cache = new DecentSamplerInstrumentCache();
        var options = new DecentSamplerLoadOptions { StreamingPreloadFrames = 1000 };

        //Act
        var a = cache.Get(first, options);
        var b = cache.Get(second, options);

        //Assert - one head, not two.
        a.StreamedSampleCount.Should().Be(1);
        b.StreamedSampleCount.Should().Be(1);
        cache.SharedSampleCount.Should().Be(1);
        cache.SharedSampleByteCount.Should().Be(1000 * sizeof(float));
    }

    [Fact]
    public void an_impulse_response_stays_in_memory_however_big_it_is()
    {
        //Arrange - the convolution effect reads its impulse response through the same shared cache, and
        //a streamed impulse response would be useless: the whole file is needed on every block.
        const string preset = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
              <effects>
                <effect type="convolution" irFile="Samples/ir.wav" mix="1.0" />
              </effects>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 100000);
        fixtures.WriteConstantWav("Samples/ir.wav", value: 0.1f, frames: 100000);

        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(preset),
            new DecentSamplerLoadOptions { StreamingSampleThresholdBytes = 1024 });

        //Act - building the synthesizer is what loads the impulse response.
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 8);

        //Assert - the zone streams; the impulse response is held whole beside it.
        instrument.StreamedSampleCount.Should().Be(1);
        instrument.DecodedByteCount.Should().BeGreaterThan(100000 * sizeof(float));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// Which samples end up in memory and which are streamed: the <c>playbackMode</c> attribute, the
/// per-sample threshold, the per-instrument budget, and the load-time override.
/// </summary>
public class DecentSamplerMemoryPolicyTests
{
    private const string TwoSamplePreset = """
        <DecentSampler>
          <groups>
            <group playbackMode="{MODE}">
              <sample path="Samples/big.wav" rootNote="60" loNote="0" hiNote="63" />
              <sample path="Samples/small.wav" rootNote="72" loNote="64" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void auto_streams_a_sample_over_the_threshold_and_keeps_the_rest()
    {
        //Arrange - big.wav decodes to 400 kB, small.wav to 4 kB; the threshold sits between them.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100000);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 1000);

        //Act
        using var instrument = Load(fixtures, "auto", options =>
            options.StreamingSampleThresholdBytes = 100000);

        //Assert
        instrument.StreamedSampleCount.Should().Be(1);
        instrument.DecodedSampleCount.Should().Be(2);
        instrument.MemoryPolicySummary.Should().Contain("1 of 2 samples in memory");
    }

    [Fact]
    public void auto_keeps_everything_when_nothing_passes_the_threshold()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100000);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 1000);

        //Act
        using var instrument = Load(fixtures, "auto");

        //Assert
        instrument.StreamedSampleCount.Should().Be(0);
        instrument.MemoryPolicySummary.Should().Contain("2 of 2 samples in memory");
    }

    [Fact]
    public void an_explicit_memory_mode_survives_a_threshold_it_would_otherwise_cross()
    {
        //Arrange - the guide's reason: a preset that moves SAMPLE_START must stay in memory.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100000);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 1000);

        //Act
        using var instrument = Load(fixtures, "memory", options =>
        {
            options.StreamingSampleThresholdBytes = 1;
            options.InstrumentMemoryBudgetBytes = 1;
        });

        //Assert
        instrument.StreamedSampleCount.Should().Be(0);
    }

    [Fact]
    public void an_explicit_disk_streaming_mode_streams_however_small_the_sample_is()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 100);

        //Act
        using var instrument = Load(fixtures, "disk_streaming");

        //Assert
        instrument.StreamedSampleCount.Should().Be(2);
    }

    [Fact]
    public void the_load_option_override_beats_what_the_preset_wrote()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 100);

        //Act
        using var instrument = Load(fixtures, "memory", options =>
            options.PlaybackModeOverride = DecentSamplerPlaybackMode.DiskStreaming);

        //Assert
        instrument.StreamedSampleCount.Should().Be(2);
    }

    [Fact]
    public void the_instrument_budget_streams_the_largest_samples_until_it_fits()
    {
        //Arrange - three files of 400 kB, 200 kB and 4 kB against a 250 kB budget.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/big.wav", frames: 100000);
        fixtures.WriteConstantWav("Samples/middle.wav", frames: 50000);
        fixtures.WriteConstantWav("Samples/small.wav", frames: 1000);

        const string preset = """
            <DecentSampler>
              <groups>
                <group playbackMode="auto">
                  <sample path="Samples/big.wav" rootNote="60" loNote="0" hiNote="41" />
                  <sample path="Samples/middle.wav" rootNote="60" loNote="42" hiNote="83" />
                  <sample path="Samples/small.wav" rootNote="60" loNote="84" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(preset),
            new DecentSamplerLoadOptions
            {
                StreamingSampleThresholdBytes = 0,
                InstrumentMemoryBudgetBytes = 250 * 1024,
            });

        //Assert - dropping the 400 kB file alone brings the total under the budget.
        instrument.StreamedSampleCount.Should().Be(1);
        instrument.Problems.Should().Contain(problem => problem.Contains("memory policy:", StringComparison.Ordinal));
    }

    [Fact]
    public void the_budget_breaks_a_tie_the_same_way_every_time()
    {
        //Arrange - two files of exactly the same size, so only the tie-break decides which streams.
        //Act
        var first = TieBreak();
        var second = TieBreak();

        //Assert
        first.Should().Be("a");
        second.Should().Be("a");
    }

    [Fact]
    public void two_zones_over_one_file_disagreeing_about_the_mode_keep_it_in_memory()
    {
        //Arrange - memory satisfies both zones; streaming satisfies only one.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/one.wav", frames: 1000);

        const string preset = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="Samples/one.wav" rootNote="60" loNote="0" hiNote="63"
                          playbackMode="disk_streaming" />
                  <sample path="Samples/one.wav" rootNote="60" loNote="64" hiNote="127"
                          playbackMode="memory" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(preset));

        //Assert
        instrument.StreamedSampleCount.Should().Be(0);
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void the_policy_takes_the_largest_first_and_stops_when_it_fits()
    {
        //Arrange
        //A one-frame head, so streaming a file costs almost nothing and the saving is its whole size.
        var entries = new List<DecentSamplerMemoryPolicy.Entry>
        {
            new("a", "a", DecentSamplerPlaybackMode.Auto, 100, 1),
            new("b", "b", DecentSamplerPlaybackMode.Auto, 400, 1),
            new("c", "c", DecentSamplerPlaybackMode.Auto, 300, 1),
        };

        //Act - 800 bytes in total against a 402 byte budget: dropping the 400 byte file is enough.
        DecentSamplerMemoryPolicy.Decide(entries, 0, 402, 1, out var budgetForced, out _);

        //Assert
        budgetForced.Should().BeTrue();
        entries.Where(entry => entry.Stream).Select(entry => entry.CacheKey).Should().Equal("b");
    }

    [Fact]
    public void the_policy_never_moves_a_memory_sample_to_satisfy_the_budget()
    {
        //Arrange
        var entries = new List<DecentSamplerMemoryPolicy.Entry>
        {
            new("a", "a", DecentSamplerPlaybackMode.Memory, 1000, 1),
            new("b", "b", DecentSamplerPlaybackMode.Auto, 10, 1),
        };

        //Act
        DecentSamplerMemoryPolicy.Decide(entries, 0, 100, 1, out _, out _);

        //Assert - the budget cannot be met, and the explicit choice still stands.
        entries[0].Stream.Should().BeFalse();
        entries[1].Stream.Should().BeTrue();
    }

    private static string TieBreak()
    {
        var entries = new List<DecentSamplerMemoryPolicy.Entry>
        {
            new("b", "b", DecentSamplerPlaybackMode.Auto, 100, 1),
            new("a", "a", DecentSamplerPlaybackMode.Auto, 100, 1),
        };

        DecentSamplerMemoryPolicy.Decide(entries, 0, 102, 1, out _, out _);

        return entries.Single(entry => entry.Stream).CacheKey;
    }

    private static DecentSamplerInstrument Load(
        DecentSamplerEngineFixtures fixtures, string mode, Action<DecentSamplerLoadOptions> configure = null)
    {
        var options = new DecentSamplerLoadOptions();
        configure?.Invoke(options);

        return DecentSamplerInstrument.Load(
            fixtures.WritePreset(
                TwoSamplePreset.Replace("{MODE}", mode, StringComparison.Ordinal), mode + ".dspreset"),
            options);
    }
}

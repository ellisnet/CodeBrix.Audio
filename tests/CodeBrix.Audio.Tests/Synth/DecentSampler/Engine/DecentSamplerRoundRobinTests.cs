using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Round robins: the four seqMode values, instrument-wide seqLength auto-detection, positions with no
/// zone, and the deterministic seeding that makes a render reproducible.
/// </summary>
/// <remarks>
/// Each position plays a constant sample of its own value - 0.1 for position 1, 0.2 for position 2 and
/// so on - so the level of a note names the position that sounded, and 0 names a silent position.
/// </remarks>
public class DecentSamplerRoundRobinTests
{
    [Fact]
    public void round_robin_advances_one_position_per_note_and_starts_at_two()
    {
        //Arrange
        using var world = Build(3, "round_robin", declareLength: false);

        //Act
        var positions = world.PlayNotes(7);

        //Assert - measured: the register starts at 1 and is advanced BEFORE each selection.
        positions.Should().Equal(2, 3, 1, 2, 3, 1, 2);
    }

    [Fact]
    public void an_explicit_seqLength_shortens_the_queue()
    {
        //Arrange
        using var world = Build(4, "round_robin", declareLength: true, length: 2);

        //Act
        var positions = world.PlayNotes(6);

        //Assert
        positions.Should().Equal(2, 1, 2, 1, 2, 1);
    }

    [Fact]
    public void a_position_with_no_zone_plays_nothing()
    {
        //Arrange - positions 1, 3 and 5 exist; the queue is five long.
        const string preset = """
            <DecentSampler>
              <groups>
                <group seqMode="round_robin" ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="1" />
                  <sample path="Samples/p3.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="3" />
                  <sample path="Samples/p5.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="5" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = BuildFrom(preset);

        //Act
        var positions = world.PlayNotes(6);

        //Assert - the queue is never compacted, so positions 2 and 4 are silent.
        positions.Should().Equal(0, 3, 0, 5, 1, 0);
    }

    [Fact]
    public void seqLength_auto_detection_is_instrument_wide()
    {
        //Arrange - a three-sample round robin beside a group that declares position 5 elsewhere.
        const string preset = """
            <DecentSampler>
              <groups>
                <group seqMode="round_robin" ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="1" />
                  <sample path="Samples/p2.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="2" />
                  <sample path="Samples/p3.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="3" />
                </group>
                <group seqMode="round_robin" ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="72" loNote="72" hiNote="72" seqPosition="4" />
                  <sample path="Samples/p2.wav" rootNote="72" loNote="72" hiNote="72" seqPosition="5" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = BuildFrom(preset);

        //Act
        var positions = world.PlayNotes(5);

        //Assert - measured: the auto-detected length is the highest seqPosition ANYWHERE in the preset,
        //so this three-sample set cycles with period five and two of every five notes are silent.
        positions.Should().Equal(2, 3, 0, 0, 1);
    }

    [Fact]
    public void a_preset_that_mixes_round_robin_lengths_says_so_in_problems()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group seqMode="round_robin" ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="1" />
                  <sample path="Samples/p2.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="2" />
                </group>
                <group seqMode="round_robin" ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="72" loNote="72" hiNote="72" seqPosition="4" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = BuildFrom(preset);

        //Act
        var synthesizer = world.NewSynthesizer();

        //Assert
        synthesizer.Problems.Should().Contain(problem => problem.Contains("shared queue length"));
    }

    [Fact]
    public void seqMode_always_plays_every_zone_on_every_note()
    {
        //Arrange
        using var world = Build(3, "always", declareLength: false);

        //Act
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert - 0.1 + 0.2 + 0.3 all at once.
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.6, 0.003);
    }

    [Fact]
    public void no_seqMode_at_all_behaves_as_always()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/p1.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="1" />
                  <sample path="Samples/p2.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="2" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = BuildFrom(preset);

        //Act
        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.3, 0.003);
    }

    [Fact]
    public void random_never_repeats_a_position_twice_in_a_row()
    {
        //Arrange
        using var world = Build(3, "random", declareLength: false);

        //Act
        var positions = world.PlayNotes(40);

        //Assert
        for (var i = 1; i < positions.Count; i++)
        {
            positions[i].Should().NotBe(positions[i - 1]);
        }

        positions.Distinct().Count().Should().Be(3);
    }

    [Fact]
    public void true_random_does_repeat()
    {
        //Arrange
        using var world = Build(3, "true_random", declareLength: false);

        //Act
        var positions = world.PlayNotes(60);

        //Assert
        var repeats = 0;
        for (var i = 1; i < positions.Count; i++)
        {
            if (positions[i] == positions[i - 1])
            {
                repeats++;
            }
        }

        repeats.Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_same_seed_gives_the_same_random_sequence()
    {
        //Arrange
        using var world = Build(4, "random", declareLength: false);

        //Act
        var first = world.PlayNotes(20);
        var second = world.PlayNotes(20);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void a_different_seed_gives_a_different_random_sequence()
    {
        //Arrange
        using var world = Build(5, "random", declareLength: false);

        //Act
        var first = world.PlayNotes(20);
        var second = world.PlayNotes(20, seed: 999);

        //Assert
        second.Should().NotEqual(first);
    }

    [Fact]
    public void reset_reseeds_so_a_replay_repeats()
    {
        //Arrange
        using var world = Build(3, "round_robin", declareLength: false);
        var synthesizer = world.NewSynthesizer();

        //Act
        var first = world.PlayNotesOn(synthesizer, 5);
        synthesizer.Reset();
        var second = world.PlayNotesOn(synthesizer, 5);

        //Assert
        second.Should().Equal(first);
    }

    private static RoundRobinWorld Build(int zoneCount, string mode, bool declareLength, int length = 0)
    {
        var samples = string.Join(
            "\n      ",
            Enumerable.Range(1, zoneCount).Select(position =>
                $"""<sample path="Samples/p{position}.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="{position}" />"""));

        var lengthAttribute = declareLength ? $""" seqLength="{length}" """ : " ";

        return BuildFrom($"""
            <DecentSampler>
              <groups>
                <group seqMode="{mode}"{lengthAttribute}ampVelTrack="0">
                  {samples}
                </group>
              </groups>
            </DecentSampler>
            """);
    }

    private static RoundRobinWorld BuildFrom(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        for (var position = 1; position <= 8; position++)
        {
            fixtures.WriteConstantWav($"Samples/p{position}.wav", value: 0.1f * position);
        }

        return new RoundRobinWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class RoundRobinWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer(int seed = 12345) =>
            DecentSamplerRenderProbe.Synthesizer(instrument, settings => settings.RandomSeed = seed);

        public IReadOnlyList<int> PlayNotes(int count, int seed = 12345) =>
            PlayNotesOn(NewSynthesizer(seed), count);

        // Each note is rendered on its own, then released and left to decay, so the level of the window
        // is the one zone that sounded. The level maps back to a position: 0.1 is position 1, and so on.
        public IReadOnlyList<int> PlayNotesOn(DecentSamplerSynthesizer synthesizer, int count)
        {
            var positions = new List<int>(count);

            for (var i = 0; i < count; i++)
            {
                synthesizer.NoteOn(0, 60, 100);
                var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
                positions.Add((int)Math.Round(DecentSamplerRenderProbe.Rms(left) / 0.1));

                synthesizer.NoteOffAll(immediate: true);
                DecentSamplerRenderProbe.RenderBlocks(synthesizer, 1);
            }

            return positions;
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

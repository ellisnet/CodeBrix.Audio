using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The effect chain at instrument and group level: order, bypass, tags, unregistered types, and the
/// per-voice instantiation the guide documents for a group chain.
/// </summary>
public class DecentSamplerEffectChainTests
{
    [Fact]
    public void the_instrument_chain_processes_the_whole_mix()
    {
        //Arrange
        using var world = ChainWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <effects>
                <effect type="gain" level="-6" />
              </effects>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        level.Should().BeApproximately(0.5 * 0.501187, 0.002);
    }

    [Fact]
    public void the_chain_runs_in_document_order()
    {
        //Arrange - a compressor is the one core effect whose output depends on its input level, so
        //putting a boost before it and after it are audibly different chains.
        using var boostFirst = ChainWorld.Build(Preset("""
            <effect type="gain" levelUnit="linear" level="2.0" />
            <effect type="compressor" threshold="-12" ratio="20" attack="1" release="200" />
            """));

        using var boostLast = ChainWorld.Build(Preset("""
            <effect type="compressor" threshold="-12" ratio="20" attack="1" release="200" />
            <effect type="gain" levelUnit="linear" level="2.0" />
            """));

        //Act
        var compressedAfterBoost = boostFirst.Play();
        var boostedAfterCompression = boostLast.Play();

        //Assert - boosting first drives the compressor harder, so it comes out quieter.
        compressedAfterBoost.Should().BeLessThan(boostedAfterCompression * 0.75);
    }

    [Fact]
    public void a_disabled_effect_is_bypassed_and_comes_back()
    {
        //Arrange
        using var world = ChainWorld.Build(Preset("<effect type=\"gain\" level=\"-12\" />"));

        //Act
        var quiet = world.Play();
        world.Instrument.Effects.Effects[0].Enabled = false;
        var loud = world.Play();

        //Assert
        quiet.Should().BeApproximately(0.5 * 0.251188, 0.002);
        loud.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void an_unregistered_effect_type_is_bypassed_and_named_in_problems()
    {
        //Arrange
        using var world = ChainWorld.Build(Preset("""
            <effect type="phaser" mix="1.0" />
            <effect type="gain" level="-6" />
            """));

        //Act
        var level = world.Play();

        //Assert - the rest of the chain still runs.
        level.Should().BeApproximately(0.5 * 0.501187, 0.002);

        world.Problems.Should().Contain(
            "effect type 'phaser' needs CodeBrix.Audio.ModestSynth: reference it and call " +
            "ModestSynth.Register() before loading");

        world.UnsupportedFeatures.Should().Contain("effect:phaser");
    }

    [Theory]
    [InlineData("pitch_shift")]
    [InlineData("wave_folder")]
    [InlineData("wave_shaper")]
    [InlineData("stereo_simulator")]
    [InlineData("bit_crusher")]
    [InlineData("gate")]
    public void every_add_on_effect_type_reports_the_same_way(string type)
    {
        //Arrange
        using var world = ChainWorld.Build(Preset($"<effect type=\"{type}\" />"));

        //Act
        world.Play();

        //Assert
        world.Problems.Should().Contain(problem => problem.Contains($"effect type '{type}' needs"));
    }

    [Fact]
    public void a_group_chain_is_instantiated_once_per_voice()
    {
        //Arrange
        using var world = ChainWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="48" hiNote="72" />
                  <effects><effect type="lowpass" frequency="500" /></effects>
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act - three notes at once need three chains; playing them again reuses those three.
        var first = world.CountChainsAfterChord([60, 64, 67]);
        var second = world.CountChainsAfterChord([60, 64, 67]);

        //Assert
        first.Should().Be(3);
        second.Should().Be(3);
    }

    [Fact]
    public void a_group_chain_filters_only_its_own_group()
    {
        //Arrange
        using var world = ChainWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                  <effects><effect type="gain" levelUnit="linear" level="0.5" /></effects>
                </group>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var filtered = world.Play(60);
        var plain = world.Play(62);

        //Assert
        filtered.Should().BeApproximately(0.25, 0.002);
        plain.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_group_chain_starts_every_note_with_cleared_state()
    {
        //Arrange - a delay at group level is exactly what the guide warns against, which makes it the
        //clearest probe: a reused chain that kept its line would leak the previous note's echo.
        using var world = ChainWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" release="0.001">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                  <effects>
                    <effect type="delay" delayTime="0.02" feedback="0.9" wetLevel="1.0" />
                  </effects>
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var first = world.PlayFirstBlock();
        var second = world.PlayFirstBlock();

        //Assert - the first block of the second note is the dry signal alone, as it was the first time.
        second.Should().BeApproximately(first, 1.0e-6);
    }

    private static string Preset(string effects) => $"""
        <DecentSampler>
          <groups>
            <group ampVelTrack="0">
              <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
            </group>
          </groups>
          <effects>
            {effects}
          </effects>
        </DecentSampler>
        """;

    [Theory]
    [InlineData(0.10, 0.12)]
    [InlineData(0.20, 0.06)]
    [InlineData(0.60, 6.65)]
    [InlineData(1.00, 10.53)]
    [InlineData(1.40, 13.09)]
    public void a_group_chain_runs_after_the_amplitude_envelope(double seconds, double expected)
    {
        //Arrange
        // MEASURED (round 2, item 24). A 1.5 s LINEAR attack ramp on a -8 dBFS sine through a group
        // compressor at threshold="-30" ratio="8": the gain reduction is ZERO while the ENVELOPED
        // signal is still under the knee and grows exactly as the envelope opens. A chain running in
        // front of the envelope would show one constant reduction all the way up the ramp instead.
        using var world = RampWorld.Build(compressed: true);
        using var reference = RampWorld.Build(compressed: false);

        //Act
        var reduction = reference.LevelAt(seconds) - world.LevelAt(seconds);

        //Assert
        reduction.Should().BeApproximately(expected, 1.0);
    }

    // A 1.5 s linear attack on a -8 dBFS root-mean-square sine, with and without a group compressor.
    private sealed class RampWorld : IDisposable
    {
        private readonly DecentSamplerEngineFixtures _fixtures;
        private readonly DecentSamplerInstrument _instrument;
        private readonly float[] _left;

        private RampWorld(DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument)
        {
            _fixtures = fixtures;
            _instrument = instrument;

            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
            synthesizer.NoteOn(0, 60, 100);
            (_left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 2.0);
        }

        public static RampWorld Build(bool compressed)
        {
            var fixtures = DecentSamplerEngineFixtures.Create();
            fixtures.WriteSineWav(
                "Samples/sine.wav", 440.0, amplitude: 0.5623f, frames: DecentSamplerEngineFixtures.SampleRate * 3);

            var effects = compressed
                ? "<effects><effect type=\"compressor\" threshold=\"-30\" ratio=\"8\" " +
                  "attack=\"1\" release=\"50\" /></effects>"
                : string.Empty;

            var xml =
                "<DecentSampler><groups>" +
                "<group ampVelTrack=\"0\" attack=\"1.5\" attackCurve=\"0\" decay=\"0.0\" sustain=\"1.0\">" +
                "<sample path=\"Samples/sine.wav\" rootNote=\"60\" loNote=\"60\" hiNote=\"60\" " +
                "pitchKeyTrack=\"0\" />" + effects + "</group></groups></DecentSampler>";

            return new RampWorld(fixtures, fixtures.LoadPreset(xml));
        }

        // The level in decibels over a 20 ms window centred on a moment in the ramp.
        public double LevelAt(double seconds)
        {
            var window = DecentSamplerEngineFixtures.SampleRate / 50;
            var from = (int)(seconds * DecentSamplerEngineFixtures.SampleRate) - (window / 2);

            return 20.0 * Math.Log10(DecentSamplerRenderProbe.Rms(_left, from, window));
        }

        public void Dispose()
        {
            _instrument.Dispose();
            _fixtures.Dispose();
        }
    }

    private sealed class ChainWorld : IDisposable
    {
        private readonly DecentSamplerEngineFixtures _fixtures;

        private ChainWorld(DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument)
        {
            _fixtures = fixtures;
            Instrument = instrument;
            Synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        }

        public DecentSamplerInstrument Instrument { get; }

        public DecentSamplerSynthesizer Synthesizer { get; }

        public IReadOnlyList<string> Problems => Synthesizer.Problems;

        public IReadOnlyCollection<string> UnsupportedFeatures => Synthesizer.UnsupportedFeatures;

        public static ChainWorld Build(string preset)
        {
            var fixtures = DecentSamplerEngineFixtures.Create();
            fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);

            return new ChainWorld(fixtures, fixtures.LoadPreset(preset));
        }

        public double Play(int key = 60)
        {
            Synthesizer.Reset();
            Synthesizer.NoteOn(0, key, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 8);
            return DecentSamplerRenderProbe.Rms(left, 64, left.Length - 64);
        }

        public double PlayFirstBlock()
        {
            Synthesizer.NoteOn(0, 60, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 1);
            var level = DecentSamplerRenderProbe.Rms(left);

            Synthesizer.NoteOffAll(immediate: true);
            DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 4);

            return level;
        }

        public int CountChainsAfterChord(int[] keys)
        {
            foreach (var key in keys)
            {
                Synthesizer.NoteOn(0, key, 100);
            }

            DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 4);
            Synthesizer.NoteOffAll(immediate: true);
            DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 2);

            return Synthesizer.GroupChainInstanceCount(0);
        }

        public void Dispose()
        {
            Instrument.Dispose();
            _fixtures.Dispose();
        }
    }
}

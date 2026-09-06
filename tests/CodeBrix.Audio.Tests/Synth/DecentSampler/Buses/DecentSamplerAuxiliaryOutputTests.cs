using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Buses;

/// <summary>
/// The auxiliary stereo outputs: how many an instrument has, how they come back separately, and what a
/// stereo consumer does with them.
/// </summary>
public class DecentSamplerAuxiliaryOutputTests
{
    [Fact]
    public void an_instrument_with_no_auxiliary_routing_has_no_auxiliary_outputs()
    {
        //Arrange
        using var world = AuxWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        //Assert
        world.Synthesizer.AuxiliaryOutputCount.Should().Be(0);
    }

    [Fact]
    public void the_count_is_the_highest_auxiliary_pair_the_preset_names()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_3"));

        //Act
        //Assert
        world.Synthesizer.AuxiliaryOutputCount.Should().Be(3);
    }

    [Fact]
    public void an_auxiliary_pair_folds_into_the_stereo_mix_by_default()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_1"));

        //Act
        var level = world.PlayMain();

        //Assert
        world.Synthesizer.FoldAuxiliaryOutputs.Should().BeTrue();
        level.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void an_auxiliary_pair_can_be_dropped_from_the_stereo_mix()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_1"));
        world.Synthesizer.FoldAuxiliaryOutputs = false;

        //Act
        var level = world.PlayMain();

        //Assert
        level.Should().Be(0.0);
    }

    [Fact]
    public void asking_for_the_pairs_separately_keeps_them_out_of_the_main_output()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_2"));

        //Act
        var (main, auxiliary) = world.PlayWithAuxiliary();

        //Assert
        main.Should().Be(0.0);
        auxiliary[0].Should().Be(0.0);
        auxiliary[1].Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_bus_can_send_itself_to_an_auxiliary_pair()
    {
        //Arrange
        using var world = AuxWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="AUX_STEREO_OUTPUT_1" output1Volume="0.5" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var (main, auxiliary) = world.PlayWithAuxiliary();

        //Assert
        world.Synthesizer.AuxiliaryOutputCount.Should().Be(1);
        main.Should().Be(0.0);
        auxiliary[0].Should().BeApproximately(0.25, 0.002);
    }

    [Fact]
    public void a_zone_can_feed_the_main_output_and_an_auxiliary_pair_at_once()
    {
        //Arrange
        using var world = AuxWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="MAIN_OUTPUT" output1Volume="1.0"
                       output2Target="AUX_STEREO_OUTPUT_1" output2Volume="0.5">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var (main, auxiliary) = world.PlayWithAuxiliary();

        //Assert
        main.Should().BeApproximately(0.5, 0.002);
        auxiliary[0].Should().BeApproximately(0.25, 0.002);
    }

    [Fact]
    public void the_instrument_chain_does_not_touch_an_auxiliary_pair()
    {
        //Arrange
        using var world = AuxWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="AUX_STEREO_OUTPUT_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <effects>
                <effect type="gain" levelUnit="linear" level="0.25" />
              </effects>
            </DecentSampler>
            """);

        //Act
        var (_, auxiliary) = world.PlayWithAuxiliary();

        //Assert - an auxiliary pair is its own output, not part of the main mix the chain shapes.
        auxiliary[0].Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void render_with_auxiliary_rejects_a_buffer_that_is_too_short()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_1"));
        var left = new float[64];
        var right = new float[64];
        float[][] auxiliaryLeft = [new float[32]];
        float[][] auxiliaryRight = [new float[64]];

        //Act
        var act = () => world.Synthesizer.RenderWithAuxiliary(left, right, auxiliaryLeft, auxiliaryRight);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_synthesizer_is_a_multi_output_renderer()
    {
        //Arrange
        using var world = AuxWorld.Build(Preset("AUX_STEREO_OUTPUT_1"));

        //Act
        var renderer = (IMultiOutputRenderer)world.Synthesizer;

        //Assert
        renderer.AuxiliaryOutputCount.Should().Be(1);
        renderer.FoldAuxiliaryOutputs.Should().BeTrue();
    }

    private static string Preset(string target) => $"""
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" output1Target="{target}">
              <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
            </group>
          </groups>
        </DecentSampler>
        """;

    private sealed class AuxWorld : IDisposable
    {
        private const int Blocks = 8;

        private readonly DecentSamplerEngineFixtures _fixtures;

        private AuxWorld(DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument)
        {
            _fixtures = fixtures;
            Instrument = instrument;
            Synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        }

        public DecentSamplerInstrument Instrument { get; }

        public DecentSamplerSynthesizer Synthesizer { get; }

        public static AuxWorld Build(string preset)
        {
            var fixtures = DecentSamplerEngineFixtures.Create();
            fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);

            return new AuxWorld(fixtures, fixtures.LoadPreset(preset));
        }

        public double PlayMain()
        {
            Synthesizer.NoteOn(0, 60, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, Blocks);
            return DecentSamplerRenderProbe.Rms(left, 64, left.Length - 64);
        }

        public (double Main, double[] Auxiliary) PlayWithAuxiliary()
        {
            var frames = Blocks * Synthesizer.BlockSize;
            var left = new float[frames];
            var right = new float[frames];

            var count = Synthesizer.AuxiliaryOutputCount;
            var auxiliaryLeft = new float[count][];
            var auxiliaryRight = new float[count][];

            for (var i = 0; i < count; i++)
            {
                auxiliaryLeft[i] = new float[frames];
                auxiliaryRight[i] = new float[frames];
            }

            Synthesizer.NoteOn(0, 60, 100);
            Synthesizer.RenderWithAuxiliary(left, right, auxiliaryLeft, auxiliaryRight);

            var levels = new double[count];
            for (var i = 0; i < count; i++)
            {
                levels[i] = DecentSamplerRenderProbe.Rms(auxiliaryLeft[i], 64, frames - 64);
            }

            return (DecentSamplerRenderProbe.Rms(left, 64, frames - 64), levels);
        }

        public void Dispose()
        {
            Instrument.Dispose();
            _fixtures.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Buses;

/// <summary>
/// Buses: their own chains, their volumes, where they send themselves, and what happens when a preset
/// asks for a routing that cannot be honoured.
/// </summary>
public class DecentSamplerBusTests
{
    [Fact]
    public void a_bus_at_minus_six_decibels_arrives_at_the_main_output_at_half_level()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="0.5" output1Target="MAIN_OUTPUT" output1Volume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        level.Should().BeApproximately(0.25, 0.002);
    }

    [Fact]
    public void an_output_volume_multiplies_the_bus_volume()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="0.5" output1Target="MAIN_OUTPUT" output1Volume="0.5" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        level.Should().BeApproximately(0.125, 0.002);
    }

    [Fact]
    public void a_bus_that_names_no_output_goes_to_the_main_mix()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        level.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_bus_routed_nowhere_is_silent()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="NO_OUTPUT" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        level.Should().Be(0.0);
    }

    [Fact]
    public void a_bus_chain_processes_everything_routed_to_it()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group ampVelTrack="0" output1Target="MAIN_OUTPUT">
                  <sample path="Samples/half.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="MAIN_OUTPUT">
                  <effects><effect type="gain" levelUnit="linear" level="0.5" /></effects>
                </bus>
              </buses>
            </DecentSampler>
            """);

        //Act
        var throughBus = world.PlayMain(60);
        var direct = world.PlayMain(62);

        //Assert
        throughBus.Should().BeApproximately(0.25, 0.002);
        direct.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_bus_that_sends_to_another_bus_is_silent_and_reported()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="0.5" output1Target="BUS_2" output1Volume="1.0" />
                <bus busVolume="0.5" output1Target="MAIN_OUTPUT" output1Volume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        // MEASURED (round 2, item 24): a bus whose own output target names another bus is SILENT in the
        // reference - a bus cannot feed a bus - so the send is dropped and the bus has nothing left to
        // write. The reference reports nothing; this engine reports it in Problems.
        level.Should().Be(0.0);
        world.Problems.Should().Contain(problem => problem.Contains("cannot feed another bus"));
    }

    [Fact]
    public void a_bus_sending_to_a_bus_declared_before_it_is_silent_too()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_2">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="0.5" output1Target="MAIN_OUTPUT" output1Volume="1.0" />
                <bus busVolume="1.0" output1Target="BUS_1" output1Volume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert - declaring the target first does not make a bus-to-bus send legal.
        level.Should().Be(0.0);
        world.Problems.Should().Contain(problem => problem.Contains("cannot feed another bus"));
    }

    [Fact]
    public void a_routing_cycle_cannot_arise_because_no_bus_may_feed_a_bus()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="BUS_2" output1Volume="1.0"
                     output2Target="MAIN_OUTPUT" output2Volume="1.0" />
                <bus busVolume="1.0" output1Target="BUS_1" output1Volume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert - both bus-to-bus sends are dropped, so the second output of bus 1 is all that is left.
        world.Problems.Should().Contain(problem => problem.Contains("cannot feed another bus"));
        level.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_bus_that_names_itself_as_an_output_is_reported()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="BUS_1" output1Volume="1.0"
                     output2Target="MAIN_OUTPUT" output2Volume="1.0" />
              </buses>
            </DecentSampler>
            """);

        //Act
        world.PlayMain();

        //Assert
        world.Problems.Should().Contain(problem => problem.Contains("cannot feed another bus"));
    }

    [Fact]
    public void a_group_routed_to_a_bus_the_preset_never_declares_is_silent_and_reported()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_4">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="MAIN_OUTPUT" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var level = world.PlayMain();

        //Assert
        // MEASURED (round 2, item 24): the reference does not fold an undeclared bus back to the main
        // output - the group is simply silent. It reports nothing; this engine reports it in Problems.
        level.Should().Be(0.0);
        world.Problems.Should().Contain(problem => problem.Contains("BUS_4"));
    }

    [Fact]
    public void the_bus_volume_is_live()
    {
        //Arrange
        using var world = BusWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_1">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <buses>
                <bus busVolume="1.0" output1Target="MAIN_OUTPUT" />
              </buses>
            </DecentSampler>
            """);

        //Act
        var before = world.PlayMain();
        world.Instrument.Buses[0].BusVolume = 0.25;
        var after = world.PlayMain();

        //Assert
        before.Should().BeApproximately(0.5, 0.002);
        after.Should().BeApproximately(0.125, 0.002);
    }

    private sealed class BusWorld : IDisposable
    {
        private readonly DecentSamplerEngineFixtures _fixtures;

        private BusWorld(DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument)
        {
            _fixtures = fixtures;
            Instrument = instrument;
            Synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        }

        public DecentSamplerInstrument Instrument { get; }

        public DecentSamplerSynthesizer Synthesizer { get; }

        public IReadOnlyList<string> Problems => Synthesizer.Problems;

        public static BusWorld Build(string preset)
        {
            var fixtures = DecentSamplerEngineFixtures.Create();
            fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);

            return new BusWorld(fixtures, fixtures.LoadPreset(preset));
        }

        public double PlayMain(int key = 60)
        {
            Synthesizer.Reset();
            Synthesizer.NoteOn(0, key, 100);

            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 8);

            return DecentSamplerRenderProbe.Rms(left, 64, left.Length - 64);
        }

        public void Dispose()
        {
            Instrument.Dispose();
            _fixtures.Dispose();
        }
    }
}

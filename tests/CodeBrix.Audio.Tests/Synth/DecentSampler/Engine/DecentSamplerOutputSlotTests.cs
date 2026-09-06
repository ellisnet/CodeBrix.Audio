using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The output-slot contract: how a routing target maps onto a slot, and what a zone's eight outputs and
/// their volumes do to the mix while the buses and auxiliary pairs are still folded into it.
/// </summary>
public class DecentSamplerOutputSlotTests
{
    [Fact]
    public void the_main_output_is_slot_zero()
    {
        //Arrange
        //Act
        var slot = DecentSamplerOutputSlot.FromTarget(DecentSamplerOutputTarget.MainOutput);

        //Assert
        slot.Kind.Should().Be(DecentSamplerOutputSlotKind.Main);
        slot.Index.Should().Be(0);
        slot.ToString().Should().Be("MAIN_OUTPUT");
    }

    [Fact]
    public void no_output_has_no_slot()
    {
        //Arrange
        //Act
        var slot = DecentSamplerOutputSlot.FromTarget(DecentSamplerOutputTarget.NoOutput);

        //Assert
        slot.Kind.Should().Be(DecentSamplerOutputSlotKind.None);
        slot.Index.Should().Be(-1);
    }

    [Theory]
    [InlineData(DecentSamplerOutputTarget.Bus1, 1, 1)]
    [InlineData(DecentSamplerOutputTarget.Bus16, 16, 16)]
    public void buses_occupy_slots_one_to_sixteen(DecentSamplerOutputTarget target, int number, int index)
    {
        //Arrange
        //Act
        var slot = DecentSamplerOutputSlot.FromTarget(target);

        //Assert
        slot.Kind.Should().Be(DecentSamplerOutputSlotKind.Bus);
        slot.Number.Should().Be(number);
        slot.Index.Should().Be(index);
    }

    [Theory]
    [InlineData(DecentSamplerOutputTarget.AuxStereoOutput1, 1, 17)]
    [InlineData(DecentSamplerOutputTarget.AuxStereoOutput16, 16, 32)]
    public void auxiliary_pairs_occupy_slots_seventeen_to_thirty_two(
        DecentSamplerOutputTarget target, int number, int index)
    {
        //Arrange
        //Act
        var slot = DecentSamplerOutputSlot.FromTarget(target);

        //Assert
        slot.Kind.Should().Be(DecentSamplerOutputSlotKind.Aux);
        slot.Number.Should().Be(number);
        slot.Index.Should().Be(index);
    }

    [Fact]
    public void every_slot_index_round_trips()
    {
        for (var index = 0; index < DecentSamplerOutputSlot.Count; index++)
        {
            DecentSamplerOutputSlot.FromIndex(index).Index.Should().Be(index);
        }

        DecentSamplerOutputSlot.FromIndex(-1).Should().Be(DecentSamplerOutputSlot.None);
        DecentSamplerOutputSlot.FromIndex(DecentSamplerOutputSlot.Count).Should().Be(DecentSamplerOutputSlot.None);
    }

    [Fact]
    public void slots_compare_by_index()
    {
        //Arrange
        var first = DecentSamplerOutputSlot.FromTarget(DecentSamplerOutputTarget.Bus3);
        var second = DecentSamplerOutputSlot.FromIndex(3);

        //Assert
        (first == second).Should().BeTrue();
        (first != DecentSamplerOutputSlot.Main).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals((object)second).Should().BeTrue();
    }

    [Fact]
    public void a_zone_routed_nowhere_is_silent()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="NO_OUTPUT">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        level.Should().Be(0.0);
    }

    [Fact]
    public void a_zone_routed_to_a_bus_the_preset_never_declares_is_silent()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="BUS_2">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        // MEASURED (round 2, item 24): the reference silences a group routed to a bus the <buses>
        // element does not declare, rather than folding it back into the main output.
        level.Should().Be(0.0);
    }

    [Fact]
    public void an_output_volume_scales_what_reaches_its_slot()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="MAIN_OUTPUT" output1Volume="0.25">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        level.Should().BeApproximately(0.125, 0.002);
    }

    [Fact]
    public void a_zone_can_feed_two_slots_at_once()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="MAIN_OUTPUT" output2Target="BUS_1"
                       output2Volume="0.5">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert - only the main output survives: the undeclared bus takes its share into silence.
        level.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void an_auxiliary_pair_folds_into_the_mix()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="AUX_STEREO_OUTPUT_3">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        level.Should().BeApproximately(0.5, 0.002);
    }

    private static SlotWorld Build(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);

        return new SlotWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class SlotWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public double Play()
        {
            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
            synthesizer.NoteOn(0, 60, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
            return DecentSamplerRenderProbe.Rms(left);
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

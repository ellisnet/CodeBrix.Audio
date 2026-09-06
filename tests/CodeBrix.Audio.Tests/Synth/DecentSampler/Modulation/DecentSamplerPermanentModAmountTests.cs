using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// <c>modAmount</c> on a PERMANENT binding - the depth the developer guide gives a
/// <c>&lt;midi&gt;&lt;velocity&gt;</c> binding.
/// </summary>
/// <remarks>
/// MEASURED (measurement round 2, item 23, finding 4): the reference player IGNORES it. A
/// <c>&lt;velocity&gt;</c> binding driving a filter cutoff moves the cutoff identically at
/// <c>modAmount="1.0"</c> and at <c>modAmount="0.3"</c>, at every velocity. The attribute is read
/// only by a modulator's own bindings, where it is the depth that scales the translated value. A
/// permanent binding therefore always writes the translated value outright, whatever depth it
/// carries. The blend machinery is left in place, unused, because the guide still describes the
/// attribute as a depth and a later version of the player may start honouring it.
/// </remarks>
public class DecentSamplerPermanentModAmountTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("1.0")]
    [InlineData("0.5")]
    [InlineData("0.0")]
    public void a_velocity_binding_writes_its_translated_value_whatever_depth_it_carries(string depth)
    {
        //Arrange
        using var harness = Harness(depth);

        //Act
        harness.Instrument.ProcessNoteOn(0, 60, 1);

        //Assert - velocity 1 over 0 to 127, translated onto 0 to 1.
        harness.Instrument.Groups[0].Volume.Should().BeApproximately(0.007874, 0.0005);
    }

    [Fact]
    public void firing_the_same_velocity_twice_lands_in_the_same_place()
    {
        //Arrange
        using var harness = Harness("0.5");

        //Act
        harness.Instrument.ProcessNoteOn(0, 60, 1);
        var once = harness.Instrument.Groups[0].Volume;

        harness.Instrument.ProcessNoteOn(0, 60, 1);
        var twice = harness.Instrument.Groups[0].Volume;

        //Assert
        once.Should().BeApproximately(0.007874, 0.0005);
        twice.Should().Be(once);
    }

    [Fact]
    public void a_different_velocity_moves_the_parameter_with_it()
    {
        //Arrange
        using var harness = Harness("0.3");

        //Act
        harness.Instrument.ProcessNoteOn(0, 60, 127);
        var loud = harness.Instrument.Groups[0].Volume;

        harness.Instrument.ProcessNoteOn(0, 60, 64);
        var middle = harness.Instrument.Groups[0].Volume;

        //Assert
        loud.Should().BeApproximately(1.0, 0.0005);
        middle.Should().BeApproximately(64.0 / 127.0, 0.0005);
    }

    private static ModulationHarness Harness(string depth)
    {
        var amount = depth == null ? string.Empty : " modAmount=\"" + depth + "\"";

        return ModulationHarness.Load(
            string.Empty,
            extraSections:
                "<midi><velocity><binding" + amount + " type=\"amp\" level=\"group\" " +
                "groupIndex=\"0\" parameter=\"GROUP_VOLUME\" translation=\"linear\" " +
                "translationOutputMin=\"0\" translationOutputMax=\"1\" /></velocity></midi>");
    }
}

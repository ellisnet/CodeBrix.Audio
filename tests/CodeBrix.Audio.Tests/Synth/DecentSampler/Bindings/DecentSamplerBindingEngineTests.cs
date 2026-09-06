using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Covers the live MIDI surface the synthesizer drives - <c>&lt;cc&gt;</c>, <c>&lt;note&gt;</c> and
/// <c>&lt;velocity&gt;</c> handlers - and the modulation seam the modulator runtime will use.
/// </summary>
public class DecentSamplerBindingEngineTests
{
    [Fact]
    public void a_controller_message_drives_the_control_it_is_bound_to()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"1\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>",
            body:
            "  <midi>\n" +
            "    <cc number=\"11\">\n" +
            "      <binding level=\"ui\" type=\"labeled_knob\" position=\"0\" parameter=\"value\" " +
            "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
            "    </cc>\n" +
            "  </midi>\n");

        //Act
        var fired = fixture.Engine.ProcessControlChange(0, 11, 64);

        //Assert
        fired.Should().BeTrue();
        fixture.Instrument.Controls[0].Value.Should().BeApproximately(64.0 / 127.0, 1e-9);
        fixture.Instrument.Groups[0].Volume.Should().BeApproximately(64.0 / 127.0, 1e-9);
    }

    [Fact]
    public void a_controller_fires_only_when_its_value_changes()
    {
        //Arrange
        using var fixture = ControllerFixture();

        //Act
        var firstZero = fixture.Engine.ProcessControlChange(0, 1, 0);
        var moved = fixture.Engine.ProcessControlChange(0, 1, 100);
        var again = fixture.Engine.ProcessControlChange(0, 1, 100);

        //Assert
        firstZero.Should().BeFalse();
        moved.Should().BeTrue();
        again.Should().BeFalse();
    }

    [Fact]
    public void a_controller_message_the_preset_does_not_listen_for_changes_nothing()
    {
        //Arrange
        using var fixture = ControllerFixture();

        //Act
        var fired = fixture.Engine.ProcessControlChange(0, 42, 100);

        //Assert
        fired.Should().BeFalse();
        fixture.Instrument.Groups[0].Volume.Should().Be(1.0);
    }

    [Fact]
    public void a_key_switch_note_fires_its_bindings()
    {
        //Arrange
        using var fixture = KeySwitchFixture();

        //Act
        fixture.Engine.ProcessNoteOn(0, 12, 100);

        //Assert
        fixture.Instrument.Groups[0].Enabled.Should().BeFalse();
        fixture.Instrument.Groups[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_note_outside_a_handlers_range_does_nothing()
    {
        //Arrange
        using var fixture = KeySwitchFixture();

        //Act
        fixture.Engine.ProcessNoteOn(0, 60, 100);

        //Assert
        fixture.Instrument.Groups[0].Enabled.Should().BeTrue();
        fixture.Instrument.Groups[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_handler_with_swallowNotes_stops_the_note_reaching_the_sampler()
    {
        //Arrange
        using var fixture = KeySwitchFixture();

        //Act
        var swallowed = fixture.Engine.ProcessNoteOn(0, 11, 100);

        //Assert
        swallowed.Should().BeTrue();
    }

    [Fact]
    public void a_note_off_only_reaches_a_handler_that_listens_for_one()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            body:
            "  <midi>\n" +
            "    <note note=\"11\" eventType=\"note_off\">\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "    </note>\n" +
            "  </midi>\n");

        //Act
        fixture.Engine.ProcessNoteOn(0, 11, 100);
        var afterNoteOn = fixture.Instrument.Groups[0].Enabled;
        fixture.Engine.ProcessNoteOff(0, 11, 0);

        //Assert
        afterNoteOn.Should().BeTrue();
        fixture.Instrument.Groups[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void a_velocity_handler_fires_on_every_note_on()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            body:
            "  <midi>\n" +
            "    <velocity>\n" +
            "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
            "    </velocity>\n" +
            "  </midi>\n");

        //Act
        fixture.Engine.ProcessNoteOn(0, 60, 127);

        //Assert
        fixture.Instrument.Groups[0].Volume.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void a_disabled_note_handler_is_inert()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            body:
            "  <midi>\n" +
            "    <note note=\"11\" enabled=\"false\">\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "    </note>\n" +
            "  </midi>\n");

        //Act
        fixture.Engine.ProcessNoteOn(0, 11, 100);

        //Assert
        fixture.Instrument.Groups[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_modulator_binding_never_writes_a_base_value()
    {
        //Arrange
        using var fixture = ModulatorFixture();

        //Act
        var group = fixture.Instrument.Groups[0];

        //Assert
        group.Volume.Should().Be(1.0);
    }

    [Fact]
    public void a_modulator_contribution_moves_the_effective_value_and_can_be_taken_away()
    {
        //Arrange
        using var fixture = ModulatorFixture();
        var binding = fixture.Instrument.Modulators[0].Bindings[0];

        //Act
        fixture.Engine.ApplyModulation(binding, sourceId: 1, voiceId: -1, value: 1.0);
        var whileApplied = fixture.Instrument.Groups[0].Volume;
        fixture.Engine.ClearModulation(binding, sourceId: 1, voiceId: -1);

        //Assert
        whileApplied.Should().BeApproximately(0.5, 1e-9);
        fixture.Instrument.Groups[0].Volume.Should().Be(1.0);
    }

    [Fact]
    public void a_voice_scope_modulator_is_cleared_when_the_voice_ends()
    {
        //Arrange
        using var fixture = ModulatorFixture(scope: "voice");
        var binding = fixture.Instrument.Modulators[0].Bindings[0];
        var route = fixture.Engine.RouteFor(binding);
        var target = route.Targets[0];

        //Act
        fixture.Engine.ApplyModulation(binding, sourceId: 1, voiceId: 3, value: 1.0);
        var duringVoice = target.GetEffectiveValue(3).AsNumber;
        fixture.Engine.ClearVoiceModulation(3);

        //Assert
        route.IsVoiceScope.Should().BeTrue();
        duringVoice.Should().BeApproximately(0.5, 1e-9);
        target.GetEffectiveValue(3).AsNumber.Should().Be(1.0);
    }

    [Fact]
    public void a_modulators_route_carries_its_behaviour_and_depth()
    {
        //Arrange
        using var fixture = ModulatorFixture();

        //Act
        var route = fixture.Engine.RouteFor(fixture.Instrument.Modulators[0].Bindings[0]);

        //Assert
        route.IsTemporary.Should().BeTrue();
        route.Behavior.Should().Be(DecentSamplerModBehavior.Set);
        route.Amount.Should().Be(1.0);
    }

    private static DecentSamplerBindingFixture ControllerFixture() =>
        DecentSamplerBindingFixture.Build(
            body:
            "  <midi>\n" +
            "    <cc number=\"1\">\n" +
            "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
            "    </cc>\n" +
            "  </midi>\n");

    private static DecentSamplerBindingFixture KeySwitchFixture() =>
        DecentSamplerBindingFixture.Build(
            body:
            "  <midi>\n" +
            "    <note note=\"11\" eventType=\"note_on\" swallowNotes=\"true\">\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"true\" />\n" +
            "      <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "    </note>\n" +
            "    <note note=\"12\" eventType=\"note_on\">\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "      <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"true\" />\n" +
            "    </note>\n" +
            "  </midi>\n",
            groups:
            "    <group name=\"a\"><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
            "    <group name=\"b\"><sample path=\"Samples/other.wav\" rootNote=\"60\" /></group>\n");

    private static DecentSamplerBindingFixture ModulatorFixture(string scope = "global") =>
        DecentSamplerBindingFixture.Build(
            body:
            "  <modulators>\n" +
            $"    <lfo shape=\"sine\" frequency=\"2\" modAmount=\"1.0\" scope=\"{scope}\">\n" +
            "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"0.5\" />\n" +
            "    </lfo>\n" +
            "  </modulators>\n");
}

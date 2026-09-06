using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Covers the live control surface: what the initial state does at load, what moving a control does
/// afterwards, and what a host can observe while it happens.
/// </summary>
/// <remarks>
/// The initial state is the reason this phase exists. Every preset in the nine-library corpus writes
/// its balance into its knobs and expects those values to be in the engine before the first note.
/// </remarks>
public class DecentSamplerControlTests
{
    [Fact]
    public void a_labeled_knobs_initial_value_drives_its_binding_at_load()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" label=\"Dry\" " +
            "minValue=\"0\" maxValue=\"1\" value=\"0.35\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>");

        //Act
        var group = fixture.Instrument.Groups[0];

        //Assert
        group.Volume.Should().Be(0.35);
        fixture.Instrument.Controls[0].Value.Should().Be(0.35);
        fixture.Instrument.Controls[0].Label.Should().Be("Dry");
    }

    [Fact]
    public void a_binding_with_triggerOnLoad_false_stays_quiet_at_load_and_fires_on_a_move()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.2\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "triggerOnLoad=\"false\" />\n" +
            "      </labeled-knob>");
        var group = fixture.Instrument.Groups[0];
        group.Volume.Should().Be(1.0);

        //Act
        fixture.Instrument.Controls[0].SetValue(0.6);

        //Assert
        group.Volume.Should().Be(0.6);
    }

    [Fact]
    public void a_menus_selected_option_fires_its_bindings_at_load()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Menu(selected: 2), groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Enabled.Should().BeFalse();
        groups[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void selecting_a_menu_option_fires_that_options_bindings()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Menu(selected: 2), groups: TwoGroups);

        //Act
        fixture.Instrument.Controls[0].Select(0);

        //Assert
        fixture.Instrument.Groups[0].Enabled.Should().BeTrue();
        fixture.Instrument.Groups[1].Enabled.Should().BeFalse();
    }

    [Fact]
    public void a_menus_value_is_one_based_and_its_selected_index_is_not()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Menu(selected: 2), groups: TwoGroups);

        //Act
        var menu = fixture.Instrument.Controls[0];

        //Assert
        menu.Kind.Should().Be(DecentSamplerControlKind.Menu);
        menu.Value.Should().Be(2.0);
        menu.SelectedIndex.Should().Be(1);
        menu.Options.Should().Equal(["First", "Second"]);
    }

    [Fact]
    public void a_menu_value_of_zero_selects_nothing()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Menu(selected: 0), groups: TwoGroups);

        //Act
        var menu = fixture.Instrument.Controls[0];

        //Assert
        menu.SelectedIndex.Should().Be(-1);
        fixture.Instrument.Groups[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_buttons_selected_state_fires_its_bindings_at_load()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Button(selected: 1), groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Enabled.Should().BeFalse();
        groups[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_button_state_can_be_chosen_by_name()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(Button(selected: 1), groups: TwoGroups);

        //Act
        var found = fixture.Instrument.Controls[0].Select("english");

        //Assert
        found.Should().BeTrue();
        fixture.Instrument.Groups[0].Enabled.Should().BeTrue();
        fixture.Instrument.Groups[1].Enabled.Should().BeFalse();
    }

    [Fact]
    public void a_multi_state_control_fires_the_selected_states_bindings_at_load()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <control x=\"0\" y=\"0\" width=\"40\" height=\"40\" parameterName=\"Language\" " +
            "valueType=\"multi_state\" value=\"1\">\n" +
            "        <state name=\"English\">\n" +
            "          <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "translation=\"fixed_value\" translationValue=\"0.2\" />\n" +
            "        </state>\n" +
            "        <state name=\"French\">\n" +
            "          <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" " +
            "translation=\"fixed_value\" translationValue=\"0.8\" />\n" +
            "        </state>\n" +
            "      </control>");

        //Act
        var control = fixture.Instrument.Controls[0];

        //Assert
        control.ValueType.Should().Be(DecentSamplerValueType.MultiState);
        control.States.Should().Equal(["English", "French"]);
        control.SelectedIndex.Should().Be(1);
        fixture.Instrument.Groups[0].Volume.Should().Be(0.8);
    }

    [Fact]
    public void an_xy_pad_fires_both_axes_at_load()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(XyPad(), groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Volume.Should().BeApproximately(0.25, 1e-9);
        groups[1].Volume.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void setting_an_xy_pads_axis_re_fires_only_that_axis()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(XyPad(), groups: TwoGroups);

        //Act
        fixture.Instrument.Controls[0].SetXValue(1.0);

        //Assert
        fixture.Instrument.Controls[0].XValue.Should().Be(1.0);
        fixture.Instrument.Groups[0].Volume.Should().BeApproximately(1.0, 1e-9);
        fixture.Instrument.Groups[1].Volume.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void SetValue_re_fires_the_controls_bindings()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"1\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>");

        //Act
        fixture.Instrument.Controls[0].SetValue(0.125);

        //Assert
        fixture.Instrument.Groups[0].Volume.Should().Be(0.125);
        fixture.Instrument.Controls[0].Value.Should().Be(0.125);
    }

    [Fact]
    public void a_control_raises_its_changed_event_when_a_binding_moves_it()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"1\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>");
        var seen = new List<string>();
        fixture.Instrument.Controls[0].Changed += (_, arguments) => seen.Add(arguments.PropertyName);

        //Act
        fixture.Instrument.Controls[0].SetValue(0.5);

        //Assert
        seen.Should().Equal([nameof(DecentSamplerControl.Value)]);
    }

    [Fact]
    public void the_instrument_reports_every_parameter_change()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"1\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>");
        var changes = new List<DecentSamplerParameterChangedEventArgs>();
        fixture.Instrument.ParameterChanged += (_, arguments) => changes.Add(arguments);
        var before = fixture.Instrument.ParameterVersion;

        //Act
        fixture.Instrument.Controls[0].SetValue(0.5);

        //Assert
        changes.Should().Contain(change =>
            change.Parameter == "AMP_VOLUME" && change.TargetName == "group[0].AMP_VOLUME" &&
            change.NumberValue == 0.5);
        fixture.Instrument.ParameterVersion.Should().BeGreaterThan(before);
    }

    [Fact]
    public void a_control_can_be_found_by_name()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <control x=\"0\" y=\"0\" width=\"40\" height=\"40\" parameterName=\"MIC 1\" " +
            "minValue=\"0\" maxValue=\"1\" value=\"0.6\" />");

        //Act
        var control = fixture.Instrument.GetControl("mic 1");

        //Assert
        control.Should().NotBeNull();
        control.Value.Should().Be(0.6);
    }

    [Fact]
    public void a_label_keeps_its_text_and_a_binding_can_rewrite_it()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <label x=\"0\" y=\"0\" width=\"40\" height=\"20\" text=\"Choose\" />\n" +
            "      <menu x=\"0\" y=\"30\" width=\"60\" height=\"20\" value=\"1\">\n" +
            "        <option name=\"First\">\n" +
            "          <binding type=\"control\" level=\"ui\" position=\"0\" parameter=\"TEXT\" " +
            "translation=\"fixed_value\" translationValue=\"You chose the first option.\" />\n" +
            "        </option>\n" +
            "      </menu>");

        //Act
        var label = fixture.Instrument.Controls[0];

        //Assert
        label.Text.Should().Be("You chose the first option.");
    }

    [Fact]
    public void the_background_image_is_live_and_a_binding_can_replace_it()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Load(
            "<DecentSampler minVersion=\"1.0.0\">\n" +
            "  <ui width=\"812\" height=\"375\" bgImage=\"Images/one.png\">\n" +
            "    <tab>\n" +
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Night\">\n" +
            "          <binding type=\"general\" level=\"ui\" parameter=\"BG_IMAGE\" " +
            "translation=\"fixed_value\" translationValue=\"Images/two.png\" />\n" +
            "        </state>\n" +
            "      </button>\n" +
            "    </tab>\n" +
            "  </ui>\n" +
            "  <groups>\n" +
            "    <group><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
            "  </groups>\n" +
            "</DecentSampler>\n");

        //Act
        var background = fixture.Instrument.BackgroundImage;

        //Assert
        background.Should().Be("Images/two.png");
    }

    private const string TwoGroups =
        "    <group name=\"a\"><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
        "    <group name=\"b\"><sample path=\"Samples/other.wav\" rootNote=\"60\" /></group>\n";

    private static string Menu(int selected) =>
        $"      <menu x=\"0\" y=\"0\" width=\"120\" height=\"30\" value=\"{selected}\">\n" +
        "        <option name=\"First\">\n" +
        "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "          <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"false\" />\n" +
        "        </option>\n" +
        "        <option name=\"Second\">\n" +
        "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"false\" />\n" +
        "          <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "        </option>\n" +
        "      </menu>";

    private static string Button(int selected) =>
        $"      <button x=\"0\" y=\"0\" width=\"70\" height=\"50\" style=\"text\" value=\"{selected}\">\n" +
        "        <state name=\"English\">\n" +
        "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "          <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"false\" />\n" +
        "        </state>\n" +
        "        <state name=\"French\">\n" +
        "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"false\" />\n" +
        "          <binding type=\"general\" level=\"group\" position=\"1\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "        </state>\n" +
        "      </button>";

    private static string XyPad() =>
        "      <xyPad x=\"0\" y=\"0\" width=\"300\" height=\"100\" xValue=\"0.25\" yValue=\"0.75\">\n" +
        "        <x>\n" +
        "          <binding type=\"amp\" level=\"group\" groupIndex=\"0\" parameter=\"AMP_VOLUME\" " +
        "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
        "        </x>\n" +
        "        <y>\n" +
        "          <binding type=\"amp\" level=\"group\" groupIndex=\"1\" parameter=\"AMP_VOLUME\" " +
        "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
        "        </y>\n" +
        "      </xyPad>";
}

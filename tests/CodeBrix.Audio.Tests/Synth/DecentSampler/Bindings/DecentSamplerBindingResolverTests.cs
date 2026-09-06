using System.Globalization;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Covers every way Appendix B lets a binding name its target: by position, by each typed index, by
/// tags, and by identifier - and covers what happens when it names nothing.
/// </summary>
public class DecentSamplerBindingResolverTests
{
    private const string TwoGroups =
        "    <group name=\"dry\" tags=\"close\">\n" +
        "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" tags=\"mic1\" />\n" +
        "    </group>\n" +
        "    <group name=\"wet\" tags=\"far\">\n" +
        "      <sample path=\"Samples/other.wav\" rootNote=\"60\" tags=\"mic2\" />\n" +
        "    </group>\n";

    [Fact]
    public void a_group_binding_addressed_by_position_reaches_that_group()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.25, "<binding type=\"amp\" level=\"group\" position=\"1\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Volume.Should().Be(1.0);
        groups[1].Volume.Should().Be(0.25);
    }

    [Fact]
    public void a_group_binding_addressed_by_groupIndex_reaches_that_group()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.25, "<binding type=\"amp\" level=\"group\" groupIndex=\"0\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Volume.Should().Be(0.25);
        groups[1].Volume.Should().Be(1.0);
    }

    [Fact]
    public void a_group_binding_addressed_by_groupTags_reaches_every_matching_group()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.5, "<binding type=\"amp\" level=\"group\" groupTags=\"far\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Volume.Should().Be(1.0);
        groups[1].Volume.Should().Be(0.5);
    }

    [Fact]
    public void a_bare_tags_list_is_promoted_into_groupTags_at_group_level()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.5, "<binding type=\"amp\" level=\"group\" tags=\"close\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Volume.Should().Be(0.5);
        groups[1].Volume.Should().Be(1.0);
    }

    [Fact]
    public void an_instrument_binding_reaches_every_group()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.4, "<binding type=\"amp\" level=\"instrument\" position=\"0\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups.Should().AllSatisfy(group => group.InstrumentVolume.Should().Be(0.4));
    }

    [Fact]
    public void level_groups_is_an_alias_for_level_instrument()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.4, "<binding type=\"amp\" level=\"groups\" position=\"0\" parameter=\"AMP_VOLUME\" />"),
            groups: TwoGroups);

        //Act
        var problems = fixture.Instrument.Problems;

        //Assert
        fixture.Instrument.Groups[0].InstrumentVolume.Should().Be(0.4);
        problems.Should().NotContain(problem => problem.Contains("level"));
    }

    [Fact]
    public void a_sample_binding_addressed_by_sampleTags_reaches_only_that_sample()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(3.0, "<binding type=\"amp\" level=\"sample\" sampleTags=\"mic2\" parameter=\"TUNING\" />",
                minimum: -12.0, maximum: 12.0));

        //Act
        var zones = fixture.Instrument.Zones;

        //Assert
        zones[0].Tuning.Should().Be(0.0);
        zones[1].Tuning.Should().Be(3.0);
    }

    [Fact]
    public void a_bare_tags_list_is_promoted_into_sampleTags_at_sample_level()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(3.0, "<binding type=\"amp\" level=\"sample\" tags=\"mic1\" parameter=\"TUNING\" />",
                minimum: -12.0, maximum: 12.0));

        //Act
        var zones = fixture.Instrument.Zones;

        //Assert
        zones[0].Tuning.Should().Be(3.0);
        zones[1].Tuning.Should().Be(0.0);
    }

    [Fact]
    public void an_oscillator_binding_addressed_by_oscillatorTags_reaches_only_that_oscillator()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(2.0, "<binding type=\"amp\" level=\"oscillator\" oscillatorTags=\"osc2\" parameter=\"TUNING\" />",
                minimum: -12.0, maximum: 12.0),
            groups:
            "    <group>\n" +
            "      <oscillator waveform=\"saw\" tags=\"osc1\" />\n" +
            "      <oscillator waveform=\"square\" tags=\"osc2\" />\n" +
            "    </group>\n");

        //Act
        var zones = fixture.Instrument.Zones;

        //Assert
        zones[0].Tuning.Should().Be(0.0);
        zones[1].Tuning.Should().Be(2.0);
    }

    [Fact]
    public void a_tag_binding_names_its_tag_in_identifier()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.6, "<binding type=\"amp\" level=\"tag\" identifier=\"mic1\" parameter=\"AMP_VOLUME\" />"));

        //Act
        var tag = fixture.Instrument.TagStates.Single(state => state.Name == "mic1");

        //Assert
        tag.Volume.Should().Be(0.6);
    }

    [Fact]
    public void a_tag_polyphony_binding_reaches_a_tag_the_preset_declared()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(1.0,
                "<binding type=\"general\" level=\"tag\" identifier=\"mic1\" parameter=\"TAG_POLYPHONY\" " +
                "translation=\"fixed_value\" translationValue=\"1\" />"),
            body: "  <tags>\n    <tag name=\"mic1\" polyphony=\"12\" />\n  </tags>\n");

        //Act
        var tag = fixture.Instrument.Tags.Single();

        //Assert
        tag.Polyphony.Should().Be(1);
        fixture.Instrument.TagStates.Single(state => state.Name == "mic1").Polyphony.Should().Be(1);
    }

    [Fact]
    public void an_effect_binding_at_instrument_level_indexes_the_instrument_chain()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.8, "<binding type=\"effect\" level=\"instrument\" position=\"1\" parameter=\"FX_REVERB_WET_LEVEL\" />"),
            body:
            "  <effects>\n" +
            "    <effect type=\"lowpass\" frequency=\"8000\" />\n" +
            "    <effect type=\"reverb\" wetLevel=\"0.1\" />\n" +
            "  </effects>\n");

        //Act
        var effects = fixture.Instrument.Effects.Effects;

        //Assert
        effects[1].WetLevel.Should().Be(0.8);
    }

    [Fact]
    public void an_effect_index_is_counted_within_its_own_chain()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(900.0,
                "<binding type=\"effect\" level=\"group\" groupIndex=\"1\" effectIndex=\"0\" " +
                "parameter=\"FX_FILTER_FREQUENCY\" />", minimum: 0.0, maximum: 22000.0),
            body: "  <effects>\n    <effect type=\"reverb\" wetLevel=\"0.1\" />\n  </effects>\n",
            groups:
            "    <group name=\"a\">\n" +
            "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" />\n" +
            "      <effects><effect type=\"lowpass\" frequency=\"100\" /></effects>\n" +
            "    </group>\n" +
            "    <group name=\"b\">\n" +
            "      <sample path=\"Samples/other.wav\" rootNote=\"60\" />\n" +
            "      <effects><effect type=\"lowpass\" frequency=\"200\" /></effects>\n" +
            "    </group>\n");

        //Act
        var groups = fixture.Instrument.Groups;

        //Assert
        groups[0].Effects.Effects[0].Frequency.Should().Be(100.0);
        groups[1].Effects.Effects[0].Frequency.Should().Be(900.0);
        fixture.Instrument.Effects.Effects[0].WetLevel.Should().Be(0.1);
    }

    [Fact]
    public void an_effect_binding_addressed_by_effectTags_reaches_every_chain()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(500.0, "<binding type=\"effect\" level=\"instrument\" effectTags=\"tone\" " +
                        "parameter=\"FX_FILTER_FREQUENCY\" />", minimum: 0.0, maximum: 22000.0),
            body: "  <effects>\n    <effect type=\"lowpass\" frequency=\"100\" tags=\"tone\" />\n  </effects>\n",
            groups:
            "    <group>\n" +
            "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" />\n" +
            "      <effects><effect type=\"highpass\" frequency=\"200\" tags=\"tone\" /></effects>\n" +
            "    </group>\n");

        //Act
        var instrumentChain = fixture.Instrument.Effects.Effects;
        var groupChain = fixture.Instrument.Groups[0].Effects.Effects;

        //Assert
        instrumentChain[0].Frequency.Should().Be(500.0);
        groupChain[0].Frequency.Should().Be(500.0);
    }

    [Fact]
    public void a_bus_binding_is_addressed_by_busIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.3, "<binding type=\"amp\" level=\"bus\" busIndex=\"1\" parameter=\"BUS_VOLUME\" />"),
            body:
            "  <buses>\n" +
            "    <bus busVolume=\"1.0\" />\n" +
            "    <bus busVolume=\"1.0\" />\n" +
            "  </buses>\n");

        //Act
        var buses = fixture.Instrument.Buses;

        //Assert
        buses[0].BusVolume.Should().Be(1.0);
        buses[1].BusVolume.Should().Be(0.3);
    }

    [Fact]
    public void an_effect_on_a_bus_is_addressed_by_busIndex_and_effectIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.75, "<binding type=\"effect\" level=\"bus\" busIndex=\"0\" effectIndex=\"1\" " +
                       "parameter=\"FX_REVERB_WET_LEVEL\" />"),
            body:
            "  <buses>\n" +
            "    <bus busVolume=\"1.0\">\n" +
            "      <effects>\n" +
            "        <effect type=\"lowpass\" frequency=\"3000\" />\n" +
            "        <effect type=\"reverb\" wetLevel=\"0.1\" />\n" +
            "      </effects>\n" +
            "    </bus>\n" +
            "  </buses>\n");

        //Act
        var effects = fixture.Instrument.Buses[0].Effects.Effects;

        //Assert
        effects[1].WetLevel.Should().Be(0.75);
    }

    [Fact]
    public void a_modulator_binding_is_addressed_by_modulatorIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(4.0, "<binding type=\"modulator\" level=\"instrument\" modulatorIndex=\"1\" " +
                      "parameter=\"FREQUENCY\" />", minimum: 0.0, maximum: 20.0),
            body:
            "  <modulators>\n" +
            "    <lfo shape=\"sine\" frequency=\"1\" />\n" +
            "    <lfo shape=\"saw\" frequency=\"2\" />\n" +
            "  </modulators>\n");

        //Act
        var modulators = fixture.Instrument.Modulators.OfType<DecentSamplerLfoModulator>().ToArray();

        //Assert
        modulators[0].Frequency.Should().Be(1.0);
        modulators[1].Frequency.Should().Be(4.0);
    }

    [Fact]
    public void a_modulator_binding_addressed_by_modulatorTags_reaches_every_matching_modulator()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.25, "<binding type=\"modulator\" level=\"instrument\" modulatorTags=\"wobble\" " +
                       "parameter=\"MOD_AMOUNT\" />"),
            body:
            "  <modulators>\n" +
            "    <lfo shape=\"sine\" frequency=\"1\" modAmount=\"1\" tags=\"wobble\" />\n" +
            "    <lfo shape=\"saw\" frequency=\"2\" modAmount=\"1\" />\n" +
            "  </modulators>\n");

        //Act
        var modulators = fixture.Instrument.Modulators;

        //Assert
        modulators[0].ModAmount.Should().Be(0.25);
        modulators[1].ModAmount.Should().Be(1.0);
    }

    [Fact]
    public void a_note_sequence_binding_is_addressed_by_seqIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(2.5, "<binding type=\"note_sequence\" level=\"instrument\" seqIndex=\"1\" parameter=\"RATE\" />",
                minimum: 0.01, maximum: 100.0),
            body:
            "  <noteSequences>\n" +
            "    <sequence name=\"a\" length=\"4\" rate=\"1\" />\n" +
            "    <sequence name=\"b\" length=\"4\" rate=\"1\" />\n" +
            "  </noteSequences>\n");

        //Act
        var sequences = fixture.Instrument.Sequences;

        //Assert
        sequences[0].Rate.Should().Be(1.0);
        sequences[1].Rate.Should().Be(2.5);
    }

    [Fact]
    public void the_arpeggiator_is_a_singleton_and_needs_no_index()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(1.0, "<binding type=\"arpeggiator\" level=\"instrument\" parameter=\"ARP_ENABLED\" " +
                      "translation=\"fixed_value\" translationValue=\"true\" />"),
            body: "  <arpeggiator enabled=\"false\" arpOrder=\"up\" />\n");

        //Act
        var arpeggiator = fixture.Instrument.Arpeggiator;

        //Assert
        arpeggiator.Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_keyboard_colour_binding_is_addressed_by_colorIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Load(
            "<DecentSampler minVersion=\"1.0.0\">\n" +
            "  <ui width=\"812\" height=\"375\">\n" +
            "    <tab>\n" +
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"1\">\n" +
            "        <state name=\"Off\">\n" +
            "          <binding level=\"ui\" type=\"keyboard_color\" colorIndex=\"1\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "        <state name=\"On\">\n" +
            "          <binding level=\"ui\" type=\"keyboard_color\" colorIndex=\"1\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"true\" />\n" +
            "        </state>\n" +
            "      </button>\n" +
            "    </tab>\n" +
            "    <keyboard>\n" +
            "      <color loNote=\"36\" hiNote=\"50\" color=\"FF2C365E\" />\n" +
            "      <color loNote=\"51\" hiNote=\"57\" color=\"FF6D9DC5\" />\n" +
            "    </keyboard>\n" +
            "  </ui>\n" +
            "  <groups>\n" +
            "    <group><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
            "  </groups>\n" +
            "</DecentSampler>\n");

        //Act
        var colors = fixture.Instrument.Ui.Keyboard.Colors;

        //Assert
        colors[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_ui_control_index_counts_labels_and_images_too()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <label x=\"0\" y=\"0\" width=\"40\" height=\"20\" text=\"Volume\" />\n" +
            "      <labeled-knob x=\"0\" y=\"30\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.7\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>\n" +
            "      <label x=\"0\" y=\"80\" width=\"40\" height=\"20\" text=\"Tone\" />");

        //Act
        var controls = fixture.Instrument.Controls;

        //Assert
        controls.Should().HaveCount(3);
        controls[0].Kind.Should().Be(DecentSamplerControlKind.Label);
        controls[1].Kind.Should().Be(DecentSamplerControlKind.LabeledKnob);
        controls[1].Index.Should().Be(1);
        fixture.Instrument.Groups[0].Volume.Should().Be(0.7);
    }

    [Fact]
    public void controlTags_addresses_several_controls_at_once()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <control x=\"0\" y=\"0\" width=\"10\" height=\"10\" tags=\"hidden-set\" />\n" +
            "      <control x=\"0\" y=\"20\" width=\"10\" height=\"10\" tags=\"hidden-set\" />\n" +
            "      <button x=\"0\" y=\"40\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Hide\">\n" +
            "          <binding type=\"control\" level=\"ui\" tags=\"hidden-set\" parameter=\"VISIBLE\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "      </button>");

        //Act
        var controls = fixture.Instrument.Controls;

        //Assert
        controls[0].Visible.Should().BeFalse();
        controls[1].Visible.Should().BeFalse();
        controls[2].Visible.Should().BeTrue();
    }

    [Fact]
    public void a_midi_element_index_counts_cc_note_and_velocity_handlers_in_document_order()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Off\">\n" +
            "          <binding type=\"note\" level=\"midi\" midiElementIndex=\"2\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "      </button>",
            body:
            "  <midi>\n" +
            "    <cc number=\"1\" />\n" +
            "    <velocity />\n" +
            "    <note note=\"11\" enabled=\"true\" />\n" +
            "    <note note=\"12\" enabled=\"true\" />\n" +
            "  </midi>\n");

        //Act
        var handlers = fixture.Instrument.MidiHandlers;

        //Assert
        handlers[0].Should().BeOfType<DecentSamplerMidiCc>();
        handlers[1].Should().BeOfType<DecentSamplerMidiVelocity>();
        ((DecentSamplerMidiNote)handlers[2]).Enabled.Should().BeFalse();
        ((DecentSamplerMidiNote)handlers[3]).Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_note_binding_is_addressed_by_midiElementIndex_and_bindingIndex()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Off\">\n" +
            "          <binding type=\"note_binding\" level=\"midi\" midiElementIndex=\"0\" bindingIndex=\"1\" " +
            "parameter=\"ENABLED\" translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "      </button>",
            body:
            "  <midi>\n" +
            "    <note note=\"11\">\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"true\" />\n" +
            "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "    </note>\n" +
            "  </midi>\n");

        //Act
        var bindings = fixture.Instrument.MidiHandlers[0].Bindings;

        //Assert
        bindings[0].Enabled.Should().BeNull();
        bindings[1].Enabled.Should().BeFalse();
    }

    [Fact]
    public void a_button_state_binding_is_addressed_by_control_state_and_binding_index()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"A\">\n" +
            "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
            "translation=\"fixed_value\" translationValue=\"true\" />\n" +
            "        </state>\n" +
            "      </button>\n" +
            "      <button x=\"0\" y=\"20\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Lock\">\n" +
            "          <binding type=\"button_state_binding\" level=\"ui\" controlIndex=\"0\" stateIndex=\"0\" " +
            "bindingIndex=\"0\" parameter=\"ENABLED\" translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "      </button>");

        //Act
        var target = ((DecentSamplerUiButton)fixture.Instrument.Ui.Controls[0]).States[0].Bindings[0];

        //Assert
        target.Enabled.Should().BeFalse();
    }

    [Fact]
    public void the_cc_binding_type_is_recognised_and_reaches_a_controllers_binding()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Lock\">\n" +
            "          <binding type=\"cc_binding\" level=\"midi\" midiElementIndex=\"0\" bindingIndex=\"0\" " +
            "parameter=\"ENABLED\" translation=\"fixed_value\" translationValue=\"false\" />\n" +
            "        </state>\n" +
            "      </button>",
            body:
            "  <midi>\n" +
            "    <cc number=\"1\">\n" +
            "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "    </cc>\n" +
            "  </midi>\n");

        //Act
        var target = fixture.Instrument.MidiHandlers[0].Bindings[0];

        //Assert
        target.Enabled.Should().BeFalse();
        fixture.Instrument.Problems.Should().NotContain(problem => problem.Contains("cc_binding"));
    }

    [Fact]
    public void a_binding_that_addresses_nothing_is_one_problem_and_not_an_exception()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.5, "<binding type=\"amp\" level=\"group\" position=\"9\" parameter=\"AMP_VOLUME\" />"));

        //Act
        var problems = fixture.Instrument.Problems;

        //Assert
        problems.Should().ContainSingle(problem => problem.StartsWith("Unresolved binding:"));
    }

    [Fact]
    public void a_binding_with_enabled_false_is_inert()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.5, "<binding enabled=\"false\" type=\"amp\" level=\"group\" position=\"0\" " +
                      "parameter=\"AMP_VOLUME\" />"));

        //Act
        var group = fixture.Instrument.Groups[0];

        //Assert
        group.Volume.Should().Be(1.0);
    }

    [Fact]
    public void an_all_notes_off_binding_raises_the_instruments_event()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"Panic\">\n" +
            "          <binding type=\"general\" level=\"instrument\" parameter=\"ALL_NOTES_OFF\" " +
            "translation=\"fixed_value\" translationValue=\"true\" triggerOnLoad=\"false\" />\n" +
            "        </state>\n" +
            "      </button>");
        var raised = 0;
        fixture.Instrument.AllNotesOffRequested += (_, _) => raised++;

        //Act
        fixture.Instrument.Controls[0].Select(0);

        //Assert
        raised.Should().Be(1);
    }

    [Fact]
    public void a_parameter_name_is_matched_without_regard_to_case()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            Knob(0.45, "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"amp_volume\" />"));

        //Act
        var group = fixture.Instrument.Groups[0];

        //Assert
        group.Volume.Should().Be(0.45);
    }

    private static string Knob(
        double value, string binding, double minimum = 0.0, double maximum = 1.0) =>
        $"      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"{Text(minimum)}\" " +
        $"maxValue=\"{Text(maximum)}\" value=\"{Text(value)}\">\n" +
        "        " + binding + "\n" +
        "      </labeled-knob>";

    private static string Text(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}

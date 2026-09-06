using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Proves that every parameter Appendix B lists has somewhere to go: a preset holding one of
/// everything is loaded, and each of the guide's parameter names is resolved against it at every level
/// the guide allows.
/// </summary>
/// <remarks>
/// This is the coverage claim in test form. If the guide gains a parameter, add it to
/// <c>DecentSamplerSupportedFeatures</c> and this test says at once whether the engine can reach it.
/// </remarks>
public class DecentSamplerParameterFactoryTests
{
    private static readonly (DecentSamplerBindingType Type, DecentSamplerBindingLevel Level)[] Candidates =
    [
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.General, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Group),
        (DecentSamplerBindingType.General, DecentSamplerBindingLevel.Group),
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Sample),
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Oscillator),
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Tag),
        (DecentSamplerBindingType.General, DecentSamplerBindingLevel.Tag),
        (DecentSamplerBindingType.Amp, DecentSamplerBindingLevel.Bus),
        (DecentSamplerBindingType.Effect, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.Effect, DecentSamplerBindingLevel.Bus),
        (DecentSamplerBindingType.Modulator, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.NoteSequence, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.Arpeggiator, DecentSamplerBindingLevel.Instrument),
        (DecentSamplerBindingType.Control, DecentSamplerBindingLevel.Ui),
        (DecentSamplerBindingType.KeyboardColor, DecentSamplerBindingLevel.Ui),
        (DecentSamplerBindingType.General, DecentSamplerBindingLevel.Ui),
        (DecentSamplerBindingType.Note, DecentSamplerBindingLevel.Midi),
        (DecentSamplerBindingType.NoteBinding, DecentSamplerBindingLevel.Midi),
        (DecentSamplerBindingType.CcBinding, DecentSamplerBindingLevel.Midi),
        (DecentSamplerBindingType.ButtonStateBinding, DecentSamplerBindingLevel.Ui),
    ];

    [Fact]
    public void every_binding_parameter_in_the_guide_has_a_live_target()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Load(Everything());
        var missing = new List<string>();

        //Act
        foreach (var parameter in DecentSamplerSupportedFeatures.BindingParameterNames)
        {
            if (!Reachable(fixture, parameter))
            {
                missing.Add(parameter);
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void the_guide_lists_more_than_three_hundred_binding_parameters() =>
        DecentSamplerSupportedFeatures.BindingParameterNames.Count.Should().BeGreaterThan(300);

    [Fact]
    public void two_spellings_of_one_parameter_share_one_target()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.5\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>\n" +
            "      <labeled-knob x=\"0\" y=\"50\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.25\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"amp_volume\" />\n" +
            "      </labeled-knob>");

        //Act
        var first = fixture.Engine.RouteFor(fixture.Instrument.Ui.Controls[0].Bindings[0]).Targets[0];
        var second = fixture.Engine.RouteFor(fixture.Instrument.Ui.Controls[1].Bindings[0]).Targets[0];

        //Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void a_controls_value_target_is_the_same_one_a_binding_writes()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"1\" />",
            body:
            "  <midi>\n" +
            "    <cc number=\"1\">\n" +
            "      <binding level=\"ui\" type=\"labeled_knob\" position=\"0\" parameter=\"value\" " +
            "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />\n" +
            "    </cc>\n" +
            "  </midi>\n");
        var fromBinding = fixture.Engine
            .RouteFor(fixture.Instrument.MidiHandlers[0].Bindings[0]).Targets[0];

        //Act
        fixture.Instrument.Controls[0].SetValue(0.5);

        //Assert
        fromBinding.BaseValue.AsNumber.Should().Be(0.5);
    }

    [Fact]
    public void two_bindings_on_one_parameter_share_one_target()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.5\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>\n" +
            "      <labeled-knob x=\"0\" y=\"50\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"1\" value=\"0.25\">\n" +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
            "      </labeled-knob>");

        //Act
        var first = fixture.Engine.RouteFor(fixture.Instrument.Ui.Controls[0].Bindings[0]).Targets[0];
        var second = fixture.Engine.RouteFor(fixture.Instrument.Ui.Controls[1].Bindings[0]).Targets[0];

        //Assert
        second.Should().BeSameAs(first);
        fixture.Instrument.Groups[0].Volume.Should().Be(0.25);
    }

    [Fact]
    public void an_output_target_binding_accepts_an_enumeration_name()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
            "        <state name=\"To bus 3\">\n" +
            "          <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"OUTPUT_1_TARGET\" " +
            "translation=\"fixed_value\" translationValue=\"BUS_3\" />\n" +
            "        </state>\n" +
            "      </button>");

        //Act
        var group = fixture.Instrument.Groups[0];

        //Assert
        group.OutputTargets[0].Should().Be(DecentSamplerOutputTarget.Bus3);
        group.Zones[0].OutputTargets[0].Should().Be(DecentSamplerOutputTarget.Bus3);
    }

    [Fact]
    public void a_group_level_envelope_binding_reaches_that_groups_zones_only()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"10\" value=\"2.5\">\n" +
            "        <binding type=\"amp\" level=\"group\" groupIndex=\"1\" parameter=\"ENV_ATTACK\" />\n" +
            "      </labeled-knob>",
            groups:
            "    <group name=\"a\"><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
            "    <group name=\"b\"><sample path=\"Samples/other.wav\" rootNote=\"60\" /></group>\n");

        //Act
        var zones = fixture.Instrument.Zones;

        //Assert
        zones[0].Attack.Should().Be(0.0);
        zones[1].Attack.Should().Be(2.5);
    }

    [Fact]
    public void an_instrument_level_envelope_binding_reaches_every_zone()
    {
        //Arrange
        using var fixture = DecentSamplerBindingFixture.Build(
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" minValue=\"0\" maxValue=\"15\" value=\"2\">\n" +
            "        <binding type=\"amp\" level=\"instrument\" position=\"0\" parameter=\"ENV_RELEASE\" />\n" +
            "      </labeled-knob>",
            groups:
            "    <group name=\"a\"><sample path=\"Samples/tone.wav\" rootNote=\"60\" /></group>\n" +
            "    <group name=\"b\"><sample path=\"Samples/other.wav\" rootNote=\"60\" release=\"0.1\" /></group>\n");

        //Act
        var zones = fixture.Instrument.Zones;

        //Assert
        zones.Should().AllSatisfy(zone => zone.Release.Should().Be(2.0));
    }

    private static bool Reachable(DecentSamplerBindingFixture fixture, string parameter)
    {
        foreach (var (type, level) in Candidates)
        {
            var binding = new DecentSamplerBinding
            {
                BindingType = type,
                TypeName = type.ToString(),
                Level = level,
                LevelName = level.ToString(),
                Parameter = parameter,
                Position = 0,
                ControlIndex = 0,
                GroupIndex = 0,
                EffectIndex = 0,
                ModulatorIndex = 0,
                BusIndex = 0,
                StateIndex = 0,
                BindingIndex = 0,
                MidiElementIndex = 0,
                ColorIndex = 0,
                SeqIndex = 0,
                Identifier = "mic1",
            };

            if (fixture.Engine.Resolver.Resolve(binding, out _).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string Everything() =>
        "<DecentSampler minVersion=\"1.0.0\">\n" +
        "  <ui width=\"812\" height=\"375\" bgImage=\"Images/bg.png\">\n" +
        "    <tab>\n" +
        "      <button x=\"0\" y=\"0\" width=\"10\" height=\"10\" value=\"0\">\n" +
        "        <state name=\"A\">\n" +
        "          <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "        </state>\n" +
        "      </button>\n" +
        "      <control x=\"0\" y=\"20\" width=\"10\" height=\"10\" minValue=\"0\" maxValue=\"1\" value=\"1\" />\n" +
        "      <label x=\"0\" y=\"40\" width=\"10\" height=\"10\" text=\"Hi\" />\n" +
        "      <menu x=\"0\" y=\"60\" width=\"10\" height=\"10\" value=\"1\"><option name=\"One\" /></menu>\n" +
        "      <xyPad x=\"0\" y=\"80\" width=\"10\" height=\"10\" xValue=\"0\" yValue=\"0\" />\n" +
        "      <image x=\"0\" y=\"100\" width=\"10\" height=\"10\" path=\"Images/a.png\" />\n" +
        "      <multiFrameImage x=\"0\" y=\"120\" width=\"10\" height=\"10\" path=\"Images/b.png\" " +
        "numFrames=\"4\" frameRate=\"12\" sourceFormat=\"vertical_image_strip\" />\n" +
        "      <rectangle x=\"0\" y=\"140\" width=\"10\" height=\"10\" fillColor=\"FF000000\" />\n" +
        "      <line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"10\" lineColor=\"FFFFFFFF\" />\n" +
        "    </tab>\n" +
        "    <keyboard>\n" +
        "      <color loNote=\"36\" hiNote=\"50\" color=\"FF2C365E\" />\n" +
        "    </keyboard>\n" +
        "  </ui>\n" +
        "  <groups>\n" +
        "    <group name=\"main\" tags=\"body\">\n" +
        "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" tags=\"mic1\" />\n" +
        "      <oscillator waveform=\"saw\" tags=\"osc1\" />\n" +
        "      <effects><effect type=\"lowpass\" frequency=\"8000\" /></effects>\n" +
        "    </group>\n" +
        "  </groups>\n" +
        "  <effects>\n" +
        "    <effect type=\"reverb\" wetLevel=\"0.2\" />\n" +
        "  </effects>\n" +
        "  <buses>\n" +
        "    <bus busVolume=\"1.0\"><effects><effect type=\"gain\" level=\"0\" /></effects></bus>\n" +
        "  </buses>\n" +
        "  <midi>\n" +
        "    <note note=\"11\">\n" +
        "      <binding type=\"general\" level=\"group\" position=\"0\" parameter=\"ENABLED\" " +
        "translation=\"fixed_value\" translationValue=\"true\" />\n" +
        "    </note>\n" +
        "    <cc number=\"1\">\n" +
        "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
        "    </cc>\n" +
        "    <velocity>\n" +
        "      <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />\n" +
        "    </velocity>\n" +
        "  </midi>\n" +
        "  <modulators>\n" +
        "    <lfo shape=\"sine\" frequency=\"2\" modAmount=\"1.0\" delayTime=\"0.1\" />\n" +
        "    <envelope attack=\"0.1\" decay=\"0.2\" sustain=\"0.5\" release=\"0.3\" modAmount=\"1.0\" />\n" +
        "    <random mode=\"note_on\" frequency=\"1\" seed=\"7\" />\n" +
        "  </modulators>\n" +
        "  <noteSequences>\n" +
        "    <sequence name=\"one\" length=\"4\" rate=\"1\">\n" +
        "      <note position=\"0\" velocity=\"100\" note=\"60\" length=\"1\" />\n" +
        "    </sequence>\n" +
        "  </noteSequences>\n" +
        "  <arpeggiator enabled=\"false\" arpOrder=\"up\" />\n" +
        "  <tags>\n" +
        "    <tag name=\"mic1\" volume=\"1\" polyphony=\"12\" />\n" +
        "  </tags>\n" +
        "</DecentSampler>\n";
}

using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// The <c>&lt;midi&gt;</c> element as the synthesizer runs it: controller handlers that reach the
/// sound, note handlers that swallow their key, velocity handlers, and the all-notes-off binding.
/// </summary>
public class DecentSamplerMidiRuntimeTests
{
    [Fact]
    public void a_cc_handler_changes_the_render_level()
    {
        //Arrange
        using var world = SequencingWorld.Build(VolumeFromControllerPreset());

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var full = world.Render(4);

        world.Synthesizer.NoteOffAll(true);
        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 20, 64);
        world.Synthesizer.NoteOn(0, 60, 100);
        var halved = world.Render(4);

        //Assert - 64/127 of the way from 0 to 1, which is what the linear translation sends.
        DecentSamplerRenderProbe.Rms(full).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
        DecentSamplerRenderProbe.Rms(halved)
            .Should().BeApproximately(SequencingWorld.BlipLevel * (64.0 / 127.0), 0.01);
    }

    [Fact]
    public void a_cc_of_zero_as_the_first_message_changes_nothing()
    {
        //Arrange - measured behaviour: a controller starts at 0, and a <cc> binding fires on change.
        using var world = SequencingWorld.Build(VolumeFromControllerPreset());

        //Act
        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 20, 0);
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.Render(4);

        //Assert
        DecentSamplerRenderProbe.Rms(render).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void a_key_switch_with_swallow_notes_never_sounds()
    {
        //Arrange
        using var world = SequencingWorld.Build(KeySwitchPreset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var swallowed = world.Render(4);

        //Assert
        world.Synthesizer.ActiveVoiceCount.Should().Be(0);
        DecentSamplerRenderProbe.Peak(swallowed).Should().BeLessThan(0.0001);
    }

    [Fact]
    public void a_key_switch_still_fires_its_bindings()
    {
        //Arrange - note 24 disables the group, note 25 enables it again.
        using var world = SequencingWorld.Build(KeySwitchPreset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        world.Synthesizer.NoteOn(0, 60, 100);
        var silenced = world.Render(4);

        world.Synthesizer.NoteOffAll(true);
        world.Synthesizer.NoteOn(0, 25, 100);
        world.Synthesizer.NoteOn(0, 60, 100);
        var restored = world.Render(4);

        //Assert
        DecentSamplerRenderProbe.Peak(silenced).Should().BeLessThan(0.0001);
        DecentSamplerRenderProbe.Rms(restored).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void a_note_outside_the_handlers_range_is_played_normally()
    {
        //Arrange
        using var world = SequencingWorld.Build(KeySwitchPreset());

        //Act - 26 is one above the handler range 24-25.
        world.Synthesizer.NoteOn(0, 26, 100);
        var render = world.Render(4);

        //Assert
        world.Synthesizer.ActiveVoiceCount.Should().Be(1);
        DecentSamplerRenderProbe.Rms(render).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void a_disabled_note_handler_neither_fires_nor_swallows()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="24" enabled="false" swallowNotes="true">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.Render(4);

        //Assert
        DecentSamplerRenderProbe.Rms(render).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void an_event_type_of_note_off_ignores_the_key_going_down()
    {
        //Arrange - the handler silences the group, but only when the key comes up.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="24" eventType="note_off" swallowNotes="true">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var before = world.Instrument.Groups[0].Enabled;

        world.Synthesizer.NoteOff(0, 24);
        var after = world.Instrument.Groups[0].Enabled;

        //Assert
        before.Should().BeTrue();
        after.Should().BeFalse();
    }

    [Fact]
    public void a_velocity_binding_moves_the_parameter_with_the_velocity()
    {
        //Arrange - the guide's own example shape: a <velocity> binding with modAmount. MEASURED
        // (round 2, item 23): the reference IGNORES modAmount on a permanent binding, so the
        // translated velocity is written outright.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <velocity>
                  <binding modAmount="0.5" level="group" type="amp" groupIndex="0"
                           parameter="AMP_VOLUME" translation="linear"
                           translationOutputMin="0" translationOutputMax="1" />
                </velocity>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 127);
        var loud = world.Render(4);

        world.Synthesizer.NoteOffAll(true);
        world.Synthesizer.NoteOn(0, 60, 1);
        var soft = world.Render(4);

        //Assert - velocity 127 opens the parameter fully and velocity 1 all but closes it.
        DecentSamplerRenderProbe.Rms(loud).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
        DecentSamplerRenderProbe.Rms(soft)
            .Should().BeApproximately(SequencingWorld.BlipLevel / 127.0, 0.01);
    }

    [Fact]
    public void a_velocity_binding_stays_inside_the_group_its_group_index_names()
    {
        //Arrange
        // A PUBLISHED DIVERGENCE. MEASURED (round 3, item 46): a permanent <midi><velocity> binding at
        // level="group" IGNORES its groupIndex in the reference and is applied to EVERY group - three
        // identical lowpass groups, only one of them named, tracked each other to 0.01 dB and to the
        // same cutoff at velocities 20, 64 and 127. A <modulators> binding's groupIndex does work
        // there, so the defect is specific to the permanent <midi> bindings, and this engine keeps the
        // documented behaviour.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60"
                          pitchKeyTrack="0" />
                </group>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
                  <sample path="Samples/blip.wav" rootNote="61" loNote="61" hiNote="61"
                          pitchKeyTrack="0" />
                </group>
              </groups>
              <midi>
                <velocity>
                  <binding level="group" type="amp" groupIndex="0" parameter="AMP_VOLUME"
                           translation="linear" translationOutputMin="0" translationOutputMax="1" />
                </velocity>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 1);
        var named = world.Render(4);

        world.Synthesizer.NoteOffAll(true);
        world.Synthesizer.NoteOn(0, 61, 1);
        var unnamed = world.Render(4);

        //Assert - the named group follows the velocity; the unnamed one is untouched by the binding.
        DecentSamplerRenderProbe.Rms(named)
            .Should().BeApproximately(SequencingWorld.BlipLevel / 127.0, 0.01);
        DecentSamplerRenderProbe.Rms(unnamed)
            .Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void a_binding_can_switch_a_note_handler_off()
    {
        //Arrange - key 20 turns the key switch on key 24 off, by its midiElementIndex.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="20" swallowNotes="true">
                  <binding type="note" level="midi" midiElementIndex="1" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
                <note note="24" swallowNotes="true">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 20, 100);
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.Render(4);

        //Assert - the key switch neither fired nor swallowed, so key 24 simply played.
        world.Instrument.Groups[0].Enabled.Should().BeTrue();
        DecentSamplerRenderProbe.Rms(render).Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void an_all_notes_off_binding_stops_every_voice()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding type="general" level="instrument" parameter="ALL_NOTES_OFF"
                           translation="fixed_value" translationValue="true" />
                </note>
              </midi>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var sounding = world.Synthesizer.ActiveVoiceCount;

        world.Synthesizer.NoteOn(0, 24, 100);
        world.Render(64);

        //Assert
        sounding.Should().Be(1);
        world.Synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void the_handlers_are_reported_as_implemented_features() =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Attribute, "note", "swallowNotes")
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    // A controller that drives the group's volume from zero to one.
    private static string VolumeFromControllerPreset() =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.BlipGroup()}}
          <midi>
            <cc number="20">
              <binding type="amp" level="group" groupIndex="0" parameter="AMP_VOLUME"
                       translation="linear" translationOutputMin="0" translationOutputMax="1" />
            </cc>
          </midi>
        </DecentSampler>
        """;

    // Note 24 turns the only group off and note 25 turns it back on, both swallowed. The shape of the
    // guide's own key-switch example, which uses one <note> handler per key with a fixed value.
    private static string KeySwitchPreset() =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.BlipGroup()}}
          <midi>
            <note note="24" enabled="true" eventType="note_on" swallowNotes="true">
              <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                       translation="fixed_value" translationValue="false" />
            </note>
            <note note="25" enabled="true" eventType="note_on" swallowNotes="true">
              <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                       translation="fixed_value" translationValue="true" />
            </note>
          </midi>
        </DecentSampler>
        """;
}

using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers the parser: every documented element reaching the model, every attribute landing on the right
/// property, the tolerant handling of things the engine does not know, and the one hard failure.
/// </summary>
public class DecentSamplerParserTests
{
    private const string FullPreset = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DecentSampler minVersion="1.29.0" pluginVersion="1">
          <ui coverArt="art.png" bgImage="bg.png" bgColor="FF000000" width="812" height="375"
              layoutMode="relative" bgMode="top_left">
            <tab name="main">
              <labeled-knob x="10" y="20" width="90" height="120" label="Attack" showLabel="true"
                            parameterName="Attack" style="rotary_vertical_drag" minValue="0" maxValue="4"
                            value="0.01" defaultValue="0.02" valueType="float" textColor="AA000000"
                            textSize="16" trackForegroundColor="CC000000" trackBackgroundColor="66999999"
                            tags="knobs" visible="true" enabled="true" disabledOpacity="0.5"
                            tooltip="Attack time" uid="abc" snapMode="tenths" snapStopPoints="0,0.5,1"
                            defeatSnapWithShift="true" customSkinImage="skin.png"
                            customSkinHoverImage="skin-hover.png" customSkinNumFrames="31"
                            customSkinImageOrientation="horizontal" mouseDragSensitivity="100">
                <binding type="amp" level="instrument" position="0" parameter="ENV_ATTACK"
                         translation="linear" translationOutputMin="0" translationOutputMax="4"
                         translationReversed="false" triggerOnLoad="true" enabled="true" />
              </labeled-knob>
              <control x="120" y="20" width="47" height="47" parameterName="Tone" valueType="multi_state"
                       value="0">
                <state name="Dark" mainImage="dark.png" hoverImage="dark-h.png" clickImage="dark-c.png">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="true" />
                </state>
                <state name="Bright">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </state>
              </control>
              <button x="200" y="20" width="14" height="14" value="1" style="image" mainImage="b.png"
                      hoverImage="bh.png" clickImage="bc.png" disabledOpacity="0.4" name="Strum">
                <state name="On">
                  <binding type="arpeggiator" level="instrument" parameter="ARP_ENABLED"
                           translation="fixed_value" translationValue="true" />
                </state>
              </button>
              <menu x="240" y="20" width="120" height="30" value="2" requireSelection="true"
                    placeholderText="Choose..." textColor="FFFFFFFF" backgroundColor="FF333333"
                    highlightedTextColor="FF000000" highlightedBackgroundColor="FFCCCCCC"
                    vAlign="center" hAlign="left">
                <option name="First">
                  <binding type="amp" level="tag" identifier="growl" parameter="TAG_ENABLED"
                           translation="fixed_value" translationValue="true" />
                </option>
                <option name="Second" />
              </menu>
              <xyPad x="380" y="20" width="300" height="100" parameterName="Pad" xValue="0.5" yValue="0.25"
                     markerDiameter="12" markerOutlineColor="FF111111" markerFillColor="FFFFFFFF"
                     outlineColor="77FFFFFF" bgColor="77FFCC00">
                <x>
                  <binding type="amp" level="group" groupIndex="0" parameter="AMP_VOLUME" />
                </x>
                <y>
                  <binding type="effect" level="instrument" effectIndex="0"
                           parameter="FX_FILTER_FREQUENCY" translation="table"
                           translationTable="0,33;1.0001,22000" />
                </y>
              </xyPad>
              <label x="10" y="200" width="50" height="30" text="Reverb" textColor="FFbefde2"
                     textSize="12" vAlign="top" hAlign="right" orientation="vertical_up" />
              <image x="70" y="200" width="40" height="40" path="pic.png" aspectRatioMode="stretch"
                     opacity="0.75" />
              <multiFrameImage x="120" y="200" width="64" height="64" path="anim.png" numFrames="31"
                               frameRate="24" opacity="0.9" sourceFormat="vertical_image_strip"
                               playbackMode="ping_pong_loop" />
              <rectangle x="0" y="0" width="812" height="375" fillColor="#FF2a2a2a"
                         borderColor="#FFe74c3c" borderThickness="2" />
              <line x1="30" y1="95" x2="270" y2="95" lineColor="#FFe74c3c" lineThickness="2" />
              <oscilloscope x="300" y="200" width="300" height="80" backgroundColor="#FF101010"
                            waveColor="#FF00FF88" lineThickness="1.5" showCenterLine="true" />
            </tab>
            <keyboard centerNote="60">
              <color loNote="36" hiNote="50" color="FF2C365E" />
            </keyboard>
          </ui>
          <groups volume="0dB" globalTuning="0.5" glideTime="0.3" glideMode="always" attack="0.01">
            <group name="main" enabled="true" volume="-6dB" ampVelTrack="0.5" groupTuning="1"
                   pitchKeyTrack="0.9" glideTime="0.2" glideMode="legato" tags="a,b"
                   silencedByTags="c" silencingMode="normal" silencingDecay="0.05"
                   seqMode="round_robin" seqLength="2" seqPosition="1" trigger="attack"
                   releaseTriggerDecay="3dB" playbackMode="memory" delay="0.5" delayUnit="beats"
                   retriggerEnabled="true" retriggerInterval="2" retriggerIntervalUnit="seconds"
                   loopCrossfade="1000" loopCrossfadeMode="linear" loopEnabled="true"
                   ampEnvEnabled="true" decay="0.2" sustain="0.8" release="1.5" attackCurve="0"
                   decayCurve="50" releaseCurve="-100" pan="-25"
                   output1Target="MAIN_OUTPUT" output1Volume="0.9"
                   output2Target="BUS_3" output2Volume="0.2"
                   loCC64="90" hiCC64="127" onLoCC11="1" onHiCC11="127">
              <sample path="Samples/a.wav" rootNote="C3" loNote="59" hiNote="61" loVel="1" hiVel="127"
                      start="10" end="1000" tuning="0.25" volume="0.5" pan="10" seqPosition="2"
                      loopStart="100" loopEnd="900" previousNotes="60,62" legatoInterval="-2"
                      length="1024" />
              <oscillator waveform="fm6op" damping="0.4" pluckType="0.6" wavetableFile="wt.wav"
                          wavetableFrameSize="1024" wavetablePosition="0.3" randomPhase="true"
                          wavetableFrameInterpolation="false" />
              <effects>
                <effect type="wave_folder" drive="4" threshold="0.5" />
              </effects>
            </group>
          </groups>
          <effects>
            <effect type="lowpass" frequency="8000" resonance="0.9" tags="main-filter" />
            <effect type="reverb" roomSize="0.85" damping="0.2" wetLevel="0.3" />
            <effect type="delay" delayTimeFormat="musical_time" delayTime="10" feedback="0.3"
                    stereoOffset="0.01" wetLevel="0.5" />
            <effect type="compressor" threshold="-12" ratio="4" attack="5" release="100"
                    inputGain="1" outputGain="2" autoBypass="true" />
            <effect type="stereo_simulator" algorithm="schroeder" width="0.6" delayTime="0.01"
                    modRate="0.4" modDepth="0.2" />
            <effect type="bit_crusher" bitDepth="8" sampleRateReduction="4" mix="1.0" />
            <effect type="gate" amount="0.25" mix="0.75" />
            <effect type="convolution" mix="0.4" irFile="Resources/ir.wav" />
            <effect type="gain" levelUnit="linear" level="0.5" />
            <effect type="peak" q="1.2" frequency="900" gain="0.8" />
            <effect type="lowpass_1pl" frequency="1200" />
            <effect type="pitch_shift" pitchShift="2" mix="0.5" />
            <effect type="wave_shaper" drive="10" driveBoost="0.3" outputLevel="0.2" highQuality="true" />
            <effect type="phaser" mix="0.5" modDepth="0.2" modRate="0.2" centerFrequency="400"
                    feedback="0.7" />
            <effect type="notch" q="0.7" frequency="500" />
            <effect type="bandpass" frequency="700" resonance="1.1" />
            <effect type="highpass" frequency="80" resonance="0.5" />
            <effect type="chorus" mix="0.5" modDepth="0.2" modRate="0.2" />
          </effects>
          <buses>
            <bus busVolume="0.5" output1Target="MAIN_OUTPUT" output1Volume="0.8"
                 output2Target="AUX_STEREO_OUTPUT_2" output2Volume="0.5">
              <effects>
                <effect type="reverb" wetLevel="0.5" roomSize="0.5" damping="0.5" />
              </effects>
            </bus>
          </buses>
          <midi>
            <cc number="11">
              <binding level="ui" type="control" position="0" parameter="VALUE" translation="linear"
                       translationOutputMin="0" translationOutputMax="1" />
            </cc>
            <note note="24-35" eventType="note_on" enabled="true" swallowNotes="true">
              <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                       translation="fixed_value" translationValue="true" />
            </note>
            <velocity>
              <binding modAmount="0.3" level="group" parameter="FX_FILTER_FREQUENCY" groupIndex="0"
                       effectIndex="0" type="effect" />
            </velocity>
          </midi>
          <modulators>
            <lfo shape="square" frequency="2" frequencyFormat="hz" modAmount="0.8" delayTime="0.5"
                 scope="voice" modBehavior="add" trigger="attack" tags="vibrato">
              <binding type="amp" level="group" groupIndex="0" parameter="GROUP_TUNING" />
            </lfo>
            <envelope attack="2" decay="0.5" sustain="0.7" release="1" attackCurve="-50"
                      decayCurve="25" releaseCurve="75" modAmount="1" scope="voice"
                      modBehavior="modulate" delayTime="0.1" />
            <midiCC number="1" modAmount="0.9" channel="voice" scope="voice" />
            <midiVelocity modAmount="0.7" scope="global" />
            <mpeTimbre scope="voice" risingSmoothingTime="20" fallingSmoothingTime="30" />
            <mpePressure scope="voice" risingSmoothingTime="10" fallingSmoothingTime="15" />
            <random mode="periodic" frequency="4" trigger="none" seed="12345" scope="global" />
          </modulators>
          <noteSequences>
            <sequence name="Maj1Slow" length="768.0" rate="96">
              <note position="0" velocity="1" note="48" length="768" />
              <note position="11" velocity="0.8" note="52" length="757" />
            </sequence>
          </noteSequences>
          <arpeggiator enabled="true" arpOrder="up_down_inclusive" arpOctaveRange="2"
                       arpOctaveMode="interleaveByPitch" arpStepCount="8" arpGateLength="0.6"
                       arpFollowGlobalTempo="false" arpSyncDivision="noteOneEighthDotted"
                       arpRateMultiplier="1.5" arpOverrideBpm="90" />
          <tags>
            <tag name="growl" enabled="false" volume="0.8" pan="10" polyphony="12" />
          </tags>
        </DecentSampler>
        """;

    private static DecentSamplerPreset Full() => DecentSamplerParser.ParseText(FullPreset);

    [Fact]
    public void a_preset_using_every_documented_element_parses_without_problems()
    {
        //Arrange
        //Act
        var preset = Full();

        //Assert
        preset.Problems.Should().BeEmpty();
        preset.AllUnknownAttributes().Should().BeEmpty();
        preset.AllUnknownElements().Should().BeEmpty();
    }

    [Fact]
    public void the_root_element_carries_its_versions()
    {
        //Arrange
        //Act
        var preset = Full();

        //Assert
        preset.MinVersion.Should().Be("1.29.0");
        preset.PluginVersion.Should().Be("1");
    }

    [Fact]
    public void groups_level_attributes_land_on_the_groups_element()
    {
        //Arrange
        //Act
        var groups = Full().Groups;

        //Assert
        groups.Volume.Should().BeApproximately(1.0, 1e-9);
        groups.VolumeInDecibels.Should().BeTrue();
        groups.VolumeText.Should().Be("0dB");
        groups.GlobalTuning.Should().Be(0.5);
        groups.GlideTime.Should().Be(0.3);
        groups.GlideMode.Should().Be(DecentSamplerGlideMode.Always);
        groups.Attack.Should().Be(0.01);
    }

    [Fact]
    public void every_group_attribute_lands_on_its_property()
    {
        //Arrange
        //Act
        var group = Full().Groups.Groups[0];

        //Assert
        group.Name.Should().Be("main");
        group.Enabled.Should().BeTrue();
        group.Volume.Should().BeApproximately(0.5011872336272722, 1e-9);
        group.AmpVelTrack.Should().Be(0.5);
        group.GroupTuning.Should().Be(1.0);
        group.PitchKeyTrack.Should().Be(0.9);
        group.GlideTime.Should().Be(0.2);
        group.GlideMode.Should().Be(DecentSamplerGlideMode.Legato);
        group.Tags.Should().Equal("a", "b");
        group.SilencedByTags.Should().Equal("c");
        group.SilencingMode.Should().Be(DecentSamplerSilencingMode.Normal);
        group.SilencingDecay.Should().Be(0.05);
        group.SeqMode.Should().Be(DecentSamplerSeqMode.RoundRobin);
        group.SeqLength.Should().Be(2);
        group.SeqPosition.Should().Be(1);
        group.Trigger.Should().Be(DecentSamplerTrigger.Attack);
        group.ReleaseTriggerDecay.Should().Be(-3.0);
        group.ReleaseTriggerDecayInDecibels.Should().BeTrue();
        group.PlaybackMode.Should().Be(DecentSamplerPlaybackMode.Memory);
        group.Delay.Should().Be(0.5);
        group.DelayUnit.Should().Be(DecentSamplerTimeUnit.Beats);
        group.RetriggerEnabled.Should().BeTrue();
        group.RetriggerInterval.Should().Be(2.0);
        group.RetriggerIntervalUnit.Should().Be(DecentSamplerTimeUnit.Seconds);
        group.LoopCrossfade.Should().Be(1000);
        group.LoopCrossfadeMode.Should().Be(DecentSamplerLoopCrossfadeMode.Linear);
        group.LoopEnabled.Should().BeTrue();
        group.AmpEnvEnabled.Should().BeTrue();
        group.Decay.Should().Be(0.2);
        group.Sustain.Should().Be(0.8);
        group.Release.Should().Be(1.5);
        group.AttackCurve.Should().Be(0.0);
        group.DecayCurve.Should().Be(50.0);
        group.ReleaseCurve.Should().Be(-100.0);
        group.Pan.Should().Be(-25.0);
        group.OutputTarget(0).Should().Be(DecentSamplerOutputTarget.MainOutput);
        group.OutputVolume(0).Should().Be(0.9);
        group.OutputTarget(1).Should().Be(DecentSamplerOutputTarget.Bus3);
        group.OutputVolume(1).Should().Be(0.2);
        group.CcFilters[64].Low.Should().Be(90);
        group.CcFilters[64].High.Should().Be(127);
        group.CcTriggers[11].Low.Should().Be(1);
        group.CcTriggers[11].High.Should().Be(127);
    }

    [Fact]
    public void every_sample_attribute_lands_on_its_property()
    {
        //Arrange
        //Act
        var sample = Full().Groups.Groups[0].Samples[0];

        //Assert
        sample.Path.Should().Be("Samples/a.wav");
        sample.RootNote.Should().Be(60);
        sample.LoNote.Should().Be(59);
        sample.HiNote.Should().Be(61);
        sample.LoVel.Should().Be(1);
        sample.HiVel.Should().Be(127);
        sample.Start.Should().Be(10);
        sample.End.Should().Be(1000);
        sample.Tuning.Should().Be(0.25);
        sample.Volume.Should().Be(0.5);
        sample.Pan.Should().Be(10.0);
        sample.SeqPosition.Should().Be(2);
        sample.LoopStart.Should().Be(100);
        sample.LoopEnd.Should().Be(900);
        sample.PreviousNotes.Should().Equal(60, 62);
        sample.LegatoInterval.Should().Be(-2);
        sample.Length.Should().Be(1024);
    }

    [Fact]
    public void oscillator_attributes_land_on_the_oscillator()
    {
        //Arrange
        //Act
        var oscillator = Full().Groups.Groups[0].Oscillators[0];

        //Assert
        oscillator.Waveform.Should().Be(DecentSamplerWaveform.Fm6Op);
        oscillator.WaveformName.Should().Be("fm6op");
        oscillator.Damping.Should().Be(0.4);
        oscillator.PluckType.Should().Be(0.6);
        oscillator.WavetableFile.Should().Be("wt.wav");
        oscillator.WavetableFrameSize.Should().Be(1024);
        oscillator.WavetablePosition.Should().Be(0.3);
        oscillator.RandomPhase.Should().BeTrue();
        oscillator.WavetableFrameInterpolation.Should().BeFalse();
    }

    [Fact]
    public void harmonic_and_fm_operator_attributes_are_read_by_index()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group numPartials="16" harmonicTilt="0.25" harmonicOddEvenBalance="0.75"
                       harmonicNormalization="0.5" harmonicPartial1Level="1.0"
                       harmonicPartial64Level="0.1" fmAlgorithm="32"
                       fmOp3Ratio="2.5" fmOp3Detune="-3" fmOp3Mode="fixed" fmOp3FixedFreq="220"
                       fmOp3Level="0.6" fmOp3VelocitySensitivity="4" fmOp3Feedback="0.2"
                       fmOp3Attack="0.1" fmOp3Decay="0.2" fmOp3Sustain="0.3" fmOp3Release="0.4"
                       fmOp3EgType="dx7" fmOp3EgRate1="90" fmOp3EgLevel4="7">
                  <oscillator waveform="harmonic" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var group = DecentSamplerParser.ParseText(xml).Groups.Groups[0];

        //Assert
        group.NumPartials.Should().Be(16);
        group.HarmonicTilt.Should().Be(0.25);
        group.HarmonicOddEvenBalance.Should().Be(0.75);
        group.HarmonicNormalization.Should().Be(0.5);
        group.HarmonicPartialLevel(0).Should().Be(1.0);
        group.HarmonicPartialLevel(63).Should().Be(0.1);
        group.FmAlgorithm.Should().Be(32);

        var third = group.FmOperator(2);
        third.Number.Should().Be(3);
        third.Ratio.Should().Be(2.5);
        third.Detune.Should().Be(-3.0);
        third.Mode.Should().Be(DecentSamplerFmOperatorMode.Fixed);
        third.FixedFrequency.Should().Be(220.0);
        third.Level.Should().Be(0.6);
        third.VelocitySensitivity.Should().Be(4.0);
        third.Feedback.Should().Be(0.2);
        third.Attack.Should().Be(0.1);
        third.Decay.Should().Be(0.2);
        third.Sustain.Should().Be(0.3);
        third.Release.Should().Be(0.4);
        third.EnvelopeType.Should().Be(DecentSamplerFmEnvelopeType.Dx7);
        third.EgRates[0].Should().Be(90.0);
        third.EgLevels[3].Should().Be(7.0);
    }

    [Fact]
    public void every_documented_effect_type_is_recognised()
    {
        //Arrange
        //Act
        var effects = Full().Effects.Effects;

        //Assert
        effects.Should().HaveCount(18);
        effects.Select(effect => effect.EffectType).Should()
            .NotContain(DecentSamplerEffectType.Unknown);
        effects[0].EffectType.Should().Be(DecentSamplerEffectType.Lowpass);
        effects[0].Frequency.Should().Be(8000.0);
        effects[0].Resonance.Should().Be(0.9);
        effects[0].Tags.Should().Equal("main-filter");
        effects[1].RoomSize.Should().Be(0.85);
        effects[1].Damping.Should().Be(0.2);
        effects[1].WetLevel.Should().Be(0.3);
        effects[2].DelayTimeFormat.Should().Be(DecentSamplerDelayTimeFormat.MusicalTime);
        effects[2].DelayTime.Should().Be(10.0);
        effects[2].Feedback.Should().Be(0.3);
        effects[2].StereoOffset.Should().Be(0.01);
        effects[3].Threshold.Should().Be(-12.0);
        effects[3].Ratio.Should().Be(4.0);
        effects[3].Attack.Should().Be(5.0);
        effects[3].Release.Should().Be(100.0);
        effects[3].InputGain.Should().Be(1.0);
        effects[3].OutputGain.Should().Be(2.0);
        effects[3].AutoBypass.Should().BeTrue();
        effects[4].Algorithm.Should().Be(DecentSamplerStereoSimulatorAlgorithm.Schroeder);
        effects[4].Width.Should().Be(0.6);
        effects[5].BitDepth.Should().Be(8.0);
        effects[5].SampleRateReduction.Should().Be(4.0);
        effects[6].Amount.Should().Be(0.25);
        effects[7].IrFile.Should().Be("Resources/ir.wav");
        effects[8].LevelUnit.Should().Be(DecentSamplerGainLevelUnit.Linear);
        effects[8].Level.Should().Be(0.5);
        effects[9].Q.Should().Be(1.2);
        effects[9].Gain.Should().Be(0.8);
        effects[11].PitchShift.Should().Be(2.0);
        effects[12].Drive.Should().Be(10.0);
        effects[12].DriveBoost.Should().Be(0.3);
        effects[12].OutputLevel.Should().Be(0.2);
        effects[12].HighQuality.Should().BeTrue();
        effects[13].CenterFrequency.Should().Be(400.0);
    }

    [Fact]
    public void the_legacy_lowpass_spelling_maps_to_the_two_pole_filter()
    {
        //Arrange
        var xml = "<DecentSampler><effects><effect type=\"lowpass_4pl\" frequency=\"1000\" />" +
                  "</effects></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.Effects.Effects[0].EffectType.Should().Be(DecentSamplerEffectType.Lowpass);
        preset.Effects.Effects[0].TypeName.Should().Be("lowpass_4pl");
    }

    [Fact]
    public void a_group_can_carry_its_own_effect_chain()
    {
        //Arrange
        //Act
        var group = Full().Groups.Groups[0];

        //Assert
        group.Effects.Should().NotBeNull();
        group.Effects.Effects.Should().HaveCount(1);
        group.Effects.Effects[0].EffectType.Should().Be(DecentSamplerEffectType.WaveFolder);
        group.Effects.Effects[0].Drive.Should().Be(4.0);
        group.Effects.Effects[0].Threshold.Should().Be(0.5);
    }

    [Fact]
    public void buses_carry_their_volume_routing_and_effects()
    {
        //Arrange
        //Act
        var bus = Full().Buses.Buses[0];

        //Assert
        bus.Index.Should().Be(0);
        bus.BusVolume.Should().Be(0.5);
        bus.OutputTarget(0).Should().Be(DecentSamplerOutputTarget.MainOutput);
        bus.OutputVolume(0).Should().Be(0.8);
        bus.OutputTarget(1).Should().Be(DecentSamplerOutputTarget.AuxStereoOutput2);
        bus.Effects.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void midi_handlers_are_indexed_in_document_order()
    {
        //Arrange
        //Act
        var midi = Full().Midi;

        //Assert
        midi.Handlers.Should().HaveCount(3);
        midi.Handlers[0].Should().BeOfType<DecentSamplerMidiCc>();
        midi.Handlers[1].Should().BeOfType<DecentSamplerMidiNote>();
        midi.Handlers[2].Should().BeOfType<DecentSamplerMidiVelocity>();
        midi.Handlers[2].Index.Should().Be(2);
        midi.CcHandlers[0].Number.Should().Be(11);

        var note = midi.NoteHandlers[0];
        note.NoteText.Should().Be("24-35");
        note.LowNote.Should().Be(24);
        note.HighNote.Should().Be(35);
        note.EventType.Should().Be(DecentSamplerMidiEventType.NoteOn);
        note.Enabled.Should().BeTrue();
        note.SwallowNotes.Should().BeTrue();
        midi.VelocityHandlers[0].Bindings[0].ModAmount.Should().Be(0.3);
    }

    [Fact]
    public void all_seven_modulator_kinds_parse_with_their_attributes()
    {
        //Arrange
        //Act
        var modulators = Full().Modulators.Modulators;

        //Assert
        modulators.Should().HaveCount(7);
        modulators.Select(modulator => modulator.Kind).Should().Equal(
            DecentSamplerModulatorKind.Lfo,
            DecentSamplerModulatorKind.Envelope,
            DecentSamplerModulatorKind.MidiCc,
            DecentSamplerModulatorKind.MidiVelocity,
            DecentSamplerModulatorKind.MpeTimbre,
            DecentSamplerModulatorKind.MpePressure,
            DecentSamplerModulatorKind.Random);

        var lfo = (DecentSamplerLfoModulator)modulators[0];
        lfo.Shape.Should().Be(DecentSamplerLfoShape.Square);
        lfo.Frequency.Should().Be(2.0);
        lfo.FrequencyFormat.Should().Be(DecentSamplerFrequencyFormat.Hz);
        lfo.ModAmount.Should().Be(0.8);
        lfo.DelayTime.Should().Be(0.5);
        lfo.Scope.Should().Be(DecentSamplerModulatorScope.Voice);
        lfo.ModBehavior.Should().Be(DecentSamplerModBehavior.Add);
        lfo.Trigger.Should().Be(DecentSamplerModulatorTrigger.Attack);
        lfo.Tags.Should().Equal("vibrato");
        lfo.Bindings.Should().HaveCount(1);

        var envelope = (DecentSamplerEnvelopeModulator)modulators[1];
        envelope.Attack.Should().Be(2.0);
        envelope.AttackCurve.Should().Be(-50.0);
        envelope.DelayTime.Should().Be(0.1);
        envelope.ModBehavior.Should().Be(DecentSamplerModBehavior.Modulate);

        var midiCc = (DecentSamplerMidiCcModulator)modulators[2];
        midiCc.Number.Should().Be(1);
        midiCc.ChannelFollowsVoice.Should().BeTrue();
        midiCc.Channel.Should().BeNull();

        ((DecentSamplerMidiVelocityModulator)modulators[3]).Scope
            .Should().Be(DecentSamplerModulatorScope.Global);
        ((DecentSamplerMpeTimbreModulator)modulators[4]).RisingSmoothingTime.Should().Be(20.0);
        ((DecentSamplerMpePressureModulator)modulators[5]).FallingSmoothingTime.Should().Be(15.0);

        var random = (DecentSamplerRandomModulator)modulators[6];
        random.Mode.Should().Be(DecentSamplerRandomMode.Periodic);
        random.Frequency.Should().Be(4.0);
        random.Trigger.Should().Be(DecentSamplerModulatorTrigger.None);
        random.Seed.Should().Be(12345);
    }

    [Fact]
    public void a_fixed_midi_cc_channel_is_read_as_a_number()
    {
        //Arrange
        var xml = "<DecentSampler><modulators><midiCC number=\"11\" channel=\"3\" />" +
                  "</modulators></DecentSampler>";

        //Act
        var modulator = (DecentSamplerMidiCcModulator)DecentSamplerParser.ParseText(xml)
            .Modulators.Modulators[0];

        //Assert
        modulator.ChannelFollowsVoice.Should().BeFalse();
        modulator.Channel.Should().Be(3);
    }

    [Fact]
    public void note_sequences_and_their_notes_parse()
    {
        //Arrange
        //Act
        var sequence = Full().NoteSequences.Sequences[0];

        //Assert
        sequence.Name.Should().Be("Maj1Slow");
        sequence.Length.Should().Be(768.0);
        sequence.Rate.Should().Be(96.0);
        sequence.Notes.Should().HaveCount(2);
        sequence.Notes[1].Position.Should().Be(11.0);
        sequence.Notes[1].Velocity.Should().Be(0.8);
        sequence.Notes[1].Note.Should().Be(52);
        sequence.Notes[1].Length.Should().Be(757.0);
    }

    [Fact]
    public void the_arpeggiator_reads_every_attribute()
    {
        //Arrange
        //Act
        var arpeggiator = Full().Arpeggiator;

        //Assert
        arpeggiator.Enabled.Should().BeTrue();
        arpeggiator.Order.Should().Be(DecentSamplerArpOrder.UpDownInclusive);
        arpeggiator.OctaveRange.Should().Be(2);
        arpeggiator.OctaveMode.Should().Be(DecentSamplerArpOctaveMode.InterleaveByPitch);
        arpeggiator.StepCount.Should().Be(8);
        arpeggiator.GateLength.Should().Be(0.6);
        arpeggiator.FollowGlobalTempo.Should().BeFalse();
        arpeggiator.SyncDivision.Should().Be(DecentSamplerSyncDivision.NoteOneEighthDotted);
        arpeggiator.RateMultiplier.Should().Be(1.5);
        arpeggiator.OverrideBpm.Should().Be(90.0);
    }

    [Fact]
    public void tags_carry_their_volume_pan_and_polyphony()
    {
        //Arrange
        //Act
        var tag = Full().Tags.Tags[0];

        //Assert
        tag.Name.Should().Be("growl");
        tag.Enabled.Should().BeFalse();
        tag.Volume.Should().Be(0.8);
        tag.Pan.Should().Be(10.0);
        tag.Polyphony.Should().Be(12);
    }

    [Fact]
    public void the_user_interface_parses_every_element_type()
    {
        //Arrange
        //Act
        var ui = Full().Ui;

        //Assert
        ui.CoverArt.Should().Be("art.png");
        ui.BgImage.Should().Be("bg.png");
        ui.BgColor.Should().Be("FF000000");
        ui.Width.Should().Be(812.0);
        ui.Height.Should().Be(375.0);
        ui.LayoutMode.Should().Be("relative");
        ui.BgMode.Should().Be("top_left");
        ui.Tabs.Should().HaveCount(1);
        ui.Keyboard.CenterNote.Should().Be(60);
        ui.Keyboard.Colors[0].LoNote.Should().Be(36);
        ui.Keyboard.Colors[0].HiNote.Should().Be(50);
        ui.Keyboard.Colors[0].Color.Should().Be("FF2C365E");

        ui.Controls.Should().HaveCount(11);
        ui.Controls[0].Should().BeOfType<DecentSamplerUiControl>();
        ui.Controls[1].Should().BeOfType<DecentSamplerUiControl>();
        ui.Controls[2].Should().BeOfType<DecentSamplerUiButton>();
        ui.Controls[3].Should().BeOfType<DecentSamplerUiMenu>();
        ui.Controls[4].Should().BeOfType<DecentSamplerUiXyPad>();
        ui.Controls[5].Should().BeOfType<DecentSamplerUiLabel>();
        ui.Controls[6].Should().BeOfType<DecentSamplerUiImage>();
        ui.Controls[7].Should().BeOfType<DecentSamplerUiMultiFrameImage>();
        ui.Controls[8].Should().BeOfType<DecentSamplerUiRectangle>();
        ui.Controls[9].Should().BeOfType<DecentSamplerUiLine>();
        ui.Controls[10].Should().BeOfType<DecentSamplerUiOscilloscope>();
        ui.Controls[10].ControlIndex.Should().Be(10);
    }

    [Fact]
    public void a_knob_reads_every_control_attribute()
    {
        //Arrange
        //Act
        var knob = (DecentSamplerUiControl)Full().Ui.Controls[0];

        //Assert
        knob.ElementName.Should().Be("labeled-knob");
        knob.X.Should().Be(10.0);
        knob.Y.Should().Be(20.0);
        knob.Width.Should().Be(90.0);
        knob.Height.Should().Be(120.0);
        knob.Label.Should().Be("Attack");
        knob.ShowLabel.Should().BeTrue();
        knob.ParameterName.Should().Be("Attack");
        knob.Style.Should().Be(DecentSamplerControlStyle.RotaryVerticalDrag);
        knob.MinValue.Should().Be(0.0);
        knob.MaxValue.Should().Be(4.0);
        knob.Value.Should().Be(0.01);
        knob.DefaultValue.Should().Be(0.02);
        knob.ValueType.Should().Be(DecentSamplerValueType.Float);
        knob.TextColor.Should().Be("AA000000");
        knob.TextSize.Should().Be(16.0);
        knob.TrackForegroundColor.Should().Be("CC000000");
        knob.TrackBackgroundColor.Should().Be("66999999");
        knob.Tags.Should().Equal("knobs");
        knob.Visible.Should().BeTrue();
        knob.Enabled.Should().BeTrue();
        knob.DisabledOpacity.Should().Be(0.5);
        knob.Tooltip.Should().Be("Attack time");
        knob.Uid.Should().Be("abc");
        knob.SnapMode.Should().Be(DecentSamplerSnapMode.Tenths);
        knob.SnapStopPoints.Should().Equal(0.0, 0.5, 1.0);
        knob.DefeatSnapWithShift.Should().BeTrue();
        knob.CustomSkinImage.Should().Be("skin.png");
        knob.CustomSkinHoverImage.Should().Be("skin-hover.png");
        knob.CustomSkinNumFrames.Should().Be(31);
        knob.CustomSkinImageOrientation.Should().Be(DecentSamplerImageOrientation.Horizontal);
        knob.MouseDragSensitivity.Should().Be(100);
        knob.Bindings.Should().HaveCount(1);
    }

    [Fact]
    public void the_older_type_attribute_is_read_as_the_value_type()
    {
        //Arrange
        var xml = "<DecentSampler><ui><tab><labeled-knob type=\"percent\" /></tab></ui></DecentSampler>";

        //Act
        var control = (DecentSamplerUiControl)DecentSamplerParser.ParseText(xml).Ui.Controls[0];

        //Assert
        control.ValueType.Should().Be(DecentSamplerValueType.Percent);
        control.ValueTypeName.Should().Be("percent");
    }

    [Fact]
    public void a_binding_reads_every_appendix_b_attribute()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <midi>
                <note note="11">
                  <binding type="note_sequence" level="instrument" position="1" controlIndex="2"
                           groupIndex="3" effectIndex="4" modulatorIndex="5" busIndex="6"
                           stateIndex="7" bindingIndex="8" midiElementIndex="9" colorIndex="10"
                           seqIndex="11" tags="t1,t2" groupTags="g1" sampleTags="s1"
                           oscillatorTags="o1" effectTags="e1" modulatorTags="m1" controlTags="c1"
                           enabled="true" identifier="ident" parameter="SEQ_INDEX"
                           translation="table" translationOutputMin="0" translationOutputMax="1"
                           translationReversed="true" translationTable="0,0;1,2"
                           translationValue="7" triggerOnLoad="false" modBehavior="multiply"
                           modAmount="0.25" seqFollowGlobalTempo="false" seqTriggerBehavior="on"
                           seqPlayerIdentifier="player1" seqTrackMidiInputVelocity="1.0"
                           seqTranspose="-12" seqTransposeWithRootNote="60" seqPlaybackRate="2"
                           seqLoopMode="random_no_repeat" />
                </note>
              </midi>
            </DecentSampler>
            """;

        //Act
        var preset = DecentSamplerParser.ParseText(xml);
        var binding = preset.Midi.NoteHandlers[0].Bindings[0];

        //Assert
        preset.Problems.Should().BeEmpty();
        binding.BindingType.Should().Be(DecentSamplerBindingType.NoteSequence);
        binding.Level.Should().Be(DecentSamplerBindingLevel.Instrument);
        binding.Position.Should().Be(1);
        binding.ControlIndex.Should().Be(2);
        binding.GroupIndex.Should().Be(3);
        binding.EffectIndex.Should().Be(4);
        binding.ModulatorIndex.Should().Be(5);
        binding.BusIndex.Should().Be(6);
        binding.StateIndex.Should().Be(7);
        binding.BindingIndex.Should().Be(8);
        binding.MidiElementIndex.Should().Be(9);
        binding.ColorIndex.Should().Be(10);
        binding.SeqIndex.Should().Be(11);
        binding.Tags.Should().Equal("t1", "t2");
        binding.GroupTags.Should().Equal("g1");
        binding.SampleTags.Should().Equal("s1");
        binding.OscillatorTags.Should().Equal("o1");
        binding.EffectTags.Should().Equal("e1");
        binding.ModulatorTags.Should().Equal("m1");
        binding.ControlTags.Should().Equal("c1");
        binding.Enabled.Should().BeTrue();
        binding.Identifier.Should().Be("ident");
        binding.Parameter.Should().Be("SEQ_INDEX");
        binding.Translation.Should().Be(DecentSamplerTranslation.Table);
        binding.TranslationOutputMin.Should().Be(0.0);
        binding.TranslationOutputMax.Should().Be(1.0);
        binding.TranslationReversed.Should().BeTrue();
        binding.TranslationTable.Should().HaveCount(2);
        binding.TranslationValue.Should().Be("7");
        binding.TriggerOnLoad.Should().BeFalse();
        binding.ModBehavior.Should().Be(DecentSamplerModBehavior.Multiply);
        binding.ModAmount.Should().Be(0.25);
        binding.SeqFollowGlobalTempo.Should().BeFalse();
        binding.SeqTriggerBehavior.Should().Be(DecentSamplerSeqTriggerBehavior.On);
        binding.SeqPlayerIdentifier.Should().Be("player1");
        binding.SeqTrackMidiInputVelocity.Should().Be(1.0);
        binding.SeqTranspose.Should().Be(-12.0);
        binding.SeqTransposeWithRootNote.Should().Be(60.0);
        binding.SeqPlaybackRate.Should().Be(2.0);
        binding.SeqLoopMode.Should().Be(DecentSamplerSeqLoopMode.RandomNoRepeat);
        binding.ResolvedIndex.Should().Be(2);
    }

    [Fact]
    public void the_legacy_note_index_attribute_is_read_as_the_midi_element_index()
    {
        //Arrange
        var xml = "<DecentSampler><midi><cc number=\"1\"><binding type=\"note_binding\" level=\"midi\" " +
                  "noteIndex=\"4\" parameter=\"ENABLED\" /></cc></midi></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.Problems.Should().BeEmpty();
        preset.Midi.CcHandlers[0].Bindings[0].MidiElementIndex.Should().Be(4);
    }

    [Fact]
    public void an_unknown_attribute_is_kept_and_reported_once_per_preset()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="a.wav" rootNote="60" futureThing="1" />
                  <sample path="b.wav" rootNote="62" futureThing="2" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.AllUnknownAttributes().Should().HaveCount(2);
        preset.AllUnknownAttributes().First().Name.Should().Be("futureThing");
        preset.AllUnknownAttributes().First().Value.Should().Be("1");
        preset.Problems.Should().ContainSingle();
        preset.Problems[0].Should().Contain("sample@futureThing");
    }

    [Fact]
    public void an_unknown_element_is_kept_as_raw_text_and_reported_once()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <futureElement a="1"><inner /></futureElement>
                  <futureElement a="2" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.AllUnknownElements().Should().HaveCount(2);
        preset.AllUnknownElements().First().RawText.Should().Contain("<inner");
        preset.Problems.Should().ContainSingle();
        preset.Problems[0].Should().Contain("<futureElement>");
    }

    [Fact]
    public void a_value_that_cannot_be_read_is_reported_and_the_default_is_kept()
    {
        //Arrange
        var xml = "<DecentSampler><effects><effect type=\"reverb\" damping=\"O.2\" />" +
                  "</effects></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.Effects.Effects[0].Damping.Should().BeNull();
        preset.Problems.Should().ContainSingle();
        preset.Problems[0].Should().Contain("effect@damping");
    }

    [Fact]
    public void a_binding_parameter_the_engine_does_not_know_is_reported()
    {
        //Arrange
        var xml = "<DecentSampler><midi><cc number=\"1\"><binding type=\"amp\" level=\"instrument\" " +
                  "parameter=\"FUTURE_PARAMETER\" /></cc></midi></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.Problems.Should().ContainSingle();
        preset.Problems[0].Should().Contain("FUTURE_PARAMETER");
    }

    [Fact]
    public void a_lower_case_parameter_name_is_accepted()
    {
        //Arrange
        var xml = "<DecentSampler><ui><tab><labeled-knob value=\"1\">" +
                  "<binding type=\"labeled_knob\" level=\"ui\" position=\"0\" parameter=\"value\" />" +
                  "</labeled-knob></tab></ui></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);

        //Assert
        preset.Problems.Should().BeEmpty();
        preset.Ui.Controls[0].Bindings[0].BindingType.Should().Be(DecentSamplerBindingType.LabeledKnob);
    }

    [Fact]
    public void half_a_controller_range_is_widened_and_reported()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group><sample path=\"a.wav\" rootNote=\"60\" loCC64=\"90\" />" +
                  "</group></groups></DecentSampler>";

        //Act
        var preset = DecentSamplerParser.ParseText(xml);
        var sample = preset.Groups.Groups[0].Samples[0];

        //Assert
        sample.CcFilters[64].Low.Should().Be(90);
        sample.CcFilters[64].High.Should().Be(127);
        preset.Problems.Should().ContainSingle();
        preset.Problems[0].Should().Contain("hiCC64");
    }

    [Fact]
    public void malformed_xml_is_the_only_hard_failure()
    {
        //Arrange
        var xml = "<DecentSampler><groups></DecentSampler>";

        //Act
        var act = () => DecentSamplerParser.ParseText(xml, "broken.dspreset");

        //Assert
        var exception = act.Should().Throw<DecentSamplerParseException>().Which;
        exception.Path.Should().Be("broken.dspreset");
        exception.LineNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void a_preset_parses_from_a_stream()
    {
        //Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(DecentSamplerTestPresets.MinimalPreset()));

        //Act
        var preset = DecentSamplerParser.Parse(stream, "/tmp/example.dspreset");

        //Assert
        preset.Name.Should().Be("example");
        preset.Groups.Groups.Should().ContainSingle();
    }

    [Fact]
    public void descendants_walks_the_whole_document() =>
        Full().Descendants().Count().Should().BeGreaterThan(60);
}

using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers attribute inheritance: the chain from <c>&lt;groups&gt;</c> through <c>&lt;group&gt;</c> to a
/// zone, the documented defaults where nothing is written, and the two rules that are cumulative rather
/// than nearest-wins.
/// </summary>
public class DecentSamplerZoneTests
{
    private static DecentSamplerZone ZoneOf(string xml, int group = 0, int zone = 0) =>
        DecentSamplerParser.ParseText(xml).ResolveGroups()[group].Zones[zone];

    [Fact]
    public void a_zone_with_nothing_written_takes_every_documented_default()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group><sample path=\"a.wav\" rootNote=\"60\" />" +
                  "</group></groups></DecentSampler>";

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.Volume.Should().Be(1.0);
        zone.Pan.Should().Be(0.0);
        zone.Tuning.Should().Be(0.0);
        zone.PitchKeyTrack.Should().Be(1.0);
        zone.GlideTime.Should().Be(0.0);
        zone.GlideMode.Should().Be(DecentSamplerGlideMode.Legato);
        zone.AmpVelTrack.Should().Be(1.0);
        zone.Trigger.Should().Be(DecentSamplerTrigger.Attack);
        zone.ReleaseTriggerDecay.Should().Be(0.0);
        zone.SilencingMode.Should().Be(DecentSamplerSilencingMode.Fast);
        zone.SilencingDecay.Should().Be(0.0);
        zone.SeqMode.Should().Be(DecentSamplerSeqMode.Always);
        zone.SeqLength.Should().Be(0);
        zone.SeqPosition.Should().Be(1);
        zone.LoNote.Should().Be(0);
        zone.HiNote.Should().Be(127);
        zone.LoVel.Should().Be(0);
        zone.HiVel.Should().Be(127);
        zone.PlaybackMode.Should().Be(DecentSamplerPlaybackMode.Auto);
        zone.Delay.Should().Be(0.0);
        zone.DelayUnit.Should().Be(DecentSamplerTimeUnit.Seconds);
        zone.RetriggerEnabled.Should().BeFalse();
        zone.RetriggerInterval.Should().Be(4.0);
        zone.RetriggerIntervalUnit.Should().Be(DecentSamplerTimeUnit.Beats);
        zone.Start.Should().Be(0);
        zone.End.Should().BeNull();
        zone.LoopStart.Should().BeNull();
        zone.LoopEnd.Should().BeNull();
        zone.LoopCrossfade.Should().Be(0);
        zone.LoopCrossfadeMode.Should().Be(DecentSamplerLoopCrossfadeMode.EqualPower);
        zone.LoopEnabled.Should().BeNull();
        zone.AmpEnvEnabled.Should().BeTrue();
        zone.AttackCurve.Should().Be(-100.0);
        zone.DecayCurve.Should().Be(100.0);
        zone.ReleaseCurve.Should().Be(100.0);
        zone.OutputTargets[0].Should().Be(DecentSamplerOutputTarget.MainOutput);
        zone.OutputTargets[1].Should().Be(DecentSamplerOutputTarget.NoOutput);
        zone.OutputVolumes.Should().AllSatisfy(volume => volume.Should().Be(1.0));
    }

    [Fact]
    public void a_group_value_reaches_a_zone_that_writes_none()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group glideTime="0.4" trigger="release" loVel="20" hiVel="90">
                  <sample path="a.wav" rootNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.GlideTime.Should().Be(0.4);
        zone.Trigger.Should().Be(DecentSamplerTrigger.Release);
        zone.LoVel.Should().Be(20);
        zone.HiVel.Should().Be(90);
    }

    [Fact]
    public void a_groups_value_reaches_a_zone_through_a_silent_group()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups attack="0.25" release="1.5" glideMode="off" playbackMode="disk_streaming">
                <group>
                  <sample path="a.wav" rootNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.Attack.Should().Be(0.25);
        zone.Release.Should().Be(1.5);
        zone.GlideMode.Should().Be(DecentSamplerGlideMode.Off);
        zone.PlaybackMode.Should().Be(DecentSamplerPlaybackMode.DiskStreaming);
    }

    [Fact]
    public void the_nearest_written_value_wins()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups attack="1" pan="-100">
                <group attack="2" pan="0">
                  <sample path="a.wav" rootNote="60" attack="3" pan="50" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.Attack.Should().Be(3.0);
        zone.Pan.Should().Be(50.0);
    }

    [Fact]
    public void tags_are_the_union_of_every_level()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups tags="everything">
                <group tags="strings,soft">
                  <sample path="a.wav" rootNote="60" tags="mic1,soft" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.Tags.Should().Equal("everything", "strings", "soft", "mic1");
    }

    [Fact]
    public void controller_ranges_merge_with_the_nearer_level_winning()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group loCC64="0" hiCC64="63" loCC1="10" hiCC1="20">
                  <sample path="a.wav" rootNote="60" loCC64="64" hiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.CcFilters.Should().HaveCount(2);
        zone.CcFilters[64].Low.Should().Be(64);
        zone.CcFilters[64].High.Should().Be(127);
        zone.CcFilters[1].Low.Should().Be(10);
        zone.CcFilters[1].High.Should().Be(20);
        zone.CcFilters[64].Contains(100).Should().BeTrue();
        zone.CcFilters[64].Contains(10).Should().BeFalse();
    }

    [Fact]
    public void the_group_carries_its_own_resolved_values()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups volume="0.5" globalTuning="2">
                <group volume="0.25" groupTuning="-1" output2Target="BUS_1" output2Volume="0.3">
                  <sample path="a.wav" rootNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var group = DecentSamplerParser.ParseText(xml).ResolveGroups()[0];

        //Assert
        group.Index.Should().Be(0);
        group.Enabled.Should().BeTrue();
        group.Volume.Should().Be(0.25);
        group.InstrumentVolume.Should().Be(0.5);
        group.GroupTuning.Should().Be(-1.0);
        group.GlobalTuning.Should().Be(2.0);
        group.OutputTargets[1].Should().Be(DecentSamplerOutputTarget.Bus1);
        group.OutputVolumes[1].Should().Be(0.3);
    }

    [Fact]
    public void the_legacy_group_tuning_spelling_is_read_as_the_group_tuning()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group tuning=\"3\"><sample path=\"a.wav\" rootNote=\"60\" />" +
                  "</group></groups></DecentSampler>";

        //Act
        var group = DecentSamplerParser.ParseText(xml).ResolveGroups()[0];

        //Assert
        group.GroupTuning.Should().Be(3.0);
    }

    [Fact]
    public void an_oscillator_zone_resolves_its_synthesis_parameters()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group numPartials="12" harmonicTilt="0.5" fmAlgorithm="7" fmOp2Ratio="3"
                       harmonicPartial2Level="0.6">
                  <oscillator waveform="harmonic" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zone = ZoneOf(xml);

        //Assert
        zone.Kind.Should().Be(DecentSamplerZoneKind.Oscillator);
        zone.Waveform.Should().Be(DecentSamplerWaveform.Harmonic);
        zone.NumPartials.Should().Be(12);
        zone.HarmonicTilt.Should().Be(0.5);
        zone.HarmonicPartialLevels.Should().HaveCount(64);
        zone.HarmonicPartialLevels[1].Should().Be(0.6);
        zone.HarmonicPartialLevels[0].Should().Be(0.0);
        zone.FmAlgorithm.Should().Be(7);
        zone.FmOperators.Should().HaveCount(6);
        zone.FmOperators[1].Ratio.Should().Be(3.0);
    }

    [Fact]
    public void an_fm_operator_with_nothing_written_takes_the_documented_defaults()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group><oscillator waveform=\"fm6op\" /></group>" +
                  "</groups></DecentSampler>";

        //Act
        var fmOperator = ZoneOf(xml).FmOperators[5];

        //Assert
        fmOperator.Number.Should().Be(6);
        fmOperator.Ratio.Should().Be(1.0);
        fmOperator.Detune.Should().Be(0.0);
        fmOperator.Mode.Should().Be(DecentSamplerFmOperatorMode.Ratio);
        fmOperator.FixedFrequency.Should().Be(440.0);
        fmOperator.VelocitySensitivity.Should().Be(0.0);
        fmOperator.Feedback.Should().Be(0.0);
        fmOperator.Attack.Should().Be(0.0);
        fmOperator.Decay.Should().Be(0.0);
        fmOperator.Sustain.Should().Be(1.0);
        fmOperator.Release.Should().Be(-1.0);
        fmOperator.EnvelopeType.Should().Be(DecentSamplerFmEnvelopeType.Adsr);
        fmOperator.EgRates.Should().Equal(99.0, 99.0, 0.0, 99.0);
        fmOperator.EgLevels.Should().Equal(99.0, 99.0, 99.0, 0.0);
    }

    // MEASURED, round 2 item 27: the guide's attribute table gives every fmOpNLevel a default of 1.0,
    // and that is wrong. Only operator 1 defaults to 1.0; operators 2 to 6 default to 0.0, which is
    // what makes an attribute-free fm6op zone sound a pure sine in the reference player.
    [Fact]
    public void only_the_first_fm_operators_level_defaults_to_one()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group><oscillator waveform=\"fm6op\" /></group>" +
                  "</groups></DecentSampler>";

        //Act
        var operators = ZoneOf(xml).FmOperators;

        //Assert
        operators[0].Level.Should().Be(1.0);
        operators[1].Level.Should().Be(0.0);
        operators[2].Level.Should().Be(0.0);
        operators[3].Level.Should().Be(0.0);
        operators[4].Level.Should().Be(0.0);
        operators[5].Level.Should().Be(0.0);
    }

    [Fact]
    public void a_written_fm_operator_level_still_wins_over_the_measured_default()
    {
        //Arrange
        var xml = "<DecentSampler><groups><group><oscillator waveform=\"fm6op\" fmOp3Level=\"0.4\" " +
                  "/></group></groups></DecentSampler>";

        //Act
        var operators = ZoneOf(xml).FmOperators;

        //Assert
        operators[2].Level.Should().Be(0.4);
        operators[1].Level.Should().Be(0.0);
    }

    [Fact]
    public void samples_come_before_oscillators_in_a_groups_zone_list()
    {
        //Arrange
        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <oscillator waveform="saw" />
                  <sample path="a.wav" rootNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        var zones = DecentSamplerParser.ParseText(xml).ResolveGroups()[0].Zones;

        //Assert
        zones.Select(zone => zone.Kind).Should()
            .Equal(DecentSamplerZoneKind.Sample, DecentSamplerZoneKind.Oscillator);
    }

    [Fact]
    public void a_preset_with_no_groups_resolves_to_nothing() =>
        DecentSamplerParser.ParseText("<DecentSampler />").ResolveGroups().Should().BeEmpty();
}

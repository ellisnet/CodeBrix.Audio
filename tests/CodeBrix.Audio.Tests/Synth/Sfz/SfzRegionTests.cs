using System.Linq;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers the typed region model: the SFZ specification defaults, opcode parsing per family, and the
/// merging of CC ranges and CC modulations with their curves.
/// </summary>
public class SfzRegionTests
{
    // ------------------------------------------------------------------ defaults

    [Fact]
    public void an_empty_region_carries_the_spec_defaults()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav");

        //Assert
        region.LoKey.Should().Be(0);
        region.HiKey.Should().Be(127);
        region.LoVel.Should().Be(0);
        region.HiVel.Should().Be(127);
        region.PitchKeycenter.Should().Be(60);
        region.PitchKeytrack.Should().Be(100);
        region.Tune.Should().Be(0);
        region.Transpose.Should().Be(0);
        region.BendUp.Should().Be(200);
        region.BendDown.Should().Be(-200);
        region.Volume.Should().Be(0f);
        region.Amplitude.Should().Be(100f);
        region.Pan.Should().Be(0f);
        region.AmpVeltrack.Should().Be(100f);
        region.AmpegDelay.Should().Be(0f);
        region.AmpegAttack.Should().Be(0f);
        region.AmpegHold.Should().Be(0f);
        region.AmpegDecay.Should().Be(0f);
        region.AmpegSustain.Should().Be(100f);
        region.AmpegRelease.Should().Be(0f);
        region.SeqLength.Should().Be(1);
        region.SeqPosition.Should().Be(1);
        region.LoRand.Should().Be(0f);
        region.HiRand.Should().Be(1f);
        region.Group.Should().Be(0);
        region.OffBy.Should().Be(0);
        region.OffMode.Should().Be(SfzOffMode.Fast);
        region.Trigger.Should().Be(SfzTrigger.Attack);
        region.RtDecay.Should().Be(0f);
        region.Offset.Should().Be(0);
        region.End.Should().BeNull();
        region.LoopMode.Should().BeNull();
        region.Cutoff.Should().BeNull();
        region.Resonance.Should().Be(0f);
        region.FilType.Should().Be(SfzFilterType.LowPass2P);
        region.FilKeycenter.Should().Be(60);
        region.FilKeytrack.Should().Be(0);
        region.FilVeltrack.Should().Be(0);
        region.NotePolyphony.Should().BeNull();
        region.SwLast.Should().BeNull();
        region.IsDisabled.Should().BeFalse();
    }

    // ------------------------------------------------------------------ keys and velocities

    [Fact]
    public void key_sets_the_key_range_and_keycenter_together()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav key=48");

        //Assert
        region.LoKey.Should().Be(48);
        region.HiKey.Should().Be(48);
        region.PitchKeycenter.Should().Be(48);
    }

    [Fact]
    public void explicit_bounds_win_over_key_regardless_of_order()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav lokey=40 key=48 pitch_keycenter=50");

        //Assert
        region.LoKey.Should().Be(40);
        region.HiKey.Should().Be(48);
        region.PitchKeycenter.Should().Be(50);
    }

    [Fact]
    public void note_names_are_accepted_where_keys_are()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav lokey=c4 hikey=g4 sw_last=c#3");

        //Assert
        region.LoKey.Should().Be(60);
        region.HiKey.Should().Be(67);
        region.SwLast.Should().Be(49);
    }

    [Fact]
    public void a_negative_hikey_unmaps_the_region()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav hikey=-1");

        //Assert
        region.HiKey.Should().BeLessThan(region.LoKey);
    }

    // ------------------------------------------------------------------ scope resolution

    [Fact]
    public void group_and_global_opcodes_resolve_into_the_region()
    {
        //Arrange + Act
        var file = SfzParser.ParseText("""
            <global> volume=-6 ampeg_release=0.5
            <group> lovel=64 group_label=hats
            <region> sample=a.wav hivel=100
            """);
        var section = file.Regions.First();
        var region = SfzRegion.FromResolved(section, file.Resolve(section));

        //Assert
        region.Volume.Should().Be(-6f);
        region.AmpegRelease.Should().Be(0.5f);
        region.LoVel.Should().Be(64);
        region.HiVel.Should().Be(100);
        region.GroupLabel.Should().Be("hats");
    }

    // ------------------------------------------------------------------ controller ranges and modulation

    [Fact]
    public void locc_and_hicc_merge_into_ranges_with_defaults_for_the_missing_half()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav locc64=64 hicc1=63");

        //Assert
        region.CcRanges.Should().HaveCount(2);

        var modWheel = region.CcRanges.First(r => r.CcNumber == 1);
        modWheel.Low.Should().Be(0);
        modWheel.High.Should().Be(63);

        var sustain = region.CcRanges.First(r => r.CcNumber == 64);
        sustain.Low.Should().Be(64);
        sustain.High.Should().Be(127);
    }

    [Fact]
    public void on_locc_and_on_hicc_become_trigger_ranges_not_selection_ranges()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav on_locc64=126 on_hicc64=127");

        //Assert
        region.CcRanges.Should().BeEmpty();
        region.OnCcRanges.Should().HaveCount(1);
        region.OnCcRanges[0].CcNumber.Should().Be(64);
        region.OnCcRanges[0].Low.Should().Be(126);
        region.OnCcRanges[0].High.Should().Be(127);
    }

    [Fact]
    public void cc_modulations_pick_up_their_matching_curvecc()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav volume_oncc11=-12.5 volume_curvecc11=4 pan_oncc10=37");

        //Assert
        region.VolumeCc.Should().HaveCount(1);
        region.VolumeCc[0].CcNumber.Should().Be(11);
        region.VolumeCc[0].Depth.Should().Be(-12.5f);
        region.VolumeCc[0].CurveIndex.Should().Be(4);

        region.PanCc.Should().HaveCount(1);
        region.PanCc[0].CcNumber.Should().Be(10);
        region.PanCc[0].Depth.Should().Be(37f);
        region.PanCc[0].CurveIndex.Should().Be(0);
    }

    [Fact]
    public void amplitude_cc_is_the_aria_spelling_of_amplitude_oncc()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav amplitude_cc11=100");

        //Assert
        region.AmplitudeCc.Should().HaveCount(1);
        region.AmplitudeCc[0].CcNumber.Should().Be(11);
        region.AmplitudeCc[0].Depth.Should().Be(100f);
    }

    [Fact]
    public void envelope_stage_modulations_land_on_their_stage()
    {
        //Arrange + Act
        var region = RegionFrom(
            "<region> sample=a.wav ampeg_release_oncc72=1.5 ampeg_decay_oncc73=0.4 ampeg_hold_curvecc73=2");

        //Assert
        region.AmpegReleaseCc.Should().HaveCount(1);
        region.AmpegReleaseCc[0].Depth.Should().Be(1.5f);
        region.AmpegDecayCc.Should().HaveCount(1);
        region.AmpegHoldCc.Should().BeEmpty("a curvecc with no matching depth modulates nothing");
    }

    // ------------------------------------------------------------------ velocity curve points

    [Fact]
    public void amp_velcurve_points_are_collected_by_velocity()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav amp_velcurve_64=0.5 amp_velcurve_127=1");

        //Assert
        region.AmpVelcurve.Should().HaveCount(2);
        region.AmpVelcurve[64].Should().Be(0.5f);
        region.AmpVelcurve[127].Should().Be(1f);
    }

    // ------------------------------------------------------------------ playback opcodes

    [Fact]
    public void sample_playback_opcodes_parse_to_their_typed_forms()
    {
        //Arrange + Act
        var region = RegionFrom("""
            <region> sample=Samples\kick.wav offset=100 end=5000 loop_mode=loop_sustain
            loop_start=200 loop_end=4000 tune=-25 transpose=2 pitch_keytrack=0
            """);

        //Assert
        region.Sample.Should().Be(@"Samples\kick.wav");
        region.Offset.Should().Be(100);
        region.End.Should().Be(5000);
        region.LoopMode.Should().Be(SfzLoopMode.Sustain);
        region.LoopStart.Should().Be(200);
        region.LoopEnd.Should().Be(4000);
        region.Tune.Should().Be(-25);
        region.Transpose.Should().Be(2);
        region.PitchKeytrack.Should().Be(0);
    }

    [Fact]
    public void end_of_minus_one_disables_the_region()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav end=-1");

        //Assert
        region.IsDisabled.Should().BeTrue();
    }

    // ------------------------------------------------------------------ articulation opcodes

    [Fact]
    public void keyswitch_trigger_and_group_opcodes_parse()
    {
        //Arrange + Act
        var region = RegionFrom("""
            <region> sample=a.wav sw_lokey=24 sw_hikey=35 sw_last=26 sw_default=24 sw_down=30 sw_up=31
            sw_previous=60 trigger=release rt_decay=6 group=1 off_by=2 off_mode=normal note_polyphony=2
            seq_length=4 seq_position=3 lorand=0.25 hirand=0.75
            """);

        //Assert
        region.SwLoKey.Should().Be(24);
        region.SwHiKey.Should().Be(35);
        region.SwLast.Should().Be(26);
        region.SwDefault.Should().Be(24);
        region.SwDown.Should().Be(30);
        region.SwUp.Should().Be(31);
        region.SwPrevious.Should().Be(60);
        region.Trigger.Should().Be(SfzTrigger.Release);
        region.RtDecay.Should().Be(6f);
        region.Group.Should().Be(1);
        region.OffBy.Should().Be(2);
        region.OffMode.Should().Be(SfzOffMode.Normal);
        region.NotePolyphony.Should().Be(2);
        region.SeqLength.Should().Be(4);
        region.SeqPosition.Should().Be(3);
        region.LoRand.Should().Be(0.25f);
        region.HiRand.Should().Be(0.75f);
    }

    [Fact]
    public void filter_opcodes_parse()
    {
        //Arrange + Act
        var region = RegionFrom("""
            <region> sample=a.wav cutoff=1200 resonance=3 fil_type=hpf_1p fil_keytrack=100
            fil_keycenter=48 fil_veltrack=2400 cutoff_oncc74=9600 cutoff_curvecc74=1
            """);

        //Assert
        region.Cutoff.Should().Be(1200f);
        region.Resonance.Should().Be(3f);
        region.FilType.Should().Be(SfzFilterType.HighPass1P);
        region.FilKeytrack.Should().Be(100);
        region.FilKeycenter.Should().Be(48);
        region.FilVeltrack.Should().Be(2400);
        region.CutoffCc.Should().HaveCount(1);
        region.CutoffCc[0].Depth.Should().Be(9600f);
        region.CutoffCc[0].CurveIndex.Should().Be(1);
    }

    [Fact]
    public void unknown_trigger_and_filter_values_fall_back_to_defaults()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav trigger=banana fil_type=zpf_9p off_mode=weird");

        //Assert
        region.Trigger.Should().Be(SfzTrigger.Attack);
        region.FilType.Should().Be(SfzFilterType.LowPass2P);
        region.OffMode.Should().Be(SfzOffMode.Fast);
    }

    [Fact]
    public void labels_are_carried()
    {
        //Arrange + Act
        var region = RegionFrom("<region> sample=a.wav region_label=Kick sw_label=Legato");

        //Assert
        region.RegionLabel.Should().Be("Kick");
        region.SwLabel.Should().Be("Legato");
    }

    private static SfzRegion RegionFrom(string text)
    {
        var file = SfzParser.ParseText(text);
        var section = file.Regions.First();
        return SfzRegion.FromResolved(section, file.Resolve(section));
    }
}

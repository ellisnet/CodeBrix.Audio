using System.Linq;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers the typed model behind the opcode tail: the block-index canonical folding, the family
/// collector's assembly of EQ bands, LFOs (v1 and v2), envelopes and variators, and the flat model
/// extensions - crossfade ranges, scope sums, chokes, the second filter and the ampeg extras.
/// </summary>
public class SfzExtendedModelTests
{
    // ------------------------------------------------------------------ canonical folding

    [Theory]
    [InlineData("lfo01_freq", "0.5", "lfoN_freq")]
    [InlineData("lfo3_freq", "0.5", "lfoN_freq")]
    [InlineData("lfo01_wave2", "1", "lfoN_wave_N")]
    [InlineData("eg06_time0", "0.02", "egN_time_N")]
    [InlineData("eg06_pitch_oncc140", "100", "egN_pitch_onccN")]
    [InlineData("eq2_gain_oncc77", "-18", "eqN_gain_onccN")]
    [InlineData("eq1_bw", "2", "eqN_bw")]
    [InlineData("var01_oncc131", "1", "varN_onccN")]
    [InlineData("var01_eq1gain", "9", "varN_eqNgain")]
    [InlineData("lfo3_freq_lfo1_oncc118", "0.6", "lfoN_freq_lfoN_onccN")]
    [InlineData("cutoff2", "400", "cutoff2")]
    [InlineData("resonance2", "6", "resonance2")]
    [InlineData("cutoff2_cc1", "5000", "cutoff2_ccN")]
    [InlineData("fileg_depthcc119", "7900", "fileg_depth_ccN")]
    [InlineData("amplitude_smoothcc4", "150", "amplitude_smoothccN")]
    [InlineData("ampeg_attack", "0.1", "ampeg_attack")]
    public void block_indices_fold_to_canonical_names(string name, string value, string expected)
    {
        //Arrange
        var opcode = new SfzOpcode(name, value, 1);

        //Act & Assert
        SfzSupportedOpcodes.CanonicalNameOf(opcode).Should().Be(expected);
    }

    // ------------------------------------------------------------------ family assembly

    [Fact]
    public void eq_bands_assemble_with_their_band_defaults()
    {
        //Arrange & Act
        var region = ParseRegion("eq2_gain=-6 eq2_gain_oncc77=-12 eq3_freq=4000 eq3_bw=2 eq3_gain=3");

        //Assert - band 2 keeps its default 500 Hz center and one-octave width.
        region.EqBands.Should().HaveCount(2);
        var band2 = region.EqBands[0];
        band2.Number.Should().Be(2);
        band2.Frequency.Should().Be(500f);
        band2.Bandwidth.Should().Be(1f);
        band2.Gain.Should().Be(-6f);
        band2.GainCc.Should().HaveCount(1);
        band2.GainCc[0].CcNumber.Should().Be(77);
        band2.GainCc[0].Depth.Should().Be(-12f);

        var band3 = region.EqBands[1];
        band3.Frequency.Should().Be(4000f);
        band3.Bandwidth.Should().Be(2f);
        band3.Gain.Should().Be(3f);
    }

    [Fact]
    public void a_v2_lfo_assembles_with_subs_and_cross_modulation()
    {
        //Arrange & Act - lfo3 and lfo03 are the same block, whichever spelling a file mixes.
        var region = ParseRegion("""
            lfo01_wave=1 lfo01_freq=4.5 lfo01_phase=0.25 lfo01_pitch=8
            lfo01_wave2=1 lfo01_ratio2=0.48 lfo01_scale2=0.2 lfo01_offset2=0.1
            lfo3_wave=-1 lfo03_freq=8 lfo3_volume=6 lfo3_freq_lfo1_oncc118=0.6
            """);

        //Assert
        region.Lfos.Should().HaveCount(2);

        var vibrato = region.Lfos[0];
        vibrato.Number.Should().Be(1);
        vibrato.Wave.Should().Be(SfzLfoWave.Sine);
        vibrato.Frequency.Should().Be(4.5f);
        vibrato.Phase.Should().Be(0.25f);
        vibrato.Pitch.Should().Be(8f);
        vibrato.Subs.Should().HaveCount(1);
        vibrato.Subs[0].Ratio.Should().Be(0.48f);
        vibrato.Subs[0].Scale.Should().Be(0.2f);
        vibrato.Subs[0].Offset.Should().Be(0.1f);

        var random = region.Lfos[1];
        random.Number.Should().Be(3);
        random.Wave.Should().Be(SfzLfoWave.RandomSampleHold);
        random.Frequency.Should().Be(8f);
        random.Volume.Should().Be(6f);
        random.FrequencyLfoModulations.Should().HaveCount(1);
        random.FrequencyLfoModulations[0].SourceNumber.Should().Be(1);
        random.FrequencyLfoModulations[0].DepthCc.Should().HaveCount(1);
        random.FrequencyLfoModulations[0].DepthCc[0].CcNumber.Should().Be(118);
    }

    [Fact]
    public void v1_lfo_blocks_translate_into_the_same_model()
    {
        //Arrange & Act
        var region = ParseRegion(
            "amplfo_freq=0.3 amplfo_depth=1.5 amplfo_delay=1.6 amplfo_fade=0.6 pitchlfo_depth_oncc1=50");

        //Assert - the translated blocks are sine, numbered 0.
        region.Lfos.Should().HaveCount(2);

        var tremolo = region.Lfos[0];
        tremolo.Number.Should().Be(0);
        tremolo.Wave.Should().Be(SfzLfoWave.Sine);
        tremolo.Frequency.Should().Be(0.3f);
        tremolo.Volume.Should().Be(1.5f);
        tremolo.Delay.Should().Be(1.6f);
        tremolo.Fade.Should().Be(0.6f);

        var vibrato = region.Lfos[1];
        vibrato.PitchCc.Should().HaveCount(1);
        vibrato.PitchCc[0].CcNumber.Should().Be(1);
        vibrato.PitchCc[0].Depth.Should().Be(50f);
    }

    [Fact]
    public void mod_envelopes_and_flex_egs_assemble()
    {
        //Arrange & Act
        var region = ParseRegion("""
            fileg_attack=0.1 fileg_depth=1200 fileg_vel2depth=600 fileg_attackcc120=0.5
            pitcheg_hold=4.4 pitcheg_depth_oncc119=7900
            eg06_level0=-1 eg06_time0=0.02 eg06_level1=0 eg06_time1=0.07 eg06_sustain=1 eg06_pitch_oncc140=100
            """);

        //Assert
        region.FilEg.Attack.Should().Be(0.1f);
        region.FilEg.Depth.Should().Be(1200f);
        region.FilEg.Vel2Depth.Should().Be(600f);
        region.FilEg.AttackCc.Single().CcNumber.Should().Be(120);

        region.PitchEg.Hold.Should().Be(4.4f);
        region.PitchEg.DepthCc.Single().Depth.Should().Be(7900f);

        var flex = region.FlexEgs.Single();
        flex.Number.Should().Be(6);
        flex.Times.Should().Equal(0.02f, 0.07f);
        flex.Levels.Should().Equal(-1f, 0f);
        flex.SustainPoint.Should().Be(1);
        flex.PitchCc.Single().CcNumber.Should().Be(140);
    }

    [Fact]
    public void variators_assemble_with_inputs_and_targets()
    {
        //Arrange & Act
        var region = ParseRegion("""
            var01_mod=mult var01_oncc121=1 var01_oncc131=1 var01_curvecc121=9
            var01_eq1gain=9 var01_eq1freq=1000 var01_cutoff=5000
            """);

        //Assert
        var variator = region.Variators.Single();
        variator.Number.Should().Be(1);
        variator.Multiply.Should().BeTrue();
        variator.Inputs.Should().HaveCount(2);
        variator.Inputs[0].CcNumber.Should().Be(121);
        variator.Inputs[0].CurveIndex.Should().Be(9);
        variator.Inputs[1].CcNumber.Should().Be(131);
        variator.Cutoff.Should().Be(5000f);
        variator.EqGain[0].Should().Be(9f);
        variator.EqFrequency[0].Should().Be(1000f);
    }

    // ------------------------------------------------------------------ flat model extensions

    [Fact]
    public void the_flat_extensions_parse_into_the_typed_region()
    {
        //Arrange & Act
        var region = ParseRegion("""
            delay=0.1 delay_random=0.04 offset_random=10000 amp_random=3 fil_random=150
            pitch_veltrack=8 amp_keytrack=-0.15 amp_keycenter=c2 width=50 width_oncc20=25
            group_volume=-20 master_volume=-5 group_tune=-10 loprog=2 hiprog=4
            sw_lolast=24 sw_hilast=27 sw_vel=previous sustain_cc=90
            off_time=0.5 off_shape=-3 off_mode=time polyphony=3
            xfin_lovel=25 xfin_hivel=61 xfout_locc1=60 xfout_hicc1=120 xf_cccurve=gain
            ampeg_vel2attack=-0.4 ampeg_vel2decay=10 ampeg_attack_shape=5.2 ampeg_dynamic=1
            cutoff2=400 resonance2=6 fil2_type=hpf_1p fil2_keytrack=100 resonance_cc116=24 cutoff2_cc1=5000
            amplitude_oncc4=100 amplitude_smoothcc4=150
            """);

        //Assert
        region.Delay.Should().Be(0.1f);
        region.DelayRandom.Should().Be(0.04f);
        region.OffsetRandom.Should().Be(10000L);
        region.AmpRandom.Should().Be(3f);
        region.FilRandom.Should().Be(150f);
        region.PitchVeltrack.Should().Be(8);
        region.AmpKeytrack.Should().Be(-0.15f);
        region.AmpKeycenter.Should().Be(36);
        region.Width.Should().Be(50f);
        region.WidthCc.Single().Depth.Should().Be(25f);
        region.ScopeVolume.Should().Be(-25f);
        region.ScopeTune.Should().Be(-10f);
        region.LoProg.Should().Be(2);
        region.HiProg.Should().Be(4);
        region.SwLoLast.Should().Be(24);
        region.SwHiLast.Should().Be(27);
        region.SwVelPrevious.Should().BeTrue();
        region.SustainCc.Should().Be(90);
        region.OffTime.Should().Be(0.5f);
        region.OffShape.Should().Be(-3f);
        region.OffMode.Should().Be(SfzOffMode.Time);
        region.Polyphony.Should().Be(3);
        region.XfInLoVel.Should().Be(25);
        region.XfInHiVel.Should().Be(61);
        region.XfOutCcRanges.Single().CcNumber.Should().Be(1);
        region.XfOutCcRanges.Single().Low.Should().Be(60);
        region.XfOutCcRanges.Single().High.Should().Be(120);
        region.XfCcCurve.Should().Be(SfzXfCurve.Gain);
        region.XfVelCurve.Should().Be(SfzXfCurve.Power);
        region.AmpegVel2Attack.Should().Be(-0.4f);
        region.AmpegVel2Decay.Should().Be(10f);
        region.AmpegAttackShape.Should().Be(5.2f);
        region.AmpegDynamic.Should().BeTrue();
        region.Cutoff2.Should().Be(400f);
        region.Resonance2.Should().Be(6f);
        region.Fil2Type.Should().Be(SfzFilterType.HighPass1P);
        region.Fil2Keytrack.Should().Be(100);
        region.ResonanceCc.Single().Depth.Should().Be(24f);
        region.Cutoff2Cc.Single().Depth.Should().Be(5000f);
        region.AmplitudeCc.Single().SmoothMilliseconds.Should().Be(150f);
    }

    [Fact]
    public void gain_cc_merges_into_the_volume_modulations()
    {
        //Arrange & Act
        var region = ParseRegion("gain_cc1=20 volume_oncc7=-6");

        //Assert
        region.VolumeCc.Should().HaveCount(2);
        region.VolumeCc[0].CcNumber.Should().Be(1);
        region.VolumeCc[0].Depth.Should().Be(20f);
        region.VolumeCc[1].CcNumber.Should().Be(7);
    }

    // ------------------------------------------------------------------ helpers

    private static SfzRegion ParseRegion(string opcodes)
    {
        var file = SfzParser.ParseText("<region> sample=irrelevant.wav " + opcodes.Replace('\n', ' '));
        var section = file.Regions.Single();
        return SfzRegion.FromResolved(section, file.Resolve(section));
    }
}

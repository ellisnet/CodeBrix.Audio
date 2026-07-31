using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One playable SFZ region with its opcodes resolved (region over group over master over global) and
/// converted to typed values, with the SFZ specification's defaults filled in for everything unset.
/// </summary>
/// <remarks>
/// <para>
/// This is the renderer's view of a region, built by <see cref="SfzInstrument"/> from the structural
/// parse (<see cref="SfzFile"/>). It carries exactly the opcode surface the CodeBrix.Audio SFZ engine
/// implements; opcodes outside that surface are left in the structural parse and reported through
/// <see cref="SfzInstrument.UnsupportedOpcodes"/> rather than dropped silently.
/// </para>
/// <para>
/// Sample-dependent defaults are resolved by the instrument, not here: when <c>loop_mode</c> is unset,
/// <see cref="LoopMode"/> stays <see langword="null"/> and the instrument chooses no-loop or continuous
/// depending on whether the sample file carries loop points (and fills <see cref="LoopStart"/> and
/// <see cref="LoopEnd"/> from the sample when the opcodes did not set them).
/// </para>
/// </remarks>
public sealed class SfzRegion
{
    private static readonly IReadOnlyList<SfzCcModulation> emptyModulations = Array.Empty<SfzCcModulation>();
    private static readonly IReadOnlyList<SfzCcRange> emptyRanges = Array.Empty<SfzCcRange>();

    private SfzRegion()
    {
        HiKey = 127;
        HiVel = 127;
        LoRand = 0f;
        HiRand = 1f;
        SeqLength = 1;
        SeqPosition = 1;
        PitchKeycenter = 60;
        PitchKeytrack = 100;
        BendUp = 200;
        BendDown = -200;
        Amplitude = 100f;
        AmpVeltrack = 100f;
        AmpegSustain = 100f;
        FilType = SfzFilterType.LowPass2P;
        FilKeycenter = 60;
        CcRanges = emptyRanges;
        OnCcRanges = emptyRanges;
        VolumeCc = emptyModulations;
        AmplitudeCc = emptyModulations;
        PanCc = emptyModulations;
        TuneCc = emptyModulations;
        CutoffCc = emptyModulations;
        AmpegDelayCc = emptyModulations;
        AmpegAttackCc = emptyModulations;
        AmpegHoldCc = emptyModulations;
        AmpegDecayCc = emptyModulations;
        AmpegSustainCc = emptyModulations;
        AmpegReleaseCc = emptyModulations;
        AmpVelcurve = new Dictionary<int, float>();

        HiProg = 127;
        SustainCc = 64;
        AmpKeycenter = 60;
        PanKeycenter = 60;
        Width = 100f;
        OffTime = 0.006f;
        XfKeyCurve = SfzXfCurve.Power;
        XfVelCurve = SfzXfCurve.Power;
        XfCcCurve = SfzXfCurve.Power;
        Fil2Type = SfzFilterType.LowPass2P;
        Fil2Keycenter = 60;
        OffsetCc = emptyModulations;
        ResonanceCc = emptyModulations;
        Cutoff2Cc = emptyModulations;
        Resonance2Cc = emptyModulations;
        WidthCc = emptyModulations;
        AmpVeltrackCc = emptyModulations;
        XfInCcRanges = emptyRanges;
        XfOutCcRanges = emptyRanges;
        EqBands = Array.Empty<SfzEqBand>();
        Lfos = Array.Empty<SfzLfo>();
        FlexEgs = Array.Empty<SfzFlexEg>();
        Variators = Array.Empty<SfzVariator>();
    }

    // ---- Sample and playback ------------------------------------------------

    /// <summary>The <c>sample</c> opcode as written, before <c>default_path</c> resolution.</summary>
    public string Sample { get; private set; }

    /// <summary>The first sample frame to play (<c>offset</c>).</summary>
    public long Offset { get; private set; }

    /// <summary>
    /// The last sample frame to play (<c>end</c>, inclusive), or <see langword="null"/> to play to the
    /// sample's end. A value of -1 disables the region entirely - see <see cref="IsDisabled"/>.
    /// </summary>
    public long? End { get; private set; }

    /// <summary>Whether <c>end=-1</c> disabled this region. A disabled region never sounds.</summary>
    public bool IsDisabled => End == -1;

    /// <summary>The loop behaviour, or <see langword="null"/> when unset (sample-dependent default).</summary>
    public SfzLoopMode? LoopMode { get; internal set; }

    /// <summary>The first frame of the loop (<c>loop_start</c>), or <see langword="null"/> when unset.</summary>
    public long? LoopStart { get; internal set; }

    /// <summary>The last frame of the loop (<c>loop_end</c>, inclusive), or <see langword="null"/> when unset.</summary>
    public long? LoopEnd { get; internal set; }

    // ---- Pitch --------------------------------------------------------------

    /// <summary>The key whose playback needs no pitch shift (<c>pitch_keycenter</c>).</summary>
    public int PitchKeycenter { get; private set; }

    /// <summary>Pitch change per key away from the keycenter, in cents (<c>pitch_keytrack</c>, default 100).</summary>
    public int PitchKeytrack { get; private set; }

    /// <summary>Fine tuning in cents (<c>tune</c>).</summary>
    public int Tune { get; private set; }

    /// <summary>Transposition in semitones (<c>transpose</c>).</summary>
    public int Transpose { get; private set; }

    /// <summary>Pitch-bend range upward in cents (<c>bend_up</c>, default 200).</summary>
    public int BendUp { get; private set; }

    /// <summary>Pitch-bend range downward in cents (<c>bend_down</c>, default -200; negative bends down).</summary>
    public int BendDown { get; private set; }

    /// <summary>CC modulations of <see cref="Tune"/>, in cents (<c>tune_onccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> TuneCc { get; private set; }

    // ---- Key, velocity and controller selection -----------------------------

    /// <summary>The lowest key that triggers the region (<c>lokey</c>).</summary>
    public int LoKey { get; private set; }

    /// <summary>The highest key that triggers the region (<c>hikey</c>).</summary>
    public int HiKey { get; private set; }

    /// <summary>The lowest velocity that triggers the region (<c>lovel</c>).</summary>
    public int LoVel { get; private set; }

    /// <summary>The highest velocity that triggers the region (<c>hivel</c>).</summary>
    public int HiVel { get; private set; }

    /// <summary>Controller ranges that must all hold for the region to sound (<c>loccN</c>/<c>hiccN</c>).</summary>
    public IReadOnlyList<SfzCcRange> CcRanges { get; private set; }

    /// <summary>
    /// Controller ranges that TRIGGER the region: it sounds when one of these controllers moves into its
    /// range (<c>on_loccN</c>/<c>on_hiccN</c>), rather than on a note-on.
    /// </summary>
    public IReadOnlyList<SfzCcRange> OnCcRanges { get; private set; }

    /// <summary>The low bound of the random layer range (<c>lorand</c>).</summary>
    public float LoRand { get; private set; }

    /// <summary>The high bound of the random layer range (<c>hirand</c>).</summary>
    public float HiRand { get; private set; }

    /// <summary>The round-robin cycle length (<c>seq_length</c>).</summary>
    public int SeqLength { get; private set; }

    /// <summary>This region's 1-based position in the round-robin cycle (<c>seq_position</c>).</summary>
    public int SeqPosition { get; private set; }

    // ---- Key switches -------------------------------------------------------

    /// <summary>The lowest key of the keyswitch range (<c>sw_lokey</c>), or <see langword="null"/> when unset.</summary>
    public int? SwLoKey { get; private set; }

    /// <summary>The highest key of the keyswitch range (<c>sw_hikey</c>), or <see langword="null"/> when unset.</summary>
    public int? SwHiKey { get; private set; }

    /// <summary>The keyswitch that must be the last one pressed for this region to sound (<c>sw_last</c>).</summary>
    public int? SwLast { get; private set; }

    /// <summary>The keyswitch selected before any has been pressed (<c>sw_default</c>).</summary>
    public int? SwDefault { get; private set; }

    /// <summary>A key that must currently be held down for this region to sound (<c>sw_down</c>).</summary>
    public int? SwDown { get; private set; }

    /// <summary>A key that must currently NOT be held down for this region to sound (<c>sw_up</c>).</summary>
    public int? SwUp { get; private set; }

    /// <summary>The key the previous note must have been for this region to sound (<c>sw_previous</c>).</summary>
    public int? SwPrevious { get; private set; }

    // ---- Trigger, groups and polyphony --------------------------------------

    /// <summary>When the region sounds (<c>trigger</c>).</summary>
    public SfzTrigger Trigger { get; private set; }

    /// <summary>
    /// For release-triggered regions, attenuation per second the note was held, in decibels
    /// (<c>rt_decay</c>).
    /// </summary>
    public float RtDecay { get; private set; }

    /// <summary>The exclusive group this region's voices belong to (<c>group</c>); 0 is no group.</summary>
    public long Group { get; private set; }

    /// <summary>The group whose new voices silence this region's voices (<c>off_by</c>); 0 is none.</summary>
    public long OffBy { get; private set; }

    /// <summary>How this region's voices are silenced when choked (<c>off_mode</c>).</summary>
    public SfzOffMode OffMode { get; private set; }

    /// <summary>
    /// The maximum number of simultaneous voices for one note of this region
    /// (<c>note_polyphony</c>), or <see langword="null"/> for no limit. The oldest voice is stolen.
    /// </summary>
    public int? NotePolyphony { get; private set; }

    // ---- Amplitude and pan --------------------------------------------------

    /// <summary>Region gain in decibels (<c>volume</c>).</summary>
    public float Volume { get; private set; }

    /// <summary>CC modulations of <see cref="Volume"/>, in additive decibels (<c>volume_onccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> VolumeCc { get; private set; }

    /// <summary>Region amplitude as a percentage of full scale (<c>amplitude</c>, default 100).</summary>
    public float Amplitude { get; private set; }

    /// <summary>
    /// CC modulations of <see cref="Amplitude"/> (<c>amplitude_onccN</c> / <c>amplitude_ccN</c>). These
    /// are multiplicative: each contributes the gain <c>depth% x curve(cc/127)</c>, so a controller at
    /// zero silences the region - the standard way SFZ libraries wire a CC volume fader.
    /// </summary>
    public IReadOnlyList<SfzCcModulation> AmplitudeCc { get; private set; }

    /// <summary>Stereo position, -100 (left) to 100 (right) (<c>pan</c>).</summary>
    public float Pan { get; private set; }

    /// <summary>CC modulations of <see cref="Pan"/>, additive (<c>pan_onccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> PanCc { get; private set; }

    /// <summary>How much velocity affects amplitude, -100 to 100 percent (<c>amp_veltrack</c>, default 100).</summary>
    public float AmpVeltrack { get; private set; }

    /// <summary>
    /// Explicit velocity-curve points (<c>amp_velcurve_N</c>): velocity to amplitude fraction (0..1).
    /// Empty when the region uses the default concave velocity response. Undefined points are
    /// interpolated linearly, with velocity 127 mapping to 1 when not stated otherwise.
    /// </summary>
    public IReadOnlyDictionary<int, float> AmpVelcurve { get; private set; }

    // ---- Amplifier envelope -------------------------------------------------

    /// <summary>Seconds before the envelope starts (<c>ampeg_delay</c>).</summary>
    public float AmpegDelay { get; private set; }

    /// <summary>Attack time in seconds, a linear amplitude ramp (<c>ampeg_attack</c>).</summary>
    public float AmpegAttack { get; private set; }

    /// <summary>Seconds at full level before the decay (<c>ampeg_hold</c>).</summary>
    public float AmpegHold { get; private set; }

    /// <summary>Decay time in seconds (<c>ampeg_decay</c>).</summary>
    public float AmpegDecay { get; private set; }

    /// <summary>Sustain level as a percentage of full amplitude (<c>ampeg_sustain</c>, default 100).</summary>
    public float AmpegSustain { get; private set; }

    /// <summary>Release time in seconds (<c>ampeg_release</c>).</summary>
    public float AmpegRelease { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegDelay"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegDelayCc { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegAttack"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegAttackCc { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegHold"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegHoldCc { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegDecay"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegDecayCc { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegSustain"/>, in percentage points, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegSustainCc { get; private set; }

    /// <summary>CC modulations of <see cref="AmpegRelease"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpegReleaseCc { get; private set; }

    // ---- Filter -------------------------------------------------------------

    /// <summary>
    /// Filter cutoff in Hz (<c>cutoff</c>), or <see langword="null"/> when the region has no filter.
    /// The filter only runs when this is set.
    /// </summary>
    public float? Cutoff { get; private set; }

    /// <summary>Filter resonance in decibels (<c>resonance</c>).</summary>
    public float Resonance { get; private set; }

    /// <summary>The filter shape (<c>fil_type</c>, default lpf_2p).</summary>
    public SfzFilterType FilType { get; private set; }

    /// <summary>Cutoff keytracking in cents per key from <see cref="FilKeycenter"/> (<c>fil_keytrack</c>).</summary>
    public int FilKeytrack { get; private set; }

    /// <summary>The center key for filter keytracking (<c>fil_keycenter</c>, default 60).</summary>
    public int FilKeycenter { get; private set; }

    /// <summary>Cutoff velocity tracking in cents at full velocity (<c>fil_veltrack</c>).</summary>
    public int FilVeltrack { get; private set; }

    /// <summary>CC modulations of the cutoff, in cents (<c>cutoff_onccN</c> / <c>cutoff_ccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> CutoffCc { get; private set; }

    // ---- Playback timing and randomization ----------------------------------

    /// <summary>Seconds the region waits before its sample starts (<c>delay</c>).</summary>
    public float Delay { get; private set; }

    /// <summary>Extra random delay, 0 up to this many seconds per voice (<c>delay_random</c>).</summary>
    public float DelayRandom { get; private set; }

    /// <summary>CC modulations of <see cref="Offset"/>, in frames, latched at note start (<c>offset_onccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> OffsetCc { get; private set; }

    /// <summary>Extra random sample offset, 0 up to this many frames per voice (<c>offset_random</c>).</summary>
    public long OffsetRandom { get; private set; }

    /// <summary>Random volume, 0 up to this many dB per voice (<c>amp_random</c>).</summary>
    public float AmpRandom { get; private set; }

    /// <summary>Random cutoff, 0 up to this many cents per voice (<c>fil_random</c>).</summary>
    public float FilRandom { get; private set; }

    // ---- Extra pitch, amplitude and pan tracking ----------------------------

    /// <summary>Pitch change in cents at full velocity (<c>pitch_veltrack</c>).</summary>
    public int PitchVeltrack { get; private set; }

    /// <summary>Volume change in dB per key away from <see cref="AmpKeycenter"/> (<c>amp_keytrack</c>).</summary>
    public float AmpKeytrack { get; private set; }

    /// <summary>The center key for <see cref="AmpKeytrack"/> (<c>amp_keycenter</c>, default 60).</summary>
    public int AmpKeycenter { get; private set; }

    /// <summary>Pan change per key away from <see cref="PanKeycenter"/> (<c>pan_keytrack</c>).</summary>
    public float PanKeytrack { get; private set; }

    /// <summary>The center key for <see cref="PanKeytrack"/> (<c>pan_keycenter</c>, default 60).</summary>
    public int PanKeycenter { get; private set; }

    /// <summary>CC modulations of <see cref="AmpVeltrack"/>, in percentage points, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AmpVeltrackCc { get; private set; }

    /// <summary>
    /// Stereo width percentage for stereo samples (<c>width</c>, default 100): 100 leaves the image,
    /// 0 collapses to mono, negative values swap the sides.
    /// </summary>
    public float Width { get; private set; }

    /// <summary>CC modulations of <see cref="Width"/>, in percentage points (<c>width_onccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> WidthCc { get; private set; }

    /// <summary>
    /// The scope-level gain in dB summed from <c>group_volume</c>, <c>master_volume</c> and
    /// <c>global_volume</c> - a separate ARIA gain stage added to <see cref="Volume"/>.
    /// </summary>
    public float ScopeVolume { get; private set; }

    /// <summary>
    /// The scope-level tuning in cents summed from <c>group_tune</c>, <c>master_tune</c> and
    /// <c>global_tune</c>, added to <see cref="Tune"/>.
    /// </summary>
    public float ScopeTune { get; private set; }

    // ---- Program, keyswitch and velocity selection extras -------------------

    /// <summary>The lowest MIDI program that selects the region (<c>loprog</c>).</summary>
    public int LoProg { get; private set; }

    /// <summary>The highest MIDI program that selects the region (<c>hiprog</c>).</summary>
    public int HiProg { get; private set; }

    /// <summary>The lowest keyswitch of a <c>sw_lolast</c>/<c>sw_hilast</c> range, or <see langword="null"/>.</summary>
    public int? SwLoLast { get; private set; }

    /// <summary>The highest keyswitch of a <c>sw_lolast</c>/<c>sw_hilast</c> range, or <see langword="null"/>.</summary>
    public int? SwHiLast { get; private set; }

    /// <summary>
    /// Whether velocity range checks test the PREVIOUS note's velocity instead of the current one
    /// (<c>sw_vel=previous</c>).
    /// </summary>
    public bool SwVelPrevious { get; private set; }

    /// <summary>The controller acting as the sustain pedal for this region (<c>sustain_cc</c>, default 64).</summary>
    public int SustainCc { get; private set; }

    // ---- Choke fades and polyphony ------------------------------------------

    /// <summary>Seconds a voice choked with <c>off_mode=time</c> fades over (<c>off_time</c>, default 0.006).</summary>
    public float OffTime { get; private set; }

    /// <summary>
    /// The curvature of that fade (<c>off_shape</c>): 0 is linear, negative drops fast then tails,
    /// positive holds then drops - the same convention as the envelope shape opcodes.
    /// </summary>
    public float OffShape { get; private set; }

    /// <summary>
    /// The maximum simultaneous voices for the region's polyphony scope - its <c>group</c> when it has
    /// one, else the region itself (<c>polyphony</c>); <see langword="null"/> for no limit. The oldest
    /// voice is stolen.
    /// </summary>
    public int? Polyphony { get; private set; }

    // ---- Crossfades ---------------------------------------------------------

    /// <summary>The velocity where the fade-in starts (<c>xfin_lovel</c>), or <see langword="null"/>.</summary>
    public int? XfInLoVel { get; private set; }

    /// <summary>The velocity where the fade-in reaches full level (<c>xfin_hivel</c>).</summary>
    public int? XfInHiVel { get; private set; }

    /// <summary>The velocity where the fade-out starts (<c>xfout_lovel</c>).</summary>
    public int? XfOutLoVel { get; private set; }

    /// <summary>The velocity where the fade-out reaches silence (<c>xfout_hivel</c>).</summary>
    public int? XfOutHiVel { get; private set; }

    /// <summary>The key where the fade-in starts (<c>xfin_lokey</c>), or <see langword="null"/>.</summary>
    public int? XfInLoKey { get; private set; }

    /// <summary>The key where the fade-in reaches full level (<c>xfin_hikey</c>).</summary>
    public int? XfInHiKey { get; private set; }

    /// <summary>The key where the fade-out starts (<c>xfout_lokey</c>).</summary>
    public int? XfOutLoKey { get; private set; }

    /// <summary>The key where the fade-out reaches silence (<c>xfout_hikey</c>).</summary>
    public int? XfOutHiKey { get; private set; }

    /// <summary>Controller fade-in ranges (<c>xfin_loccN</c>/<c>xfin_hiccN</c>): gain 0 at Low, full at High.</summary>
    public IReadOnlyList<SfzCcRange> XfInCcRanges { get; private set; }

    /// <summary>Controller fade-out ranges (<c>xfout_loccN</c>/<c>xfout_hiccN</c>): full at Low, silent at High.</summary>
    public IReadOnlyList<SfzCcRange> XfOutCcRanges { get; private set; }

    /// <summary>The gain law of the key crossfade (<c>xf_keycurve</c>, default power).</summary>
    public SfzXfCurve XfKeyCurve { get; private set; }

    /// <summary>The gain law of the velocity crossfade (<c>xf_velcurve</c>, default power).</summary>
    public SfzXfCurve XfVelCurve { get; private set; }

    /// <summary>The gain law of the controller crossfades (<c>xf_cccurve</c>, default power).</summary>
    public SfzXfCurve XfCcCurve { get; private set; }

    // ---- Amplifier envelope extras ------------------------------------------

    /// <summary>Extra delay in seconds at full velocity (<c>ampeg_vel2delay</c>).</summary>
    public float AmpegVel2Delay { get; private set; }

    /// <summary>Extra attack time in seconds at full velocity (<c>ampeg_vel2attack</c>).</summary>
    public float AmpegVel2Attack { get; private set; }

    /// <summary>Extra hold time in seconds at full velocity (<c>ampeg_vel2hold</c>).</summary>
    public float AmpegVel2Hold { get; private set; }

    /// <summary>Extra decay time in seconds at full velocity (<c>ampeg_vel2decay</c>).</summary>
    public float AmpegVel2Decay { get; private set; }

    /// <summary>Extra sustain in percentage points at full velocity (<c>ampeg_vel2sustain</c>).</summary>
    public float AmpegVel2Sustain { get; private set; }

    /// <summary>Extra release time in seconds at full velocity (<c>ampeg_vel2release</c>).</summary>
    public float AmpegVel2Release { get; private set; }

    /// <summary>
    /// The attack curvature (<c>ampeg_attack_shape</c>): 0 is linear (the SFZ default for attack),
    /// positive rises late, negative rises fast. <see langword="null"/> keeps the default.
    /// </summary>
    public float? AmpegAttackShape { get; private set; }

    /// <summary>
    /// The decay curvature (<c>ampeg_decay_shape</c>). Unset keeps this engine's exponential decay;
    /// 0 is linear, negative drops fast then tails, positive holds then drops.
    /// </summary>
    public float? AmpegDecayShape { get; private set; }

    /// <summary>The release curvature (<c>ampeg_release_shape</c>), like <see cref="AmpegDecayShape"/>.</summary>
    public float? AmpegReleaseShape { get; private set; }

    /// <summary>
    /// Whether envelope stage times and sustain follow their CC modulations while the note plays
    /// (<c>ampeg_dynamic=1</c>) instead of latching at note start.
    /// </summary>
    public bool AmpegDynamic { get; private set; }

    // ---- Filters and equalizer ----------------------------------------------

    /// <summary>CC modulations of <see cref="Resonance"/>, in dB (<c>resonance_onccN</c> / <c>resonance_ccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> ResonanceCc { get; private set; }

    /// <summary>The second filter's cutoff in Hz (<c>cutoff2</c>), or <see langword="null"/> for no second filter.</summary>
    public float? Cutoff2 { get; private set; }

    /// <summary>The second filter's resonance in dB (<c>resonance2</c>).</summary>
    public float Resonance2 { get; private set; }

    /// <summary>The second filter's shape (<c>fil2_type</c>, default lpf_2p).</summary>
    public SfzFilterType Fil2Type { get; private set; }

    /// <summary>The second filter's cutoff keytracking in cents per key (<c>fil2_keytrack</c>).</summary>
    public int Fil2Keytrack { get; private set; }

    /// <summary>The center key for the second filter's keytracking (<c>fil2_keycenter</c>, default 60).</summary>
    public int Fil2Keycenter { get; private set; }

    /// <summary>CC modulations of <see cref="Cutoff2"/>, in cents (<c>cutoff2_onccN</c> / <c>cutoff2_ccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> Cutoff2Cc { get; private set; }

    /// <summary>CC modulations of <see cref="Resonance2"/>, in dB (<c>resonance2_ccN</c>).</summary>
    public IReadOnlyList<SfzCcModulation> Resonance2Cc { get; private set; }

    /// <summary>The parametric EQ bands (<c>eqN_*</c>), in band order; empty when the region has none.</summary>
    public IReadOnlyList<SfzEqBand> EqBands { get; private set; }

    // ---- Modulation envelopes, LFOs and variators ---------------------------

    /// <summary>The filter envelope (<c>fileg_*</c>), or <see langword="null"/> when the region has none.</summary>
    public SfzModEnvelope FilEg { get; private set; }

    /// <summary>The pitch envelope (<c>pitcheg_*</c>), or <see langword="null"/> when the region has none.</summary>
    public SfzModEnvelope PitchEg { get; private set; }

    /// <summary>The flexible envelopes (<c>egN_*</c>), in block order; empty when the region has none.</summary>
    public IReadOnlyList<SfzFlexEg> FlexEgs { get; private set; }

    /// <summary>
    /// The LFOs, in block order: the v2 <c>lfoN_*</c> blocks plus the v1 <c>amplfo</c>/<c>fillfo</c>/
    /// <c>pitchlfo</c> blocks translated into the same model. Empty when the region has none.
    /// </summary>
    public IReadOnlyList<SfzLfo> Lfos { get; private set; }

    /// <summary>The ARIA variators (<c>varNN_*</c>), in block order; empty when the region has none.</summary>
    public IReadOnlyList<SfzVariator> Variators { get; private set; }

    // ---- Labels -------------------------------------------------------------

    /// <summary>The region's display label (<c>region_label</c>), or <see langword="null"/>.</summary>
    public string RegionLabel { get; private set; }

    /// <summary>The display label inherited from the region's group (<c>group_label</c>), or <see langword="null"/>.</summary>
    public string GroupLabel { get; private set; }

    /// <summary>The display label of the region's keyswitch (<c>sw_label</c>), or <see langword="null"/>.</summary>
    public string SwLabel { get; private set; }

    /// <summary>The 1-based line number of the region header in its source file.</summary>
    public int LineNumber { get; private set; }

    /// <summary>
    /// The region's position within <see cref="SfzInstrument.Regions"/>. Playback state that is
    /// per-region but not per-voice (round-robin counters) is kept by the synthesizer, indexed by this.
    /// </summary>
    public int Index { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Sample ?? "<no sample>"} keys {LoKey}-{HiKey} vels {LoVel}-{HiVel}";

    /// <summary>
    /// Builds a typed region from a region section's resolved opcodes.
    /// </summary>
    /// <param name="section">The region section, for its source line number.</param>
    /// <param name="resolved">The resolved opcodes, from <see cref="SfzFile.Resolve"/>.</param>
    /// <returns>The typed region.</returns>
    internal static SfzRegion FromResolved(SfzSection section, IReadOnlyDictionary<string, SfzOpcode> resolved)
    {
        var region = new SfzRegion();
        region.LineNumber = section.LineNumber;

        // Written-order matters only within one name (the parser already kept the last write), so the
        // dictionary can be walked in any order. Modulation opcodes are collected first and merged with
        // their _curveccN and _smoothccN partners at the end; the block-structured families (LFOs,
        // flexible envelopes, EQ bands, variators, the v1 fileg/pitcheg/amplfo/fillfo/pitchlfo blocks)
        // route to the family collector instead of the flat switch.
        var ccLow = new Dictionary<int, int>();
        var ccHigh = new Dictionary<int, int>();
        var onCcLow = new Dictionary<int, int>();
        var onCcHigh = new Dictionary<int, int>();
        var xfInCcLow = new Dictionary<int, int>();
        var xfInCcHigh = new Dictionary<int, int>();
        var xfOutCcLow = new Dictionary<int, int>();
        var xfOutCcHigh = new Dictionary<int, int>();
        var depths = new Dictionary<(string Target, int Cc), float>();
        var curves = new Dictionary<(string Target, int Cc), int>();
        var smooths = new Dictionary<(string Target, int Cc), float>();
        var velcurve = new Dictionary<int, float>();
        var families = new SfzFamilyCollector();

        var explicitLoKey = false;
        var explicitHiKey = false;
        var explicitKeycenter = false;
        var scopeVolume = 0f;
        var scopeTune = 0f;

        foreach (var opcode in resolved.Values)
        {
            if (SfzFamilyCollector.IsFamilyName(opcode.BaseName))
            {
                families.Route(opcode);
                continue;
            }

            var index = opcode.Index.GetValueOrDefault(-1);

            switch (opcode.Modulation)
            {
                case "oncc":
                case "cc" when !IsRangeBase(opcode.BaseName):
                    if (index >= 0)
                    {
                        depths[(opcode.BaseName, index)] = opcode.AsFloat();
                    }
                    continue;

                case "curvecc":
                    if (index >= 0)
                    {
                        curves[(opcode.BaseName, index)] = opcode.AsInt();
                    }
                    continue;

                case "smoothcc":
                    if (index >= 0)
                    {
                        smooths[(opcode.BaseName, index)] = Math.Max(0f, opcode.AsFloat());
                    }
                    continue;

                case "cc": // locc / hicc / on_locc / on_hicc / xfin_locc / ... range tests
                    if (index < 0)
                    {
                        continue;
                    }

                    switch (opcode.BaseName)
                    {
                        case "locc": ccLow[index] = opcode.AsInt(); break;
                        case "hicc": ccHigh[index] = opcode.AsInt(127); break;
                        case "on_locc": onCcLow[index] = opcode.AsInt(); break;
                        case "on_hicc": onCcHigh[index] = opcode.AsInt(127); break;
                        case "xfin_locc": xfInCcLow[index] = opcode.AsInt(); break;
                        case "xfin_hicc": xfInCcHigh[index] = opcode.AsInt(127); break;
                        case "xfout_locc": xfOutCcLow[index] = opcode.AsInt(); break;
                        case "xfout_hicc": xfOutCcHigh[index] = opcode.AsInt(127); break;
                    }
                    continue;
            }

            if (opcode.BaseName == "amp_velcurve" && index >= 0)
            {
                velcurve[Math.Clamp(index, 0, 127)] = opcode.AsFloat();
                continue;
            }

            switch (opcode.Name)
            {
                case "sample": region.Sample = opcode.Value; break;
                case "offset": region.Offset = Math.Max(0, AsLong(opcode)); break;
                case "end": region.End = AsLong(opcode); break;
                case "loop_mode": region.LoopMode = ParseLoopMode(opcode.Value); break;
                case "loopmode": region.LoopMode = ParseLoopMode(opcode.Value); break;
                case "loop_start": region.LoopStart = Math.Max(0, AsLong(opcode)); break;
                case "loopstart": region.LoopStart = Math.Max(0, AsLong(opcode)); break;
                case "loop_end": region.LoopEnd = Math.Max(0, AsLong(opcode)); break;
                case "loopend": region.LoopEnd = Math.Max(0, AsLong(opcode)); break;

                case "pitch_keycenter":
                    region.PitchKeycenter = opcode.AsNoteNumber(60);
                    explicitKeycenter = true;
                    break;
                case "pitch_keytrack": region.PitchKeytrack = opcode.AsInt(100); break;
                case "tune": region.Tune = opcode.AsInt(); break;
                case "pitch": region.Tune = opcode.AsInt(); break; // ARIA alias of tune
                case "transpose": region.Transpose = opcode.AsInt(); break;
                case "bend_up": region.BendUp = opcode.AsInt(200); break;
                case "bendup": region.BendUp = opcode.AsInt(200); break;
                case "bend_down": region.BendDown = opcode.AsInt(-200); break;
                case "benddown": region.BendDown = opcode.AsInt(-200); break;

                case "lokey":
                    region.LoKey = opcode.AsNoteNumber(0);
                    explicitLoKey = true;
                    break;
                case "hikey":
                    region.HiKey = opcode.AsNoteNumber(127);
                    explicitHiKey = true;
                    break;
                case "key":
                {
                    var key = opcode.AsNoteNumber(60);
                    if (!explicitLoKey)
                    {
                        region.LoKey = key;
                    }
                    if (!explicitHiKey)
                    {
                        region.HiKey = key;
                    }
                    if (!explicitKeycenter)
                    {
                        region.PitchKeycenter = key;
                    }
                    break;
                }
                case "lovel": region.LoVel = opcode.AsInt(); break;
                case "hivel": region.HiVel = opcode.AsInt(127); break;

                case "lorand": region.LoRand = opcode.AsFloat(); break;
                case "hirand": region.HiRand = opcode.AsFloat(1f); break;
                case "seq_length": region.SeqLength = Math.Max(1, opcode.AsInt(1)); break;
                case "seq_position": region.SeqPosition = Math.Max(1, opcode.AsInt(1)); break;

                case "sw_lokey": region.SwLoKey = AsKeyOrNull(opcode); break;
                case "sw_hikey": region.SwHiKey = AsKeyOrNull(opcode); break;
                case "sw_last": region.SwLast = AsKeyOrNull(opcode); break;
                case "sw_default": region.SwDefault = AsKeyOrNull(opcode); break;
                case "sw_down": region.SwDown = AsKeyOrNull(opcode); break;
                case "sw_up": region.SwUp = AsKeyOrNull(opcode); break;
                case "sw_previous": region.SwPrevious = AsKeyOrNull(opcode); break;

                case "trigger": region.Trigger = ParseTrigger(opcode.Value); break;
                case "rt_decay": region.RtDecay = Math.Max(0f, opcode.AsFloat()); break;
                case "group": region.Group = AsLong(opcode); break;
                case "off_by": region.OffBy = AsLong(opcode); break;
                case "offby": region.OffBy = AsLong(opcode); break;
                case "off_mode": region.OffMode = ParseOffMode(opcode.Value); break;
                case "note_polyphony": region.NotePolyphony = Math.Max(1, opcode.AsInt(1)); break;

                case "volume": region.Volume = opcode.AsFloat(); break;
                case "gain": region.Volume = opcode.AsFloat(); break; // ARIA alias of volume
                case "amplitude": region.Amplitude = opcode.AsFloat(100f); break;
                case "pan": region.Pan = opcode.AsFloat(); break;
                case "amp_veltrack": region.AmpVeltrack = opcode.AsFloat(100f); break;

                case "ampeg_delay": region.AmpegDelay = Math.Max(0f, opcode.AsFloat()); break;
                case "ampeg_attack": region.AmpegAttack = Math.Max(0f, opcode.AsFloat()); break;
                case "ampeg_hold": region.AmpegHold = Math.Max(0f, opcode.AsFloat()); break;
                case "ampeg_decay": region.AmpegDecay = Math.Max(0f, opcode.AsFloat()); break;
                case "ampeg_sustain": region.AmpegSustain = Math.Clamp(opcode.AsFloat(100f), 0f, 100f); break;
                case "ampeg_release": region.AmpegRelease = Math.Max(0f, opcode.AsFloat()); break;

                case "cutoff": region.Cutoff = opcode.AsFloat(); break;
                case "resonance": region.Resonance = opcode.AsFloat(); break;
                case "fil_type": region.FilType = ParseFilterType(opcode.Value); break;
                case "filtype": region.FilType = ParseFilterType(opcode.Value); break;
                case "fil_keytrack": region.FilKeytrack = opcode.AsInt(); break;
                case "fil_keycenter": region.FilKeycenter = opcode.AsNoteNumber(60); break;
                case "fil_veltrack": region.FilVeltrack = opcode.AsInt(); break;

                case "region_label": region.RegionLabel = opcode.Value; break;
                case "group_label": region.GroupLabel = opcode.Value; break;
                case "master_label": break; // carried by the parse; nothing to render
                case "global_label": break; // carried by the parse; nothing to render
                case "sw_label": region.SwLabel = opcode.Value; break;

                case "delay": region.Delay = Math.Max(0f, opcode.AsFloat()); break;
                case "delay_random": region.DelayRandom = Math.Max(0f, opcode.AsFloat()); break;
                case "offset_random": region.OffsetRandom = Math.Max(0, AsLong(opcode)); break;
                case "amp_random": region.AmpRandom = Math.Max(0f, opcode.AsFloat()); break;
                case "fil_random": region.FilRandom = Math.Max(0f, opcode.AsFloat()); break;

                case "pitch_veltrack": region.PitchVeltrack = opcode.AsInt(); break;
                case "amp_keytrack": region.AmpKeytrack = opcode.AsFloat(); break;
                case "amp_keycenter": region.AmpKeycenter = opcode.AsNoteNumber(60); break;
                case "pan_keytrack": region.PanKeytrack = opcode.AsFloat(); break;
                case "pan_keycenter": region.PanKeycenter = opcode.AsNoteNumber(60); break;
                case "width": region.Width = Math.Clamp(opcode.AsFloat(100f), -100f, 100f); break;

                case "group_volume":
                case "master_volume":
                case "global_volume":
                    scopeVolume += opcode.AsFloat();
                    break;

                case "group_tune":
                case "master_tune":
                case "global_tune":
                    scopeTune += opcode.AsFloat();
                    break;

                case "loprog": region.LoProg = Math.Clamp(opcode.AsInt(), 0, 127); break;
                case "hiprog": region.HiProg = Math.Clamp(opcode.AsInt(127), 0, 127); break;
                case "sw_lolast": region.SwLoLast = AsKeyOrNull(opcode); break;
                case "sw_hilast": region.SwHiLast = AsKeyOrNull(opcode); break;
                case "sw_vel": region.SwVelPrevious = opcode.Value == "previous"; break;
                case "sustain_cc": region.SustainCc = Math.Clamp(opcode.AsInt(64), 0, 127); break;

                case "off_time": region.OffTime = Math.Max(0f, opcode.AsFloat(0.006f)); break;
                case "off_shape": region.OffShape = opcode.AsFloat(); break;
                case "polyphony":
                {
                    // Text values (legato_high and friends) are note-priority schemes this engine does
                    // not implement; only a numeric limit takes effect. Zero is clamped up: a region
                    // written polyphony=0 wants a tight limit, not silence.
                    var limit = opcode.AsInt(-1);
                    if (limit >= 0)
                    {
                        region.Polyphony = Math.Max(1, limit);
                    }
                    break;
                }

                case "xfin_lovel": region.XfInLoVel = opcode.AsInt(); break;
                case "xfin_hivel": region.XfInHiVel = opcode.AsInt(); break;
                case "xfout_lovel": region.XfOutLoVel = opcode.AsInt(); break;
                case "xfout_hivel": region.XfOutHiVel = opcode.AsInt(127); break;
                case "xfin_lokey": region.XfInLoKey = AsKeyOrNull(opcode); break;
                case "xfin_hikey": region.XfInHiKey = AsKeyOrNull(opcode); break;
                case "xfout_lokey": region.XfOutLoKey = AsKeyOrNull(opcode); break;
                case "xfout_hikey": region.XfOutHiKey = AsKeyOrNull(opcode); break;
                case "xf_keycurve": region.XfKeyCurve = ParseXfCurve(opcode.Value); break;
                case "xf_velcurve": region.XfVelCurve = ParseXfCurve(opcode.Value); break;
                case "xf_cccurve": region.XfCcCurve = ParseXfCurve(opcode.Value); break;

                case "ampeg_vel2delay": region.AmpegVel2Delay = opcode.AsFloat(); break;
                case "ampeg_vel2attack": region.AmpegVel2Attack = opcode.AsFloat(); break;
                case "ampeg_vel2hold": region.AmpegVel2Hold = opcode.AsFloat(); break;
                case "ampeg_vel2decay": region.AmpegVel2Decay = opcode.AsFloat(); break;
                case "ampeg_vel2sustain": region.AmpegVel2Sustain = opcode.AsFloat(); break;
                case "ampeg_vel2release": region.AmpegVel2Release = opcode.AsFloat(); break;
                case "ampeg_attack_shape": region.AmpegAttackShape = opcode.AsFloat(); break;
                case "ampeg_decay_shape": region.AmpegDecayShape = opcode.AsFloat(); break;
                case "ampeg_release_shape": region.AmpegReleaseShape = opcode.AsFloat(); break;
                case "ampeg_dynamic": region.AmpegDynamic = opcode.AsInt() != 0; break;

                case "cutoff2": region.Cutoff2 = opcode.AsFloat(); break;
                case "resonance2": region.Resonance2 = opcode.AsFloat(); break;
                case "fil2_type": region.Fil2Type = ParseFilterType(opcode.Value); break;
                case "fil2_keytrack": region.Fil2Keytrack = opcode.AsInt(); break;
                case "fil2_keycenter": region.Fil2Keycenter = opcode.AsNoteNumber(60); break;
            }
        }

        region.CcRanges = MergeRanges(ccLow, ccHigh);
        region.OnCcRanges = MergeRanges(onCcLow, onCcHigh);
        region.XfInCcRanges = MergeRanges(xfInCcLow, xfInCcHigh);
        region.XfOutCcRanges = MergeRanges(xfOutCcLow, xfOutCcHigh);

        // gain_ccN is the v1 spelling of a volume modulation; both merge into one list.
        region.VolumeCc = ConcatModulations(
            MergeModulations("volume", depths, curves, smooths),
            MergeModulations("gain", depths, curves, smooths));
        region.AmplitudeCc = MergeModulations("amplitude", depths, curves, smooths);
        region.PanCc = MergeModulations("pan", depths, curves, smooths);
        region.TuneCc = MergeModulations("tune", depths, curves, smooths);
        region.CutoffCc = MergeModulations("cutoff", depths, curves, smooths);
        region.AmpegDelayCc = MergeModulations("ampeg_delay", depths, curves, smooths);
        region.AmpegAttackCc = MergeModulations("ampeg_attack", depths, curves, smooths);
        region.AmpegHoldCc = MergeModulations("ampeg_hold", depths, curves, smooths);
        region.AmpegDecayCc = MergeModulations("ampeg_decay", depths, curves, smooths);
        region.AmpegSustainCc = MergeModulations("ampeg_sustain", depths, curves, smooths);
        region.AmpegReleaseCc = MergeModulations("ampeg_release", depths, curves, smooths);
        region.OffsetCc = MergeModulations("offset", depths, curves, smooths);
        region.ResonanceCc = MergeModulations("resonance", depths, curves, smooths);
        region.Cutoff2Cc = MergeModulations("cutoff2", depths, curves, smooths);
        region.Resonance2Cc = MergeModulations("resonance2", depths, curves, smooths);
        region.WidthCc = MergeModulations("width", depths, curves, smooths);
        region.AmpVeltrackCc = MergeModulations("amp_veltrack", depths, curves, smooths);

        region.ScopeVolume = scopeVolume;
        region.ScopeTune = scopeTune;

        families.ApplyTo(region);

        if (velcurve.Count > 0)
        {
            region.AmpVelcurve = velcurve;
        }

        return region;
    }

    internal void SetModEnvelopes(SfzModEnvelope filEnvelope, SfzModEnvelope pitchEnvelope)
    {
        FilEg = filEnvelope;
        PitchEg = pitchEnvelope;
    }

    internal void SetFlexEgs(IReadOnlyList<SfzFlexEg> flexEgs) => FlexEgs = flexEgs;

    internal void SetEqBands(IReadOnlyList<SfzEqBand> eqBands) => EqBands = eqBands;

    internal void SetLfos(IReadOnlyList<SfzLfo> lfos) => Lfos = lfos;

    internal void SetVariators(IReadOnlyList<SfzVariator> variators) => Variators = variators;

    // locc/hicc/on_locc/on_hicc decompose with Modulation == "cc" but are range tests, not modulations.
    private static bool IsRangeBase(string baseName) =>
        baseName == "locc" || baseName == "hicc" || baseName == "on_locc" || baseName == "on_hicc" ||
        baseName.EndsWith("_locc", StringComparison.Ordinal) || baseName.EndsWith("_hicc", StringComparison.Ordinal);

    // A keyswitch opcode whose value parses to no key (for example sw_previous=none) is treated as
    // unset rather than as key 0, which would silently gate the region on a real key.
    private static int? AsKeyOrNull(SfzOpcode opcode)
    {
        var key = opcode.AsNoteNumber(-1);
        return 0 <= key && key <= 127 ? key : null;
    }

    private static long AsLong(SfzOpcode opcode)
    {
        if (long.TryParse(opcode.Value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        // Some libraries write sample offsets as floats; take the integer part rather than dropping it.
        if (double.TryParse(opcode.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floating))
        {
            return (long)floating;
        }

        return 0;
    }

    private static IReadOnlyList<SfzCcRange> MergeRanges(Dictionary<int, int> low, Dictionary<int, int> high)
    {
        if (low.Count == 0 && high.Count == 0)
        {
            return emptyRanges;
        }

        var result = new List<SfzCcRange>();
        foreach (var cc in low.Keys.Union(high.Keys).OrderBy(cc => cc))
        {
            var lo = low.TryGetValue(cc, out var l) ? l : 0;
            var hi = high.TryGetValue(cc, out var h) ? h : 127;
            result.Add(new SfzCcRange(cc, lo, hi));
        }

        return result;
    }

    private static IReadOnlyList<SfzCcModulation> MergeModulations(
        string target,
        Dictionary<(string Target, int Cc), float> depths,
        Dictionary<(string Target, int Cc), int> curves,
        Dictionary<(string Target, int Cc), float> smooths)
    {
        List<SfzCcModulation> result = null;

        foreach (var pair in depths)
        {
            if (pair.Key.Target != target)
            {
                continue;
            }

            var curveIndex = curves.TryGetValue(pair.Key, out var curve) ? curve : 0;
            var smooth = smooths.TryGetValue(pair.Key, out var milliseconds) ? milliseconds : 0f;
            result ??= new List<SfzCcModulation>();
            result.Add(new SfzCcModulation(pair.Key.Cc, pair.Value, curveIndex, smooth));
        }

        if (result == null)
        {
            return emptyModulations;
        }

        result.Sort((a, b) => a.CcNumber.CompareTo(b.CcNumber));
        return result;
    }

    private static IReadOnlyList<SfzCcModulation> ConcatModulations(
        IReadOnlyList<SfzCcModulation> first, IReadOnlyList<SfzCcModulation> second)
    {
        if (second.Count == 0)
        {
            return first;
        }

        if (first.Count == 0)
        {
            return second;
        }

        var result = new List<SfzCcModulation>(first.Count + second.Count);
        result.AddRange(first);
        result.AddRange(second);
        result.Sort((a, b) => a.CcNumber.CompareTo(b.CcNumber));
        return result;
    }

    private static SfzXfCurve ParseXfCurve(string value) =>
        value == "gain" ? SfzXfCurve.Gain : SfzXfCurve.Power;

    private static SfzLoopMode? ParseLoopMode(string value)
    {
        switch (value)
        {
            case "no_loop": return SfzLoopMode.NoLoop;
            case "one_shot": return SfzLoopMode.OneShot;
            case "loop_continuous": return SfzLoopMode.Continuous;
            case "loop_sustain": return SfzLoopMode.Sustain;
            default: return null;
        }
    }

    private static SfzTrigger ParseTrigger(string value)
    {
        switch (value)
        {
            case "release": return SfzTrigger.Release;
            case "release_key": return SfzTrigger.Release; // pedal-independent variant; same trigger point
            case "first": return SfzTrigger.First;
            case "legato": return SfzTrigger.Legato;
            default: return SfzTrigger.Attack;
        }
    }

    private static SfzOffMode ParseOffMode(string value)
    {
        switch (value)
        {
            case "normal": return SfzOffMode.Normal;
            case "time": return SfzOffMode.Time;
            default: return SfzOffMode.Fast;
        }
    }

    private static SfzFilterType ParseFilterType(string value)
    {
        switch (value)
        {
            case "lpf_1p": return SfzFilterType.LowPass1P;
            case "hpf_1p": return SfzFilterType.HighPass1P;
            case "lpf_2p": return SfzFilterType.LowPass2P;
            case "hpf_2p": return SfzFilterType.HighPass2P;
            case "bpf_2p": return SfzFilterType.BandPass2P;
            case "brf_2p": return SfzFilterType.BandReject2P;
            default: return SfzFilterType.LowPass2P;
        }
    }
}

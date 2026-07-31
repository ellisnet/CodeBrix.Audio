using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The set of SFZ opcodes the CodeBrix.Audio SFZ engine implements, in canonical (index-folded) form.
/// </summary>
/// <remarks>
/// <para>
/// The canonical form folds numeric indices away: <c>volume_oncc74</c> and <c>volume_oncc11</c> are both
/// the feature <c>volume_onccN</c>, <c>locc64</c> is <c>loccN</c>, <c>amp_velcurve_82</c> is
/// <c>amp_velcurve_N</c>. That is the unit an implementation supports or does not, and it is the same
/// folding the repository's <c>sfz_opcode_survey</c> tool ranks by, so coverage numbers computed against
/// this set line up with the survey's reports.
/// </para>
/// <para>
/// <see cref="SfzInstrument"/> uses this set to report - once per name, at Debug level and through
/// <see cref="SfzInstrument.UnsupportedOpcodes"/> - what a file asked for that the engine does not do.
/// An unknown opcode never stops a file from loading; it plays with what is understood.
/// </para>
/// </remarks>
public static class SfzSupportedOpcodes
{
    private static readonly HashSet<string> names = new HashSet<string>(StringComparer.Ordinal)
    {
        // Structure and sample playback
        "sample", "default_path", "offset", "end", "loop_mode", "loop_start", "loop_end",

        // Key, velocity and controller selection
        "lokey", "hikey", "key", "lovel", "hivel", "loccN", "hiccN", "on_loccN", "on_hiccN",
        "lorand", "hirand", "seq_length", "seq_position",

        // Pitch
        "pitch_keycenter", "pitch_keytrack", "tune", "transpose", "bend_up", "bend_down",
        "tune_onccN", "tune_ccN", "tune_curveccN",

        // Key switches
        "sw_lokey", "sw_hikey", "sw_last", "sw_default", "sw_down", "sw_up", "sw_previous",

        // Trigger modes, exclusive groups and polyphony
        "trigger", "rt_decay", "group", "off_by", "off_mode", "note_polyphony",

        // Amplitude, pan and velocity response
        "volume", "volume_onccN", "volume_ccN", "volume_curveccN",
        "amplitude", "amplitude_onccN", "amplitude_ccN", "amplitude_curveccN",
        "pan", "pan_onccN", "pan_ccN", "pan_curveccN",
        "amp_veltrack", "amp_velcurve_N",

        // Amplifier envelope
        "ampeg_delay", "ampeg_attack", "ampeg_hold", "ampeg_decay", "ampeg_sustain", "ampeg_release",
        "ampeg_delay_onccN", "ampeg_delay_ccN", "ampeg_delay_curveccN",
        "ampeg_attack_onccN", "ampeg_attack_ccN", "ampeg_attack_curveccN",
        "ampeg_hold_onccN", "ampeg_hold_ccN", "ampeg_hold_curveccN",
        "ampeg_decay_onccN", "ampeg_decay_ccN", "ampeg_decay_curveccN",
        "ampeg_sustain_onccN", "ampeg_sustain_ccN", "ampeg_sustain_curveccN",
        "ampeg_release_onccN", "ampeg_release_ccN", "ampeg_release_curveccN",

        // Filter
        "cutoff", "resonance", "fil_type", "fil_keytrack", "fil_keycenter", "fil_veltrack",
        "cutoff_onccN", "cutoff_ccN", "cutoff_curveccN",

        // Controller setup and curves
        "set_ccN", "set_hd_ccN", "curve_index", "v_N",

        // Labels (parsed and carried; they render nothing)
        "label_ccN", "region_label", "group_label", "master_label", "global_label", "sw_label",

        // Aliases of implemented opcodes
        "pitch", "gain", "loopmode", "loopstart", "loopend", "offby", "bendup", "benddown", "filtype",
        "gain_onccN", "gain_ccN", "gain_curveccN",

        // Playback timing and randomization
        "delay", "delay_random", "offset_onccN", "offset_ccN", "offset_curveccN", "offset_random",
        "amp_random", "fil_random",

        // Extra pitch, amplitude and pan tracking
        "pitch_veltrack", "amp_keytrack", "amp_keycenter", "pan_keytrack", "pan_keycenter",
        "amp_veltrack_onccN", "amp_veltrack_ccN", "amp_veltrack_curveccN",
        "width", "width_onccN", "width_ccN", "width_curveccN",
        "amplitude_smoothccN",

        // Scope-level gain and tuning stages
        "group_volume", "master_volume", "global_volume", "group_tune", "master_tune", "global_tune",

        // Program, keyswitch and velocity selection extras
        "loprog", "hiprog", "sw_lolast", "sw_hilast", "sw_vel", "sustain_cc",

        // Choke fades and polyphony
        "off_time", "off_shape", "polyphony",

        // Crossfades
        "xfin_lovel", "xfin_hivel", "xfout_lovel", "xfout_hivel",
        "xfin_lokey", "xfin_hikey", "xfout_lokey", "xfout_hikey",
        "xfin_loccN", "xfin_hiccN", "xfout_loccN", "xfout_hiccN",
        "xf_keycurve", "xf_velcurve", "xf_cccurve",

        // Amplifier envelope extras
        "ampeg_vel2delay", "ampeg_vel2attack", "ampeg_vel2hold", "ampeg_vel2decay",
        "ampeg_vel2sustain", "ampeg_vel2release",
        "ampeg_attack_shape", "ampeg_decay_shape", "ampeg_release_shape", "ampeg_dynamic",

        // Filter 1 resonance modulation and the second filter
        "resonance_onccN", "resonance_ccN", "resonance_curveccN",
        "cutoff2", "cutoff2_onccN", "cutoff2_ccN", "cutoff2_curveccN",
        "resonance2", "resonance2_onccN", "resonance2_ccN", "resonance2_curveccN",
        "fil2_type", "fil2_keytrack", "fil2_keycenter",

        // Equalizer bands
        "eqN_freq", "eqN_freq_onccN", "eqN_freq_ccN", "eqN_freq_curveccN",
        "eqN_bw", "eqN_bw_onccN", "eqN_bw_ccN", "eqN_bw_curveccN",
        "eqN_gain", "eqN_gain_onccN", "eqN_gain_ccN", "eqN_gain_curveccN",

        // Filter and pitch envelopes (SFZ v1 modulation envelopes)
        "fileg_delay", "fileg_delay_onccN", "fileg_delay_ccN", "fileg_delay_curveccN",
        "fileg_attack", "fileg_attack_onccN", "fileg_attack_ccN", "fileg_attack_curveccN",
        "fileg_hold", "fileg_hold_onccN", "fileg_hold_ccN", "fileg_hold_curveccN",
        "fileg_decay", "fileg_decay_onccN", "fileg_decay_ccN", "fileg_decay_curveccN",
        "fileg_sustain", "fileg_sustain_onccN", "fileg_sustain_ccN", "fileg_sustain_curveccN",
        "fileg_release", "fileg_release_onccN", "fileg_release_ccN", "fileg_release_curveccN",
        "fileg_depth", "fileg_depth_onccN", "fileg_depth_ccN", "fileg_depth_curveccN",
        "fileg_vel2depth",
        "pitcheg_delay", "pitcheg_delay_onccN", "pitcheg_delay_ccN", "pitcheg_delay_curveccN",
        "pitcheg_attack", "pitcheg_attack_onccN", "pitcheg_attack_ccN", "pitcheg_attack_curveccN",
        "pitcheg_hold", "pitcheg_hold_onccN", "pitcheg_hold_ccN", "pitcheg_hold_curveccN",
        "pitcheg_decay", "pitcheg_decay_onccN", "pitcheg_decay_ccN", "pitcheg_decay_curveccN",
        "pitcheg_sustain", "pitcheg_sustain_onccN", "pitcheg_sustain_ccN", "pitcheg_sustain_curveccN",
        "pitcheg_release", "pitcheg_release_onccN", "pitcheg_release_ccN", "pitcheg_release_curveccN",
        "pitcheg_depth", "pitcheg_depth_onccN", "pitcheg_depth_ccN", "pitcheg_depth_curveccN",
        "pitcheg_vel2depth",

        // The SFZ v1 LFO blocks
        "amplfo_delay", "amplfo_fade", "amplfo_freq", "amplfo_freq_onccN", "amplfo_freq_ccN",
        "amplfo_depth", "amplfo_depth_onccN", "amplfo_depth_ccN", "amplfo_depth_curveccN",
        "fillfo_delay", "fillfo_fade", "fillfo_freq", "fillfo_freq_onccN", "fillfo_freq_ccN",
        "fillfo_depth", "fillfo_depth_onccN", "fillfo_depth_ccN", "fillfo_depth_curveccN",
        "pitchlfo_delay", "pitchlfo_fade", "pitchlfo_freq", "pitchlfo_freq_onccN", "pitchlfo_freq_ccN",
        "pitchlfo_depth", "pitchlfo_depth_onccN", "pitchlfo_depth_ccN", "pitchlfo_depth_curveccN",

        // The SFZ v2 LFOs (block indices folded: lfo01_freq and lfo3_freq are both lfoN_freq)
        "lfoN_freq", "lfoN_freq_onccN", "lfoN_delay", "lfoN_delay_onccN",
        "lfoN_fade", "lfoN_fade_onccN", "lfoN_phase", "lfoN_wave",
        "lfoN_wave_N", "lfoN_ratio_N", "lfoN_scale_N", "lfoN_offset_N",
        "lfoN_volume", "lfoN_volume_onccN", "lfoN_pitch", "lfoN_pitch_onccN",
        "lfoN_cutoff", "lfoN_cutoff_onccN", "lfoN_pan", "lfoN_pan_onccN",
        "lfoN_eqNfreq", "lfoN_eqNfreq_onccN", "lfoN_eqNgain", "lfoN_eqNgain_onccN",
        "lfoN_freq_lfo_N", "lfoN_freq_lfoN_onccN",

        // Flexible envelopes (SFZ v2)
        "egN_time_N", "egN_level_N", "egN_sustain",
        "egN_pitch", "egN_pitch_onccN", "egN_cutoff", "egN_cutoff_onccN",
        "egN_amplitude", "egN_amplitude_onccN",

        // ARIA variators
        "varN_mod", "varN_onccN", "varN_curveccN", "varN_cutoff", "varN_eqNgain", "varN_eqNfreq",
    };

    /// <summary>Every implemented opcode, in canonical form.</summary>
    public static IReadOnlyCollection<string> CanonicalNames => names;

    /// <summary>Whether the engine implements the given opcode.</summary>
    /// <param name="opcode">The opcode to test.</param>
    /// <returns><see langword="true"/> when implemented.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opcode"/> is null.</exception>
    public static bool IsSupported(SfzOpcode opcode) => names.Contains(CanonicalNameOf(opcode));

    /// <summary>
    /// The canonical (index-folded) name of an opcode: <c>volume_oncc74</c> gives <c>volume_onccN</c>,
    /// <c>locc64</c> gives <c>loccN</c>, <c>amp_velcurve_82</c> gives <c>amp_velcurve_N</c>, and a name
    /// with no index is returned as written. Block indices fold too: <c>lfo01_freq</c> and
    /// <c>lfo3_freq</c> are both <c>lfoN_freq</c>, <c>eg06_time0</c> is <c>egN_time_N</c>,
    /// <c>eq2_gain_oncc77</c> is <c>eqN_gain_onccN</c>, <c>var01_mod</c> is <c>varN_mod</c>, and the
    /// concatenated targets fold with them (<c>lfo01_eq1gain_oncc10</c> is <c>lfoN_eqNgain_onccN</c>).
    /// The second-filter opcodes are the exception: <c>cutoff2</c> and <c>resonance2</c> name a
    /// distinct feature, not an indexed instance of the first filter, and keep their names.
    /// </summary>
    /// <param name="opcode">The opcode to canonicalise.</param>
    /// <returns>The canonical name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="opcode"/> is null.</exception>
    public static string CanonicalNameOf(SfzOpcode opcode)
    {
        if (opcode == null)
        {
            throw new ArgumentNullException(nameof(opcode));
        }

        // The second filter's opcodes end in a digit that is part of the feature name, not an index.
        if (opcode.Name == "cutoff2" || opcode.Name == "resonance2")
        {
            return opcode.Name;
        }

        return FoldBlockIndices(CanonicalSuffixFormOf(opcode));
    }

    private static string CanonicalSuffixFormOf(SfzOpcode opcode)
    {
        if (opcode.Index == null)
        {
            return opcode.Name;
        }

        if (opcode.Modulation != null)
        {
            // Range tests (locc64) keep their whole stem; modulations (volume_oncc74) re-attach the
            // modulation suffix to the base name.
            return opcode.BaseName == opcode.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')
                ? opcode.BaseName + "N"
                : opcode.BaseName + "_" + opcode.Modulation + "N";
        }

        return opcode.BaseName + "_N";
    }

    // Folds the LFO/EG/EQ/variator block number embedded in a name segment: lfo01 -> lfoN,
    // eg06 -> egN, eq3 -> eqN, var01 -> varN, and concatenated targets like eq1gain -> eqNgain.
    // Bare "eg"/"eq"/"lfo"/"var" without digits, and words merely containing them (ampeg, fileg),
    // are left alone because the digit run must directly follow the prefix.
    private static string FoldBlockIndices(string name)
    {
        if (name.IndexOfAny(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']) < 0)
        {
            return name;
        }

        var segments = name.Split('_');
        var changed = false;

        for (var i = 0; i < segments.Length; i++)
        {
            var folded = FoldSegment(segments[i]);
            if (folded != null)
            {
                segments[i] = folded;
                changed = true;
            }
        }

        return changed ? string.Join("_", segments) : name;
    }

    private static string FoldSegment(string segment)
    {
        foreach (var prefix in blockPrefixes)
        {
            if (!segment.StartsWith(prefix, StringComparison.Ordinal) || segment.Length == prefix.Length)
            {
                continue;
            }

            var digitEnd = prefix.Length;
            while (digitEnd < segment.Length && char.IsAsciiDigit(segment[digitEnd]))
            {
                digitEnd++;
            }

            if (digitEnd == prefix.Length)
            {
                continue;
            }

            var rest = segment.Substring(digitEnd);
            return prefix + "N" + rest;
        }

        return null;
    }

    private static readonly string[] blockPrefixes = ["lfo", "eq", "eg", "var"];
}

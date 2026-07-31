using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One region LFO with its targets. Covers the SFZ v2 <c>lfoN_*</c> family and, translated into the
/// same model at load, the SFZ v1 <c>amplfo_*</c>, <c>fillfo_*</c> and <c>pitchlfo_*</c> blocks.
/// </summary>
/// <remarks>
/// <para>
/// The oscillator side: <see cref="Frequency"/> Hz (CC-modulable), a start <see cref="Delay"/>, a
/// <see cref="Fade"/>-in, an initial <see cref="Phase"/>, the main <see cref="Wave"/>, optional
/// <see cref="Subs"/> (sub-waveforms at a frequency ratio, scaled and offset, summed with the main
/// wave), and <see cref="FrequencyLfoModulations"/> - other LFOs frequency-modulating this one.
/// </para>
/// <para>
/// The target side: additive depths for volume (dB), pitch (cents), cutoff (cents), pan (position),
/// and per-band EQ gain (dB) and frequency (Hz), each with CC-modulable depth. A target whose total
/// depth is zero costs nothing at render time.
/// </para>
/// </remarks>
public sealed class SfzLfo
{
    private static readonly IReadOnlyList<SfzCcModulation> emptyModulations = [];
    private static readonly IReadOnlyList<SfzLfoSub> emptySubs = [];
    private static readonly IReadOnlyList<SfzLfoEqTarget> emptyEqTargets = [];
    private static readonly IReadOnlyList<SfzLfoFrequencyModulation> emptyFrequencyModulations = [];

    internal SfzLfo(int number)
    {
        Number = number;
        Wave = SfzLfoWave.Triangle;
        FrequencyCc = emptyModulations;
        DelayCc = emptyModulations;
        FadeCc = emptyModulations;
        VolumeCc = emptyModulations;
        PitchCc = emptyModulations;
        CutoffCc = emptyModulations;
        PanCc = emptyModulations;
        Subs = emptySubs;
        EqTargets = emptyEqTargets;
        FrequencyLfoModulations = emptyFrequencyModulations;
    }

    /// <summary>The LFO number as written (<c>lfo01</c> and <c>lfo1</c> are both 1).</summary>
    public int Number { get; }

    /// <summary>The frequency in Hz (<c>lfoN_freq</c>, default 0 - static until modulated).</summary>
    public float Frequency { get; internal set; }

    /// <summary>CC modulations of <see cref="Frequency"/>, in Hz (<c>lfoN_freq_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> FrequencyCc { get; internal set; }

    /// <summary>Seconds before the LFO starts (<c>lfoN_delay</c>).</summary>
    public float Delay { get; internal set; }

    /// <summary>CC modulations of <see cref="Delay"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> DelayCc { get; internal set; }

    /// <summary>Seconds the LFO fades in over after its delay (<c>lfoN_fade</c>).</summary>
    public float Fade { get; internal set; }

    /// <summary>CC modulations of <see cref="Fade"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> FadeCc { get; internal set; }

    /// <summary>The starting phase, 0 to 1 (<c>lfoN_phase</c>).</summary>
    public float Phase { get; internal set; }

    /// <summary>The main waveform (<c>lfoN_wave</c>).</summary>
    public SfzLfoWave Wave { get; internal set; }

    /// <summary>The sub-waveforms (<c>lfoN_waveX</c>/<c>lfoN_ratioX</c>/<c>lfoN_scaleX</c>/<c>lfoN_offsetX</c>).</summary>
    public IReadOnlyList<SfzLfoSub> Subs { get; internal set; }

    /// <summary>Volume depth in dB (<c>lfoN_volume</c>; the v1 <c>amplfo_depth</c>).</summary>
    public float Volume { get; internal set; }

    /// <summary>CC modulations of the volume depth, in dB (<c>lfoN_volume_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> VolumeCc { get; internal set; }

    /// <summary>Pitch depth in cents (<c>lfoN_pitch</c>; the v1 <c>pitchlfo_depth</c>).</summary>
    public float Pitch { get; internal set; }

    /// <summary>CC modulations of the pitch depth, in cents (<c>lfoN_pitch_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> PitchCc { get; internal set; }

    /// <summary>Cutoff depth in cents (<c>lfoN_cutoff</c>; the v1 <c>fillfo_depth</c>).</summary>
    public float Cutoff { get; internal set; }

    /// <summary>CC modulations of the cutoff depth, in cents (<c>lfoN_cutoff_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> CutoffCc { get; internal set; }

    /// <summary>Pan depth, -100 to 100 (<c>lfoN_pan</c>).</summary>
    public float Pan { get; internal set; }

    /// <summary>CC modulations of the pan depth (<c>lfoN_pan_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> PanCc { get; internal set; }

    /// <summary>The EQ-band targets (<c>lfoN_eqXfreq</c>/<c>lfoN_eqXgain</c> and CC forms).</summary>
    public IReadOnlyList<SfzLfoEqTarget> EqTargets { get; internal set; }

    /// <summary>Other LFOs frequency-modulating this one (<c>lfoN_freq_lfoX</c>).</summary>
    public IReadOnlyList<SfzLfoFrequencyModulation> FrequencyLfoModulations { get; internal set; }
}

/// <summary>
/// One LFO sub-waveform: a second oscillator running at a ratio of the main frequency, scaled and
/// offset, summed into the LFO's output.
/// </summary>
public sealed class SfzLfoSub
{
    internal SfzLfoSub(int index, SfzLfoWave wave, float ratio, float scale, float offset)
    {
        Index = index;
        Wave = wave;
        Ratio = ratio;
        Scale = scale;
        Offset = offset;
    }

    /// <summary>The sub-waveform index as written (<c>lfoN_wave2</c> is 2).</summary>
    public int Index { get; }

    /// <summary>The sub-waveform's shape (<c>lfoN_waveX</c>).</summary>
    public SfzLfoWave Wave { get; }

    /// <summary>The frequency ratio against the LFO's main frequency (<c>lfoN_ratioX</c>, default 1).</summary>
    public float Ratio { get; }

    /// <summary>The amplitude relative to the main wave (<c>lfoN_scaleX</c>, default 1).</summary>
    public float Scale { get; }

    /// <summary>An offset added to the sub-waveform's output (<c>lfoN_offsetX</c>).</summary>
    public float Offset { get; }
}

/// <summary>One LFO-to-EQ-band routing: depth for the band's frequency (Hz) and gain (dB).</summary>
public sealed class SfzLfoEqTarget
{
    private static readonly IReadOnlyList<SfzCcModulation> empty = [];

    internal SfzLfoEqTarget(int band)
    {
        Band = band;
        FrequencyCc = empty;
        GainCc = empty;
    }

    /// <summary>The EQ band number targeted (<c>lfoN_eq1gain</c> targets band 1).</summary>
    public int Band { get; }

    /// <summary>Frequency depth in Hz (<c>lfoN_eqXfreq</c>).</summary>
    public float Frequency { get; internal set; }

    /// <summary>CC modulations of the frequency depth, in Hz (<c>lfoN_eqXfreq_onccY</c>).</summary>
    public IReadOnlyList<SfzCcModulation> FrequencyCc { get; internal set; }

    /// <summary>Gain depth in dB (<c>lfoN_eqXgain</c>).</summary>
    public float Gain { get; internal set; }

    /// <summary>CC modulations of the gain depth, in dB (<c>lfoN_eqXgain_onccY</c>).</summary>
    public IReadOnlyList<SfzCcModulation> GainCc { get; internal set; }
}

/// <summary>
/// One LFO frequency-modulating another (<c>lfoN_freq_lfoX</c>): the source LFO's output, scaled by
/// the depth in Hz, adds to the target LFO's frequency.
/// </summary>
public sealed class SfzLfoFrequencyModulation
{
    private static readonly IReadOnlyList<SfzCcModulation> empty = [];

    internal SfzLfoFrequencyModulation(int sourceNumber)
    {
        SourceNumber = sourceNumber;
        DepthCc = empty;
    }

    /// <summary>The number of the LFO supplying the modulation.</summary>
    public int SourceNumber { get; }

    /// <summary>The modulation depth in Hz (<c>lfoN_freq_lfoX</c>).</summary>
    public float Depth { get; internal set; }

    /// <summary>CC modulations of the depth, in Hz (<c>lfoN_freq_lfoX_onccY</c>).</summary>
    public IReadOnlyList<SfzCcModulation> DepthCc { get; internal set; }
}

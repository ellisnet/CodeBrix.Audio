using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;effect&gt;</c> element, with every parameter the developer guide documents for any
/// effect type.
/// </summary>
/// <remarks>
/// <para>
/// The format writes every effect with the same element and distinguishes them by <c>type</c>, so the
/// parameters of all types share one class. Only the ones that belong to <see cref="EffectType"/>
/// carry meaning; the rest stay <see langword="null"/>. <see cref="Values"/> holds the raw text of
/// every recognised parameter, which is what the survey tool reports on.
/// </para>
/// <para>
/// Several parameters are spelled differently by different effects even though they mean the same
/// thing - <c>wetLevel</c> on the reverb and delay, <c>mix</c> on the chorus and convolution. They are
/// kept as separate properties rather than merged, so that a binding to <c>FX_WET_LEVEL</c> and one to
/// <c>FX_MIX</c> reach exactly what the guide says they reach.
/// </para>
/// </remarks>
public sealed class DecentSamplerEffect : DecentSamplerElement
{
    private readonly Dictionary<string, string> _values = [];

    /// <summary>The effect's 0-based position in its chain, which bindings use as their index.</summary>
    public int Index { get; internal set; }

    /// <summary>The <c>type</c> attribute exactly as written, including legacy and unknown spellings.</summary>
    public string TypeName { get; internal set; }

    /// <summary>The recognised effect type, or <see cref="DecentSamplerEffectType.Unknown"/>.</summary>
    public DecentSamplerEffectType EffectType { get; internal set; } = DecentSamplerEffectType.Unknown;

    /// <summary>The tag names written in <c>tags</c>, which bindings can target instead of an index.</summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>
    /// Whether the effect processes audio. Default true. This is the target of an
    /// <c>ENABLED</c> binding, which every effect type accepts; the format has no <c>enabled</c>
    /// attribute on <c>&lt;effect&gt;</c>, so it starts on and only a binding turns it off.
    /// </summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>Every recognised parameter, keyed by its attribute name, with the text as written.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>Filter cut-off in Hz (<c>frequency</c>). Default 22000.</summary>
    public double? Frequency { get; internal set; }

    /// <summary>Filter resonance (<c>resonance</c>), 0.001 to 5. Default 0.7.</summary>
    public double? Resonance { get; internal set; }

    /// <summary>Filter Q (<c>q</c>), 0.01 to 18. Default 0.7.</summary>
    public double? Q { get; internal set; }

    /// <summary>Peak filter gain (<c>gain</c>), 0 to 1. Default 1.</summary>
    public double? Gain { get; internal set; }

    /// <summary>Gain-effect level (<c>level</c>), in the unit given by <see cref="LevelUnit"/>. Default 0.</summary>
    public double? Level { get; internal set; }

    /// <summary>The unit <see cref="Level"/> is in (<c>levelUnit</c>). Default decibels.</summary>
    public DecentSamplerGainLevelUnit? LevelUnit { get; internal set; }

    /// <summary>Reverb room size (<c>roomSize</c>), 0 to 1. Default 0.7.</summary>
    public double? RoomSize { get; internal set; }

    /// <summary>Reverb damping (<c>damping</c>), 0 to 1. Default 0.3.</summary>
    public double? Damping { get; internal set; }

    /// <summary>Reverb or delay wet level (<c>wetLevel</c>), 0 to 1. Default 0 for reverb, 0.5 for delay.</summary>
    public double? WetLevel { get; internal set; }

    /// <summary>Delay time (<c>delayTime</c>), in the format given by <see cref="DelayTimeFormat"/>. Default 0.7.</summary>
    public double? DelayTime { get; internal set; }

    /// <summary>How <see cref="DelayTime"/> is read (<c>delayTimeFormat</c>). Default seconds.</summary>
    public DecentSamplerDelayTimeFormat? DelayTimeFormat { get; internal set; }

    /// <summary>Delay or phaser feedback (<c>feedback</c>). Default 0.2 for delay, 0.7 for phaser.</summary>
    public double? Feedback { get; internal set; }

    /// <summary>Delay stereo offset in seconds (<c>stereoOffset</c>), -10 to 10. Default 0.</summary>
    public double? StereoOffset { get; internal set; }

    /// <summary>Wet/dry mix (<c>mix</c>), 0 to 1. Default 0.5, or 1 for the bit crusher and gate.</summary>
    public double? Mix { get; internal set; }

    /// <summary>Modulation depth (<c>modDepth</c>), 0 to 1. Default 0.2, or 0.3 for the stereo simulator.</summary>
    public double? ModDepth { get; internal set; }

    /// <summary>Modulation rate in Hz (<c>modRate</c>). Default 0.2, or 0.5 for the stereo simulator.</summary>
    public double? ModRate { get; internal set; }

    /// <summary>Phaser centre frequency in Hz (<c>centerFrequency</c>). Default 400.</summary>
    public double? CenterFrequency { get; internal set; }

    /// <summary>The impulse response file for the convolution effect (<c>irFile</c>), WAV or AIFF.</summary>
    public string IrFile { get; internal set; }

    /// <summary>Pitch shift in semitones (<c>pitchShift</c>), -24 to 24.</summary>
    public double? PitchShift { get; internal set; }

    /// <summary>Input drive (<c>drive</c>), 1 to 100 for the wave folder and 1 to 1000 for the wave shaper. Default 1.</summary>
    public double? Drive { get; internal set; }

    /// <summary>
    /// Wave-folder fold threshold (<c>threshold</c>, default 0.25) or compressor threshold in decibels
    /// (<c>threshold</c>, default -12). Which one it is follows <see cref="EffectType"/>.
    /// </summary>
    public double? Threshold { get; internal set; }

    /// <summary>Wave-shaper extra drive boost (<c>driveBoost</c>), 0 to 1. Default 1.</summary>
    public double? DriveBoost { get; internal set; }

    /// <summary>Wave-shaper linear output level (<c>outputLevel</c>), 0 to 1. Default 0.1.</summary>
    public double? OutputLevel { get; internal set; }

    /// <summary>Whether the wave shaper oversamples (<c>highQuality</c>). Default false.</summary>
    public bool? HighQuality { get; internal set; }

    /// <summary>The stereo simulator's algorithm (<c>algorithm</c>). Default adt.</summary>
    public DecentSamplerStereoSimulatorAlgorithm? Algorithm { get; internal set; }

    /// <summary>Stereo spread and wet amount (<c>width</c>), 0 to 1. Default 0.5.</summary>
    public double? Width { get; internal set; }

    /// <summary>Bit-crusher bit depth (<c>bitDepth</c>), 1 to 24. Default 24.</summary>
    public double? BitDepth { get; internal set; }

    /// <summary>Bit-crusher sample-rate reduction factor (<c>sampleRateReduction</c>), 1 to 32. Default 1.</summary>
    public double? SampleRateReduction { get; internal set; }

    /// <summary>The gate's probability of cutting to silence (<c>amount</c>), 0 to 1. Default 0.5.</summary>
    public double? Amount { get; internal set; }

    /// <summary>Compression ratio (<c>ratio</c>), 1 to 20. Default 4.</summary>
    public double? Ratio { get; internal set; }

    /// <summary>Compressor attack in milliseconds (<c>attack</c>), 0.1 to 200. Default 5.</summary>
    public double? Attack { get; internal set; }

    /// <summary>Compressor release in milliseconds (<c>release</c>), 5 to 2000. Default 100.</summary>
    public double? Release { get; internal set; }

    /// <summary>Compressor input gain in decibels (<c>inputGain</c>), -24 to 24. Default 0.</summary>
    public double? InputGain { get; internal set; }

    /// <summary>Compressor makeup gain in decibels (<c>outputGain</c>), -24 to 24. Default 0.</summary>
    public double? OutputGain { get; internal set; }

    /// <summary>Whether the compressor fades itself out below threshold (<c>autoBypass</c>). Default false.</summary>
    public bool? AutoBypass { get; internal set; }

    /// <summary>Filter envelope depth in Hz (<c>envelope_amount</c>), from the guide's filter examples.</summary>
    public double? EnvelopeAmount { get; internal set; }

    /// <summary>Filter envelope attack in seconds (<c>envelope_attack</c>).</summary>
    public double? EnvelopeAttack { get; internal set; }

    /// <summary>Filter envelope decay in seconds (<c>envelope_decay</c>).</summary>
    public double? EnvelopeDecay { get; internal set; }

    /// <summary>Filter envelope sustain level (<c>envelope_sustain</c>).</summary>
    public double? EnvelopeSustain { get; internal set; }

    /// <summary>Filter envelope release in seconds (<c>envelope_release</c>).</summary>
    public double? EnvelopeRelease { get; internal set; }

    internal void SetValue(string name, string text) => _values[name] = text;

    /// <inheritdoc/>
    public override string ToString() => $"<effect type=\"{TypeName}\"> (line {LineNumber})";
}

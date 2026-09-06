using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The attributes that <c>&lt;groups&gt;</c>, <c>&lt;group&gt;</c>, <c>&lt;sample&gt;</c> and
/// <c>&lt;oscillator&gt;</c> share, each unset until the preset writes it.
/// </summary>
/// <remarks>
/// <para>
/// The Decent Sampler format inherits nearly every sound attribute down the chain
/// <c>&lt;groups&gt;</c> to <c>&lt;group&gt;</c> to <c>&lt;sample&gt;</c> or
/// <c>&lt;oscillator&gt;</c>: the nearest level that writes a value wins, and where none does the
/// documented default applies. Modelling the shared set once, with everything nullable, is what makes
/// that resolution a single pass - see <see cref="DecentSamplerZone"/> for the resolved result.
/// </para>
/// <para>
/// A property being <see langword="null"/> means "not written here", never "zero".
/// </para>
/// </remarks>
public abstract class DecentSamplerSoundElement : DecentSamplerElement
{
    private DecentSamplerOutputTarget?[] _outputTargets;
    private double?[] _outputVolumes;
    private double?[] _harmonicPartialLevels;
    private DecentSamplerFmOperator[] _fmOperators;
    private Dictionary<int, DecentSamplerCcRange> _ccFilters;
    private Dictionary<int, DecentSamplerCcRange> _ccTriggers;

    /// <summary>The tag names written in <c>tags</c>. Never null; empty when none were written.</summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>The <c>volume</c> attribute as a linear multiplier, whichever way it was written.</summary>
    public double? Volume { get; internal set; }

    /// <summary>The <c>volume</c> attribute exactly as written, so <c>-3dB</c> survives a round trip.</summary>
    public string VolumeText { get; internal set; }

    /// <summary>Whether the <c>volume</c> attribute carried a <c>dB</c> suffix.</summary>
    public bool? VolumeInDecibels { get; internal set; }

    /// <summary>Stereo position, -100 (hard left) to 100 (hard right). Default 0.</summary>
    public double? Pan { get; internal set; }

    /// <summary>Fine tuning in semitones (<c>tuning</c>). Default 0.</summary>
    public double? Tuning { get; internal set; }

    /// <summary>Group-level tuning in semitones (<c>groupTuning</c>). Default 0.</summary>
    public double? GroupTuning { get; internal set; }

    /// <summary>Instrument-wide tuning in semitones (<c>globalTuning</c>). Default 0.</summary>
    public double? GlobalTuning { get; internal set; }

    /// <summary>Keyboard pitch tracking, 0 to 1 (<c>pitchKeyTrack</c>). Default 1.</summary>
    public double? PitchKeyTrack { get; internal set; }

    /// <summary>Portamento time in seconds (<c>glideTime</c>). Default 0.</summary>
    public double? GlideTime { get; internal set; }

    /// <summary>When portamento applies (<c>glideMode</c>). Default legato.</summary>
    public DecentSamplerGlideMode? GlideMode { get; internal set; }

    /// <summary>How much note velocity drives volume, 0 to 1 (<c>ampVelTrack</c>). Default 1.</summary>
    public double? AmpVelTrack { get; internal set; }

    /// <summary>What makes the zone sound (<c>trigger</c>). Default attack.</summary>
    public DecentSamplerTrigger? Trigger { get; internal set; }

    /// <summary>
    /// The release-trigger decay rate (<c>releaseTriggerDecay</c>), negative decibels per second when
    /// <see cref="ReleaseTriggerDecayInDecibels"/> is true, otherwise a linear factor per second.
    /// Default 0, meaning no decay.
    /// </summary>
    public double? ReleaseTriggerDecay { get; internal set; }

    /// <summary>Whether <see cref="ReleaseTriggerDecay"/> is in decibels per second.</summary>
    public bool? ReleaseTriggerDecayInDecibels { get; internal set; }

    /// <summary>Tags that silence this zone when one of them is triggered (<c>silencedByTags</c>).</summary>
    public IReadOnlyList<string> SilencedByTags { get; internal set; } = [];

    /// <summary>How quickly a silenced voice stops (<c>silencingMode</c>). Default fast.</summary>
    public DecentSamplerSilencingMode? SilencingMode { get; internal set; }

    /// <summary>A custom fade-out time in seconds when silenced (<c>silencingDecay</c>). Default 0.</summary>
    public double? SilencingDecay { get; internal set; }

    /// <summary>The round-robin mode (<c>seqMode</c>). Default always, which is round robins off.</summary>
    public DecentSamplerSeqMode? SeqMode { get; internal set; }

    /// <summary>The round-robin queue length (<c>seqLength</c>). Default 0, meaning auto-detect.</summary>
    public int? SeqLength { get; internal set; }

    /// <summary>This zone's place in the round-robin queue (<c>seqPosition</c>). Default 1.</summary>
    public int? SeqPosition { get; internal set; }

    /// <summary>The lowest note that triggers the zone (<c>loNote</c>). Default 0.</summary>
    public int? LoNote { get; internal set; }

    /// <summary>The highest note that triggers the zone (<c>hiNote</c>). Default 127.</summary>
    public int? HiNote { get; internal set; }

    /// <summary>The lowest velocity that triggers the zone (<c>loVel</c>). Default 0.</summary>
    public int? LoVel { get; internal set; }

    /// <summary>The highest velocity that triggers the zone (<c>hiVel</c>). Default 127.</summary>
    public int? HiVel { get; internal set; }

    /// <summary>
    /// Controller ranges from <c>loCCN</c>/<c>hiCCN</c> that decide whether the zone plays at all,
    /// keyed by controller number. Never null; empty when none were written.
    /// </summary>
    public IReadOnlyDictionary<int, DecentSamplerCcRange> CcFilters =>
        (IReadOnlyDictionary<int, DecentSamplerCcRange>)_ccFilters ?? EmptyCcRanges;

    /// <summary>
    /// Controller ranges from <c>onLoCCN</c>/<c>onHiCCN</c> that themselves trigger the zone, keyed by
    /// controller number. Never null; empty when none were written.
    /// </summary>
    public IReadOnlyDictionary<int, DecentSamplerCcRange> CcTriggers =>
        (IReadOnlyDictionary<int, DecentSamplerCcRange>)_ccTriggers ?? EmptyCcRanges;

    /// <summary>Where the zone's audio is played from (<c>playbackMode</c>). Default auto.</summary>
    public DecentSamplerPlaybackMode? PlaybackMode { get; internal set; }

    /// <summary>A delay before the zone starts (<c>delay</c>). Default 0.</summary>
    public double? Delay { get; internal set; }

    /// <summary>The unit <see cref="Delay"/> is measured in (<c>delayUnit</c>). Default seconds.</summary>
    public DecentSamplerTimeUnit? DelayUnit { get; internal set; }

    /// <summary>Whether the delay pattern repeats (<c>retriggerEnabled</c>). Default false.</summary>
    public bool? RetriggerEnabled { get; internal set; }

    /// <summary>The gap between pattern repeats (<c>retriggerInterval</c>). Default 4.</summary>
    public double? RetriggerInterval { get; internal set; }

    /// <summary>
    /// The unit <see cref="RetriggerInterval"/> is measured in (<c>retriggerIntervalUnit</c>).
    /// Default beats.
    /// </summary>
    public DecentSamplerTimeUnit? RetriggerIntervalUnit { get; internal set; }

    /// <summary>The first frame of the audio to play (<c>start</c>). Default 0.</summary>
    public long? Start { get; internal set; }

    /// <summary>The last frame of the audio to play (<c>end</c>). Default the file's last frame.</summary>
    public long? End { get; internal set; }

    /// <summary>The first frame of the loop (<c>loopStart</c>).</summary>
    public long? LoopStart { get; internal set; }

    /// <summary>The last frame of the loop (<c>loopEnd</c>).</summary>
    public long? LoopEnd { get; internal set; }

    /// <summary>The crossfade length in frames (<c>loopCrossfade</c>). Default 0, crossfades off.</summary>
    public long? LoopCrossfade { get; internal set; }

    /// <summary>The crossfade curve (<c>loopCrossfadeMode</c>). Default equal power.</summary>
    public DecentSamplerLoopCrossfadeMode? LoopCrossfadeMode { get; internal set; }

    /// <summary>Whether the loop is used (<c>loopEnabled</c>).</summary>
    public bool? LoopEnabled { get; internal set; }

    /// <summary>Whether the amplitude envelope runs at all (<c>ampEnvEnabled</c>). Default true.</summary>
    public bool? AmpEnvEnabled { get; internal set; }

    /// <summary>Envelope attack time in seconds (<c>attack</c>).</summary>
    public double? Attack { get; internal set; }

    /// <summary>Envelope decay time in seconds (<c>decay</c>).</summary>
    public double? Decay { get; internal set; }

    /// <summary>Envelope sustain level, 0 to 1 (<c>sustain</c>).</summary>
    public double? Sustain { get; internal set; }

    /// <summary>Envelope release time in seconds (<c>release</c>).</summary>
    public double? Release { get; internal set; }

    /// <summary>Attack curve, -100 logarithmic to 100 exponential (<c>attackCurve</c>). Default -100.</summary>
    public double? AttackCurve { get; internal set; }

    /// <summary>Decay curve, -100 logarithmic to 100 exponential (<c>decayCurve</c>). Default 100.</summary>
    public double? DecayCurve { get; internal set; }

    /// <summary>Release curve, -100 logarithmic to 100 exponential (<c>releaseCurve</c>). Default 100.</summary>
    public double? ReleaseCurve { get; internal set; }

    /// <summary>
    /// The eight <c>outputNTarget</c> attributes, indexed from zero. Never null; empty when none were
    /// written. Output 1 defaults to the main output and the rest to no output.
    /// </summary>
    public IReadOnlyList<DecentSamplerOutputTarget?> OutputTargets =>
        (IReadOnlyList<DecentSamplerOutputTarget?>)_outputTargets ?? [];

    /// <summary>
    /// The eight <c>outputNVolume</c> attributes, indexed from zero. Never null; empty when none were
    /// written. Each defaults to 1.0.
    /// </summary>
    public IReadOnlyList<double?> OutputVolumes => (IReadOnlyList<double?>)_outputVolumes ?? [];

    /// <summary>The waveform an oscillator generates (<c>waveform</c>). Default sine.</summary>
    public DecentSamplerWaveform? Waveform { get; internal set; }

    /// <summary>The <c>waveform</c> attribute exactly as written, including names this engine does not know.</summary>
    public string WaveformName { get; internal set; }

    /// <summary>String damping for the <c>pluck1</c> waveform, 0 to 1 (<c>damping</c>). Default 0.5.</summary>
    public double? Damping { get; internal set; }

    /// <summary>Excitation blend for the <c>pluck1</c> waveform, 0 to 1 (<c>pluckType</c>). Default 0.5.</summary>
    public double? PluckType { get; internal set; }

    /// <summary>The multi-frame wavetable file, relative to the preset (<c>wavetableFile</c>).</summary>
    public string WavetableFile { get; internal set; }

    /// <summary>Samples per wavetable frame (<c>wavetableFrameSize</c>). Default 2048.</summary>
    public int? WavetableFrameSize { get; internal set; }

    /// <summary>Position within the wavetable, 0 to 1 (<c>wavetablePosition</c>). Default 0.</summary>
    public double? WavetablePosition { get; internal set; }

    /// <summary>Whether each note-on randomizes the start phase (<c>randomPhase</c>). Default false.</summary>
    public bool? RandomPhase { get; internal set; }

    /// <summary>
    /// Whether adjacent wavetable frames are crossfaded (<c>wavetableFrameInterpolation</c>).
    /// Default true.
    /// </summary>
    public bool? WavetableFrameInterpolation { get; internal set; }

    /// <summary>Active harmonic partials, 1 to 64 (<c>numPartials</c>). Default 8.</summary>
    public int? NumPartials { get; internal set; }

    /// <summary>Spectral tilt, -1 to 1 (<c>harmonicTilt</c>). Default 0.</summary>
    public double? HarmonicTilt { get; internal set; }

    /// <summary>Odd/even partial balance, 0 to 1 (<c>harmonicOddEvenBalance</c>). Default 0.5.</summary>
    public double? HarmonicOddEvenBalance { get; internal set; }

    /// <summary>Loudness compensation, 0 to 1 (<c>harmonicNormalization</c>). Default 0.</summary>
    public double? HarmonicNormalization { get; internal set; }

    /// <summary>
    /// The 64 <c>harmonicPartialNLevel</c> attributes, indexed from zero so that index 0 is the
    /// fundamental. Never null; empty when none were written. Each defaults to 0.
    /// </summary>
    public IReadOnlyList<double?> HarmonicPartialLevels => (IReadOnlyList<double?>)_harmonicPartialLevels ?? [];

    /// <summary>The DX7 algorithm number, 1 to 32 (<c>fmAlgorithm</c>). Default 1.</summary>
    public int? FmAlgorithm { get; internal set; }

    /// <summary>
    /// The six FM operators, indexed from zero so that index 0 is operator 1. Never null; empty when
    /// the preset wrote no FM attributes.
    /// </summary>
    public IReadOnlyList<DecentSamplerFmOperator> FmOperators => (IReadOnlyList<DecentSamplerFmOperator>)_fmOperators ?? [];

    private static readonly Dictionary<int, DecentSamplerCcRange> EmptyCcRanges = [];

    /// <summary>The <c>outputNTarget</c> written here, or null when this element did not write one.</summary>
    /// <param name="index">The output index, 0 to 7.</param>
    /// <returns>The target, or null.</returns>
    public DecentSamplerOutputTarget? OutputTarget(int index) =>
        _outputTargets == null || index < 0 || index >= _outputTargets.Length ? null : _outputTargets[index];

    /// <summary>The <c>outputNVolume</c> written here, or null when this element did not write one.</summary>
    /// <param name="index">The output index, 0 to 7.</param>
    /// <returns>The volume, or null.</returns>
    public double? OutputVolume(int index) =>
        _outputVolumes == null || index < 0 || index >= _outputVolumes.Length ? null : _outputVolumes[index];

    /// <summary>The <c>harmonicPartialNLevel</c> written here, or null when this element did not write one.</summary>
    /// <param name="index">The partial index, 0 to 63, where 0 is the fundamental.</param>
    /// <returns>The level, or null.</returns>
    public double? HarmonicPartialLevel(int index) =>
        _harmonicPartialLevels == null || index < 0 || index >= _harmonicPartialLevels.Length
            ? null
            : _harmonicPartialLevels[index];

    /// <summary>The FM operator this element wrote attributes for, or null when it wrote none.</summary>
    /// <param name="index">The operator index, 0 to 5, where 0 is operator 1.</param>
    /// <returns>The operator, or null.</returns>
    public DecentSamplerFmOperator FmOperator(int index) =>
        _fmOperators == null || index < 0 || index >= _fmOperators.Length ? null : _fmOperators[index];

    internal void SetOutputTarget(int index, DecentSamplerOutputTarget target)
    {
        _outputTargets ??= new DecentSamplerOutputTarget?[8];
        _outputTargets[index] = target;
    }

    internal void SetOutputVolume(int index, double volume)
    {
        _outputVolumes ??= new double?[8];
        _outputVolumes[index] = volume;
    }

    internal void SetHarmonicPartialLevel(int index, double level)
    {
        _harmonicPartialLevels ??= new double?[64];
        _harmonicPartialLevels[index] = level;
    }

    internal DecentSamplerFmOperator EnsureFmOperator(int index)
    {
        if (_fmOperators == null)
        {
            _fmOperators = new DecentSamplerFmOperator[6];
            for (var i = 0; i < 6; i++)
            {
                _fmOperators[i] = new DecentSamplerFmOperator(i + 1);
            }
        }

        return _fmOperators[index];
    }

    internal void AddCcFilter(DecentSamplerCcRange range)
    {
        _ccFilters ??= [];
        _ccFilters[range.Controller] = range;
    }

    internal void AddCcTrigger(DecentSamplerCcRange range)
    {
        _ccTriggers ??= [];
        _ccTriggers[range.Controller] = range;
    }
}

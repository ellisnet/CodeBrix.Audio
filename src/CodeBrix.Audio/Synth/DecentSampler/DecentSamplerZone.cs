using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// One playable zone - a sample or an oscillator - with its inheritance resolved: every value is the
/// effective one, taken from the zone, then its group, then <c>&lt;groups&gt;</c>, and finally from the
/// documented default.
/// </summary>
/// <remarks>
/// <para>
/// Two things do not follow the "nearest wins" rule, because the guide describes them as cumulative:
/// tag lists are the union of every level, and controller ranges are merged with the nearer level
/// winning per controller number.
/// </para>
/// <para>
/// A few defaults are not written down anywhere in the developer guide. Where that is so, the choice
/// this engine makes is named in the property's own documentation and awaits measurement against the
/// reference player.
/// </para>
/// </remarks>
public sealed class DecentSamplerZone
{
    private readonly DecentSamplerOutputTarget[] _outputTargets = new DecentSamplerOutputTarget[8];
    private readonly double[] _outputVolumes = new double[8];
    private readonly Dictionary<int, DecentSamplerCcRange> _ccFilters = [];
    private readonly Dictionary<int, DecentSamplerCcRange> _ccTriggers = [];
    private DecentSamplerFmOperator[] _fmOperators;
    private double[] _harmonicPartialLevels;

    private DecentSamplerZone(DecentSamplerGroup group, DecentSamplerSoundElement source, int index)
    {
        Group = group;
        Source = source;
        Index = index;
        Kind = source is DecentSamplerOscillatorElement
            ? DecentSamplerZoneKind.Oscillator
            : DecentSamplerZoneKind.Sample;
    }

    /// <summary>The group the zone belongs to.</summary>
    public DecentSamplerGroup Group { get; }

    /// <summary>The parsed <c>&lt;sample&gt;</c> or <c>&lt;oscillator&gt;</c> this was resolved from.</summary>
    public DecentSamplerSoundElement Source { get; }

    /// <summary>The zone's 0-based position within its group, samples first then oscillators.</summary>
    public int Index { get; }

    /// <summary>Whether the zone plays a file or generates a waveform.</summary>
    public DecentSamplerZoneKind Kind { get; }

    /// <summary>The sample file, relative to the preset, or null for an oscillator zone.</summary>
    public string Path { get; internal set; }

    /// <summary>
    /// The note the sample was recorded at. Defaults to 60 (middle C under this engine's octave
    /// convention) when the zone writes none.
    /// </summary>
    public int RootNote { get; internal set; }

    /// <summary>Whether the zone wrote a root note of its own.</summary>
    public bool HasRootNote { get; internal set; }

    /// <summary>The lowest note that triggers the zone. Default 0.</summary>
    public int LoNote { get; internal set; }

    /// <summary>The highest note that triggers the zone. Default 127.</summary>
    public int HiNote { get; internal set; }

    /// <summary>The lowest velocity that triggers the zone. Default 0.</summary>
    public int LoVel { get; internal set; }

    /// <summary>The highest velocity that triggers the zone. Default 127.</summary>
    public int HiVel { get; internal set; }

    /// <summary>The zone's own volume as a linear multiplier. Default 1.</summary>
    public double Volume { get; internal set; }

    /// <summary>The zone's stereo position, -100 to 100. Default 0.</summary>
    public double Pan { get; internal set; }

    /// <summary>The zone's own fine tuning in semitones. Default 0.</summary>
    public double Tuning { get; internal set; }

    /// <summary>Keyboard pitch tracking, 0 to 1. Default 1.</summary>
    public double PitchKeyTrack { get; internal set; }

    /// <summary>Portamento time in seconds. Default 0.</summary>
    public double GlideTime { get; internal set; }

    /// <summary>When portamento applies. Default legato.</summary>
    public DecentSamplerGlideMode GlideMode { get; internal set; }

    /// <summary>How much note velocity drives volume, 0 to 1. Default 1.</summary>
    public double AmpVelTrack { get; internal set; }

    /// <summary>What makes the zone sound. Default attack.</summary>
    public DecentSamplerTrigger Trigger { get; internal set; }

    /// <summary>
    /// The release-trigger decay rate: negative decibels per second when
    /// <see cref="ReleaseTriggerDecayInDecibels"/> is true, otherwise a linear factor per second.
    /// Default 0, meaning no decay.
    /// </summary>
    public double ReleaseTriggerDecay { get; internal set; }

    /// <summary>Whether <see cref="ReleaseTriggerDecay"/> is in decibels per second.</summary>
    public bool ReleaseTriggerDecayInDecibels { get; internal set; }

    /// <summary>The union of the tags on the zone, its group and <c>&lt;groups&gt;</c>.</summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>The union of the silencing tags on the zone, its group and <c>&lt;groups&gt;</c>.</summary>
    public IReadOnlyList<string> SilencedByTags { get; internal set; } = [];

    /// <summary>How quickly a silenced voice stops. Default fast.</summary>
    public DecentSamplerSilencingMode SilencingMode { get; internal set; }

    /// <summary>
    /// A custom fade-out time in seconds when silenced. Default 0, which leaves
    /// <see cref="SilencingMode"/> in charge.
    /// </summary>
    public double SilencingDecay { get; internal set; }

    /// <summary>The round-robin mode. Default always, which is round robins off.</summary>
    public DecentSamplerSeqMode SeqMode { get; internal set; }

    /// <summary>The round-robin queue length. Default 0, meaning the engine detects it.</summary>
    public int SeqLength { get; internal set; }

    /// <summary>This zone's place in the round-robin queue. Default 1.</summary>
    public int SeqPosition { get; internal set; }

    /// <summary>Controller ranges that decide whether the zone plays, keyed by controller number.</summary>
    public IReadOnlyDictionary<int, DecentSamplerCcRange> CcFilters => _ccFilters;

    /// <summary>Controller ranges that themselves trigger the zone, keyed by controller number.</summary>
    public IReadOnlyDictionary<int, DecentSamplerCcRange> CcTriggers => _ccTriggers;

    /// <summary>Where the zone's audio is played from. Default auto.</summary>
    public DecentSamplerPlaybackMode PlaybackMode { get; internal set; }

    /// <summary>A delay before the zone starts, in <see cref="DelayUnit"/>. Default 0.</summary>
    public double Delay { get; internal set; }

    /// <summary>The unit <see cref="Delay"/> is measured in. Default seconds.</summary>
    public DecentSamplerTimeUnit DelayUnit { get; internal set; }

    /// <summary>Whether the delay pattern repeats. Default false.</summary>
    public bool RetriggerEnabled { get; internal set; }

    /// <summary>The gap between pattern repeats, in <see cref="RetriggerIntervalUnit"/>. Default 4.</summary>
    public double RetriggerInterval { get; internal set; }

    /// <summary>The unit <see cref="RetriggerInterval"/> is measured in. Default beats.</summary>
    public DecentSamplerTimeUnit RetriggerIntervalUnit { get; internal set; }

    /// <summary>The first frame of the audio to play. Default 0.</summary>
    public long Start { get; internal set; }

    /// <summary>The last frame of the audio to play, or null to play to the end of the file.</summary>
    public long? End { get; internal set; }

    /// <summary>
    /// The first frame of the loop, or null to fall back to the file's own loop marker.
    /// </summary>
    public long? LoopStart { get; internal set; }

    /// <summary>
    /// The last frame of the loop, or null to fall back to the file's own loop marker.
    /// </summary>
    public long? LoopEnd { get; internal set; }

    /// <summary>The crossfade length in frames. Default 0, crossfades off.</summary>
    public long LoopCrossfade { get; internal set; }

    /// <summary>The crossfade curve. Default equal power.</summary>
    public DecentSamplerLoopCrossfadeMode LoopCrossfadeMode { get; internal set; }

    /// <summary>
    /// Whether the loop is used, or null when the preset says nothing - in which case the engine
    /// follows the sample file's own loop markers, as the guide describes.
    /// </summary>
    public bool? LoopEnabled { get; internal set; }

    /// <summary>Whether the amplitude envelope runs. Default true.</summary>
    public bool AmpEnvEnabled { get; internal set; }

    /// <summary>
    /// Envelope attack time in seconds. Defaults to 0; the guide states no default, so this awaits
    /// measurement against the reference player.
    /// </summary>
    public double Attack { get; internal set; }

    /// <summary>
    /// Envelope decay time in seconds. Defaults to 0; the guide states no default, so this awaits
    /// measurement against the reference player.
    /// </summary>
    public double Decay { get; internal set; }

    /// <summary>
    /// Envelope sustain level, 0 to 1. Defaults to 1; the guide states no default, so this awaits
    /// measurement against the reference player.
    /// </summary>
    public double Sustain { get; internal set; }

    /// <summary>
    /// Envelope release time in seconds. Defaults to 0; the guide states no default, so this awaits
    /// measurement against the reference player.
    /// </summary>
    public double Release { get; internal set; }

    /// <summary>Attack curve, -100 logarithmic to 100 exponential. Default -100.</summary>
    public double AttackCurve { get; internal set; }

    /// <summary>Decay curve, -100 logarithmic to 100 exponential. Default 100.</summary>
    public double DecayCurve { get; internal set; }

    /// <summary>Release curve, -100 logarithmic to 100 exponential. Default 100.</summary>
    public double ReleaseCurve { get; internal set; }

    /// <summary>Where the zone sends its audio, indexed from zero. Always eight entries.</summary>
    public IReadOnlyList<DecentSamplerOutputTarget> OutputTargets => _outputTargets;

    /// <summary>The volume of each of the zone's outputs, indexed from zero. Always eight entries.</summary>
    public IReadOnlyList<double> OutputVolumes => _outputVolumes;

    /// <summary>
    /// Notes that must have been the previously triggered note for the zone to play. Empty when the
    /// zone sets no such rule.
    /// </summary>
    public IReadOnlyList<int> PreviousNotes { get; internal set; } = [];

    /// <summary>
    /// The exact semitone distance from the previously triggered note that lets the zone play, or null
    /// when the zone sets no such rule.
    /// </summary>
    public int? LegatoInterval { get; internal set; }

    /// <summary>The waveform an oscillator zone generates. Default sine.</summary>
    public DecentSamplerWaveform Waveform { get; internal set; }

    /// <summary>The waveform name exactly as the preset wrote it, or null when it wrote none.</summary>
    public string WaveformName { get; internal set; }

    /// <summary>String damping for the <c>pluck1</c> waveform, 0 to 1. Default 0.5.</summary>
    public double Damping { get; internal set; }

    /// <summary>Excitation blend for the <c>pluck1</c> waveform, 0 to 1. Default 0.5.</summary>
    public double PluckType { get; internal set; }

    /// <summary>The wavetable file, relative to the preset, or null when the zone names none.</summary>
    public string WavetableFile { get; internal set; }

    /// <summary>Samples per wavetable frame. Default 2048.</summary>
    public int WavetableFrameSize { get; internal set; }

    /// <summary>Position within the wavetable, 0 to 1. Default 0.</summary>
    public double WavetablePosition { get; internal set; }

    /// <summary>Whether each note-on randomizes the wavetable start phase. Default false.</summary>
    public bool RandomPhase { get; internal set; }

    /// <summary>Whether adjacent wavetable frames are crossfaded. Default true.</summary>
    public bool WavetableFrameInterpolation { get; internal set; }

    /// <summary>Active harmonic partials, 1 to 64. Default 8.</summary>
    public int NumPartials { get; internal set; }

    /// <summary>Spectral tilt, -1 to 1. Default 0.</summary>
    public double HarmonicTilt { get; internal set; }

    /// <summary>Odd/even partial balance, 0 to 1. Default 0.5.</summary>
    public double HarmonicOddEvenBalance { get; internal set; }

    /// <summary>Loudness compensation, 0 to 1. Default 0.</summary>
    public double HarmonicNormalization { get; internal set; }

    /// <summary>
    /// The 64 harmonic partial levels, indexed from zero so that index 0 is the fundamental. Always 64
    /// entries; each defaults to 0.
    /// </summary>
    public IReadOnlyList<double> HarmonicPartialLevels => _harmonicPartialLevels ??= BuildHarmonicLevels();

    /// <summary>The DX7 algorithm number, 1 to 32. Default 1.</summary>
    public int FmAlgorithm { get; internal set; }

    /// <summary>
    /// The six FM operators, indexed from zero so that index 0 is operator 1. Always six entries, each
    /// with every property filled in from the zone, its group, <c>&lt;groups&gt;</c> or the documented
    /// default - so nothing on them is null.
    /// </summary>
    public IReadOnlyList<DecentSamplerFmOperator> FmOperators => _fmOperators ??= BuildFmOperators();

    /// <summary>Writes one of the zone's output targets, which an <c>OUTPUT_n_TARGET</c> binding does.</summary>
    /// <param name="index">The output, 0 to 7.</param>
    /// <param name="target">Where the output goes.</param>
    internal void SetOutputTarget(int index, DecentSamplerOutputTarget target)
    {
        if (index >= 0 && index < _outputTargets.Length)
        {
            _outputTargets[index] = target;
        }
    }

    /// <summary>Writes one of the zone's output volumes, which an <c>OUTPUT_n_VOLUME</c> binding does.</summary>
    /// <param name="index">The output, 0 to 7.</param>
    /// <param name="volume">The linear volume.</param>
    internal void SetOutputVolume(int index, double volume)
    {
        if (index >= 0 && index < _outputVolumes.Length)
        {
            _outputVolumes[index] = volume;
        }
    }

    /// <summary>
    /// Writes one harmonic partial level, which an <c>OSCILLATOR_HARMONIC_PARTIAL_n_LEVEL</c> binding
    /// does.
    /// </summary>
    /// <param name="index">The partial, 0 to 63, where 0 is the fundamental.</param>
    /// <param name="level">The level.</param>
    internal void SetHarmonicPartialLevel(int index, double level)
    {
        _harmonicPartialLevels ??= BuildHarmonicLevels();

        if (index >= 0 && index < _harmonicPartialLevels.Length)
        {
            _harmonicPartialLevels[index] = level;
        }
    }

    internal static IReadOnlyList<string> UnionTags(params IReadOnlyList<string>[] lists)
    {
        List<string> result = null;

        foreach (var list in lists)
        {
            if (list == null || list.Count == 0)
            {
                continue;
            }

            result ??= [];

            foreach (var tag in list)
            {
                if (!result.Contains(tag))
                {
                    result.Add(tag);
                }
            }
        }

        return (IReadOnlyList<string>)result ?? [];
    }

    internal static DecentSamplerZone Resolve(
        DecentSamplerGroup group, DecentSamplerSoundElement source, int index)
    {
        var zone = new DecentSamplerZone(group, source, index);
        var element = group.Source;
        var groups = group.Groups;

        var sample = source as DecentSamplerSampleElement;

        zone.Path = sample?.Path;
        zone.HasRootNote = sample?.RootNote != null;
        zone.RootNote = sample?.RootNote ?? 60;
        zone.PreviousNotes = sample?.PreviousNotes ?? [];
        zone.LegatoInterval = sample?.LegatoInterval;

        zone.LoNote = source.LoNote ?? element.LoNote ?? groups.LoNote ?? 0;
        zone.HiNote = source.HiNote ?? element.HiNote ?? groups.HiNote ?? 127;
        zone.LoVel = source.LoVel ?? element.LoVel ?? groups.LoVel ?? 0;
        zone.HiVel = source.HiVel ?? element.HiVel ?? groups.HiVel ?? 127;

        zone.Volume = source.Volume ?? 1.0;
        zone.Pan = source.Pan ?? element.Pan ?? groups.Pan ?? 0.0;
        zone.Tuning = source.Tuning ?? 0.0;
        zone.PitchKeyTrack = source.PitchKeyTrack ?? element.PitchKeyTrack ?? groups.PitchKeyTrack ?? 1.0;
        zone.GlideTime = source.GlideTime ?? element.GlideTime ?? groups.GlideTime ?? 0.0;
        zone.GlideMode = source.GlideMode ?? element.GlideMode ?? groups.GlideMode ??
                         DecentSamplerGlideMode.Legato;
        zone.AmpVelTrack = source.AmpVelTrack ?? element.AmpVelTrack ?? groups.AmpVelTrack ?? 1.0;

        zone.Trigger = source.Trigger ?? element.Trigger ?? groups.Trigger ?? DecentSamplerTrigger.Attack;
        zone.ReleaseTriggerDecay =
            source.ReleaseTriggerDecay ?? element.ReleaseTriggerDecay ?? groups.ReleaseTriggerDecay ?? 0.0;
        zone.ReleaseTriggerDecayInDecibels =
            source.ReleaseTriggerDecayInDecibels ?? element.ReleaseTriggerDecayInDecibels ??
            groups.ReleaseTriggerDecayInDecibels ?? false;

        zone.Tags = UnionTags(groups.Tags, element.Tags, source.Tags);
        zone.SilencedByTags = UnionTags(groups.SilencedByTags, element.SilencedByTags, source.SilencedByTags);
        zone.SilencingMode = source.SilencingMode ?? element.SilencingMode ?? groups.SilencingMode ??
                             DecentSamplerSilencingMode.Fast;
        zone.SilencingDecay = source.SilencingDecay ?? element.SilencingDecay ?? groups.SilencingDecay ?? 0.0;

        zone.SeqMode = source.SeqMode ?? element.SeqMode ?? groups.SeqMode ?? DecentSamplerSeqMode.Always;
        zone.SeqLength = source.SeqLength ?? element.SeqLength ?? groups.SeqLength ?? 0;
        zone.SeqPosition = source.SeqPosition ?? element.SeqPosition ?? groups.SeqPosition ?? 1;

        MergeRanges(zone._ccFilters, groups.CcFilters, element.CcFilters, source.CcFilters);
        MergeRanges(zone._ccTriggers, groups.CcTriggers, element.CcTriggers, source.CcTriggers);

        zone.PlaybackMode = source.PlaybackMode ?? element.PlaybackMode ?? groups.PlaybackMode ??
                            DecentSamplerPlaybackMode.Auto;
        zone.Delay = source.Delay ?? element.Delay ?? groups.Delay ?? 0.0;
        zone.DelayUnit = source.DelayUnit ?? element.DelayUnit ?? groups.DelayUnit ??
                         DecentSamplerTimeUnit.Seconds;
        zone.RetriggerEnabled =
            source.RetriggerEnabled ?? element.RetriggerEnabled ?? groups.RetriggerEnabled ?? false;
        zone.RetriggerInterval =
            source.RetriggerInterval ?? element.RetriggerInterval ?? groups.RetriggerInterval ?? 4.0;
        zone.RetriggerIntervalUnit =
            source.RetriggerIntervalUnit ?? element.RetriggerIntervalUnit ?? groups.RetriggerIntervalUnit ??
            DecentSamplerTimeUnit.Beats;

        zone.Start = source.Start ?? element.Start ?? groups.Start ?? 0;
        zone.End = source.End ?? element.End ?? groups.End;
        zone.LoopStart = source.LoopStart ?? element.LoopStart ?? groups.LoopStart;
        zone.LoopEnd = source.LoopEnd ?? element.LoopEnd ?? groups.LoopEnd;
        zone.LoopCrossfade = source.LoopCrossfade ?? element.LoopCrossfade ?? groups.LoopCrossfade ?? 0;
        zone.LoopCrossfadeMode = source.LoopCrossfadeMode ?? element.LoopCrossfadeMode ??
                                 groups.LoopCrossfadeMode ?? DecentSamplerLoopCrossfadeMode.EqualPower;
        zone.LoopEnabled = source.LoopEnabled ?? element.LoopEnabled ?? groups.LoopEnabled;

        zone.AmpEnvEnabled = source.AmpEnvEnabled ?? element.AmpEnvEnabled ?? groups.AmpEnvEnabled ?? true;
        zone.Attack = source.Attack ?? element.Attack ?? groups.Attack ?? 0.0;
        zone.Decay = source.Decay ?? element.Decay ?? groups.Decay ?? 0.0;
        zone.Sustain = source.Sustain ?? element.Sustain ?? groups.Sustain ?? 1.0;
        zone.Release = source.Release ?? element.Release ?? groups.Release ?? 0.0;
        zone.AttackCurve = source.AttackCurve ?? element.AttackCurve ?? groups.AttackCurve ?? -100.0;
        zone.DecayCurve = source.DecayCurve ?? element.DecayCurve ?? groups.DecayCurve ?? 100.0;
        zone.ReleaseCurve = source.ReleaseCurve ?? element.ReleaseCurve ?? groups.ReleaseCurve ?? 100.0;

        for (var output = 0; output < 8; output++)
        {
            zone._outputTargets[output] =
                source.OutputTarget(output) ?? element.OutputTarget(output) ?? groups.OutputTarget(output) ??
                (output == 0 ? DecentSamplerOutputTarget.MainOutput : DecentSamplerOutputTarget.NoOutput);
            zone._outputVolumes[output] =
                source.OutputVolume(output) ?? element.OutputVolume(output) ?? groups.OutputVolume(output) ?? 1.0;
        }

        zone.WaveformName = source.WaveformName ?? element.WaveformName ?? groups.WaveformName;
        zone.Waveform = source.Waveform ?? element.Waveform ?? groups.Waveform ?? DecentSamplerWaveform.Sine;
        zone.Damping = source.Damping ?? element.Damping ?? groups.Damping ?? 0.5;
        zone.PluckType = source.PluckType ?? element.PluckType ?? groups.PluckType ?? 0.5;
        zone.WavetableFile = source.WavetableFile ?? element.WavetableFile ?? groups.WavetableFile;
        zone.WavetableFrameSize =
            source.WavetableFrameSize ?? element.WavetableFrameSize ?? groups.WavetableFrameSize ?? 2048;
        zone.WavetablePosition =
            source.WavetablePosition ?? element.WavetablePosition ?? groups.WavetablePosition ?? 0.0;
        zone.RandomPhase = source.RandomPhase ?? element.RandomPhase ?? groups.RandomPhase ?? false;
        zone.WavetableFrameInterpolation =
            source.WavetableFrameInterpolation ?? element.WavetableFrameInterpolation ??
            groups.WavetableFrameInterpolation ?? true;

        zone.NumPartials = source.NumPartials ?? element.NumPartials ?? groups.NumPartials ?? 8;
        zone.HarmonicTilt = source.HarmonicTilt ?? element.HarmonicTilt ?? groups.HarmonicTilt ?? 0.0;
        zone.HarmonicOddEvenBalance = source.HarmonicOddEvenBalance ?? element.HarmonicOddEvenBalance ??
                                      groups.HarmonicOddEvenBalance ?? 0.5;
        zone.HarmonicNormalization = source.HarmonicNormalization ?? element.HarmonicNormalization ??
                                     groups.HarmonicNormalization ?? 0.0;
        zone.FmAlgorithm = source.FmAlgorithm ?? element.FmAlgorithm ?? groups.FmAlgorithm ?? 1;

        return zone;
    }

    private static void MergeRanges(
        Dictionary<int, DecentSamplerCcRange> target,
        params IReadOnlyDictionary<int, DecentSamplerCcRange>[] sources)
    {
        foreach (var source in sources)
        {
            foreach (var pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }

    private double[] BuildHarmonicLevels()
    {
        var levels = new double[64];

        for (var partial = 0; partial < 64; partial++)
        {
            levels[partial] =
                Source.HarmonicPartialLevel(partial) ??
                Group.Source.HarmonicPartialLevel(partial) ??
                Group.Groups.HarmonicPartialLevel(partial) ?? 0.0;
        }

        return levels;
    }

    private DecentSamplerFmOperator[] BuildFmOperators()
    {
        var operators = new DecentSamplerFmOperator[6];

        for (var index = 0; index < 6; index++)
        {
            var zoneOperator = Source.FmOperator(index);
            var groupOperator = Group.Source.FmOperator(index);
            var groupsOperator = Group.Groups.FmOperator(index);

            var resolved = new DecentSamplerFmOperator(index + 1)
            {
                Ratio = Pick(zoneOperator?.Ratio, groupOperator?.Ratio, groupsOperator?.Ratio, 1.0),
                Detune = Pick(zoneOperator?.Detune, groupOperator?.Detune, groupsOperator?.Detune, 0.0),
                Mode = zoneOperator?.Mode ?? groupOperator?.Mode ?? groupsOperator?.Mode ??
                       DecentSamplerFmOperatorMode.Ratio,
                FixedFrequency = Pick(zoneOperator?.FixedFrequency, groupOperator?.FixedFrequency,
                    groupsOperator?.FixedFrequency, 440.0),
                // MEASURED (round 2, item 27): operator 1's level defaults to 1.0 and every other
                // operator's to 0.0, which is why an fm6op zone carrying no fm* attributes renders a
                // pure sine. The guide's own attribute table says 1.0 for all six; it is wrong.
                Level = Pick(zoneOperator?.Level, groupOperator?.Level, groupsOperator?.Level,
                    index == 0 ? 1.0 : 0.0),
                VelocitySensitivity = Pick(zoneOperator?.VelocitySensitivity,
                    groupOperator?.VelocitySensitivity, groupsOperator?.VelocitySensitivity, 0.0),
                Feedback = Pick(zoneOperator?.Feedback, groupOperator?.Feedback, groupsOperator?.Feedback, 0.0),
                Attack = Pick(zoneOperator?.Attack, groupOperator?.Attack, groupsOperator?.Attack, 0.0),
                Decay = Pick(zoneOperator?.Decay, groupOperator?.Decay, groupsOperator?.Decay, 0.0),
                Sustain = Pick(zoneOperator?.Sustain, groupOperator?.Sustain, groupsOperator?.Sustain, 1.0),
                Release = Pick(zoneOperator?.Release, groupOperator?.Release, groupsOperator?.Release, -1.0),
                EnvelopeType = zoneOperator?.EnvelopeType ?? groupOperator?.EnvelopeType ??
                               groupsOperator?.EnvelopeType ?? DecentSamplerFmEnvelopeType.Adsr,
            };

            double[] defaultRates = [99.0, 99.0, 0.0, 99.0];
            double[] defaultLevels = [99.0, 99.0, 99.0, 0.0];

            for (var stage = 0; stage < 4; stage++)
            {
                resolved.EgRates[stage] = Pick(zoneOperator?.EgRates[stage], groupOperator?.EgRates[stage],
                    groupsOperator?.EgRates[stage], defaultRates[stage]);
                resolved.EgLevels[stage] = Pick(zoneOperator?.EgLevels[stage], groupOperator?.EgLevels[stage],
                    groupsOperator?.EgLevels[stage], defaultLevels[stage]);
            }

            operators[index] = resolved;
        }

        return operators;
    }

    private static double Pick(double? zone, double? group, double? groups, double fallback) =>
        zone ?? group ?? groups ?? fallback;

    /// <inheritdoc/>
    public override string ToString() =>
        Kind == DecentSamplerZoneKind.Oscillator
            ? $"oscillator {Waveform} ({LoNote}-{HiNote})"
            : $"sample {Path} ({LoNote}-{HiNote}, vel {LoVel}-{HiVel})";
}

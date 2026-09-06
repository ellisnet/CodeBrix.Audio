using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// A group with its inheritance resolved: every value is the effective one, either written on the
/// group or inherited from <c>&lt;groups&gt;</c>, or the documented default.
/// </summary>
/// <remarks>
/// The parsed model keeps "written here" and "not written here" apart, which is what inheritance needs;
/// this is the other half, the flat view the engine plays from. Build it with
/// <see cref="DecentSamplerPreset.ResolveGroups"/>.
/// </remarks>
public sealed class DecentSamplerGroup
{
    private readonly List<DecentSamplerZone> _zones = [];
    private readonly DecentSamplerOutputTarget[] _outputTargets = new DecentSamplerOutputTarget[8];
    private readonly double[] _outputVolumes = new double[8];

    private DecentSamplerGroup(DecentSamplerGroupElement source, DecentSamplerGroupsElement groups)
    {
        Source = source;
        Groups = groups;
    }

    /// <summary>The parsed <c>&lt;group&gt;</c> this was resolved from.</summary>
    public DecentSamplerGroupElement Source { get; }

    /// <summary>The parsed <c>&lt;groups&gt;</c> the group inherits from.</summary>
    public DecentSamplerGroupsElement Groups { get; }

    /// <summary>The group's 0-based position, which bindings address it by.</summary>
    public int Index { get; internal set; }

    /// <summary>The group's name, or null when it has none.</summary>
    public string Name { get; internal set; }

    /// <summary>Whether the group plays. Default true.</summary>
    public bool Enabled { get; internal set; }

    /// <summary>The group's volume as a linear multiplier. Default 1.</summary>
    public double Volume { get; internal set; }

    /// <summary>The instrument-wide volume as a linear multiplier. Default 1.</summary>
    public double InstrumentVolume { get; internal set; }

    /// <summary>The group's stereo position, -100 to 100. Default 0.</summary>
    public double Pan { get; internal set; }

    /// <summary>Group tuning in semitones. Default 0.</summary>
    public double GroupTuning { get; internal set; }

    /// <summary>Instrument-wide tuning in semitones. Default 0.</summary>
    public double GlobalTuning { get; internal set; }

    /// <summary>How much note velocity drives volume, 0 to 1. Default 1.</summary>
    public double AmpVelTrack { get; internal set; }

    /// <summary>Keyboard pitch tracking, 0 to 1. Default 1.</summary>
    public double PitchKeyTrack { get; internal set; }

    /// <summary>Portamento time in seconds. Default 0.</summary>
    public double GlideTime { get; internal set; }

    /// <summary>When portamento applies. Default legato.</summary>
    public DecentSamplerGlideMode GlideMode { get; internal set; }

    /// <summary>The group's tags, including any inherited from <c>&lt;groups&gt;</c>.</summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>Where the group sends its audio, indexed from zero. Always eight entries.</summary>
    public IReadOnlyList<DecentSamplerOutputTarget> OutputTargets => _outputTargets;

    /// <summary>The volume of each of the group's outputs, indexed from zero. Always eight entries.</summary>
    public IReadOnlyList<double> OutputVolumes => _outputVolumes;

    /// <summary>The group's own effect chain, or null when it declares none.</summary>
    public DecentSamplerEffectsElement Effects => Source.Effects;

    /// <summary>The group's zones - its samples and oscillators - in document order.</summary>
    public IReadOnlyList<DecentSamplerZone> Zones => _zones;

    /// <summary>Writes one of the group's output targets, which an <c>OUTPUT_n_TARGET</c> binding does.</summary>
    /// <param name="index">The output, 0 to 7.</param>
    /// <param name="target">Where the output goes.</param>
    internal void SetOutputTarget(int index, DecentSamplerOutputTarget target)
    {
        if (index >= 0 && index < _outputTargets.Length)
        {
            _outputTargets[index] = target;
        }
    }

    /// <summary>Writes one of the group's output volumes, which an <c>OUTPUT_n_VOLUME</c> binding does.</summary>
    /// <param name="index">The output, 0 to 7.</param>
    /// <param name="volume">The linear volume.</param>
    internal void SetOutputVolume(int index, double volume)
    {
        if (index >= 0 && index < _outputVolumes.Length)
        {
            _outputVolumes[index] = volume;
        }
    }

    internal static IReadOnlyList<DecentSamplerGroup> Resolve(DecentSamplerPreset preset)
    {
        var resolved = new List<DecentSamplerGroup>();

        if (preset?.Groups == null)
        {
            return resolved;
        }

        var groups = preset.Groups;

        foreach (var element in groups.Groups)
        {
            var group = new DecentSamplerGroup(element, groups)
            {
                Index = element.Index,
                Name = element.Name,
                Enabled = element.Enabled ?? true,
                Volume = element.Volume ?? 1.0,
                InstrumentVolume = groups.Volume ?? 1.0,
                Pan = element.Pan ?? groups.Pan ?? 0.0,
                GroupTuning = element.GroupTuning ?? element.Tuning ?? 0.0,
                GlobalTuning = groups.GlobalTuning ?? groups.Tuning ?? 0.0,
                AmpVelTrack = element.AmpVelTrack ?? groups.AmpVelTrack ?? 1.0,
                PitchKeyTrack = element.PitchKeyTrack ?? groups.PitchKeyTrack ?? 1.0,
                GlideTime = element.GlideTime ?? groups.GlideTime ?? 0.0,
                GlideMode = element.GlideMode ?? groups.GlideMode ?? DecentSamplerGlideMode.Legato,
                Tags = DecentSamplerZone.UnionTags(groups.Tags, element.Tags),
            };

            for (var output = 0; output < 8; output++)
            {
                group._outputTargets[output] =
                    element.OutputTarget(output) ?? groups.OutputTarget(output) ??
                    (output == 0 ? DecentSamplerOutputTarget.MainOutput : DecentSamplerOutputTarget.NoOutput);
                group._outputVolumes[output] =
                    element.OutputVolume(output) ?? groups.OutputVolume(output) ?? 1.0;
            }

            foreach (var sample in element.Samples)
            {
                group._zones.Add(DecentSamplerZone.Resolve(group, sample, group._zones.Count));
            }

            foreach (var oscillator in element.Oscillators)
            {
                group._zones.Add(DecentSamplerZone.Resolve(group, oscillator, group._zones.Count));
            }

            resolved.Add(group);
        }

        return resolved;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"group {Index}{(Name == null ? string.Empty : " \"" + Name + "\"")}: {_zones.Count} zones";
}

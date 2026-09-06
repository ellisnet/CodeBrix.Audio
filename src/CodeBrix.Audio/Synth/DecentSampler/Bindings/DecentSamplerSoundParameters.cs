using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The parameter targets of the sound tree: the instrument as a whole, a group, a single sample or
/// oscillator zone, a tag, and a bus.
/// </summary>
/// <remarks>
/// <para>
/// A binding at <c>level="instrument"</c> or <c>level="group"</c> reaches parameters that live on the
/// zones - the envelope, the loop points, the key range - because that is where the resolved values
/// are. Such a binding OVERRIDES what the zone declared: the guide calls these "Global Amp Envelope
/// Attack" and "Low Note ... that will trigger samples in this group", and a group-level
/// <c>ROOT_NOTE</c> binding would reach nothing at all if it deferred to the root note every sample
/// declares.
/// </para>
/// <para>
/// The one exception is volume and tuning, which the format itself keeps on separate levels that
/// combine: instrument volume multiplies group volume multiplies zone volume, and the tunings add. Each
/// of those has its own property, so a global volume knob and a group volume knob do not fight.
/// </para>
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    /// <summary>The target of an instrument-level parameter, or null when there is no such thing.</summary>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForInstrument(string parameter)
    {
        var owner = _engine.Instrument;
        var groups = _engine.Instrument.Groups;
        var zones = _engine.Instrument.Zones;
        var folded = Fold(parameter);
        var name = "instrument." + parameter;

        switch (folded)
        {
            case "ampvolume":
            case "volume":
                return Number(
                    owner, name, parameter, groups.Count > 0 ? groups[0].InstrumentVolume : 1.0,
                    value => Each(groups, group => group.InstrumentVolume = value));

            case "globaltuning":
                return Number(
                    owner, name, parameter, groups.Count > 0 ? groups[0].GlobalTuning : 0.0,
                    value => Each(groups, group => group.GlobalTuning = value));

            case "allnotesoff":
                return Command(owner, name, parameter, _engine.RequestAllNotesOff);
        }

        return ForSoundParameter(owner, name, parameter, folded, groups, zones);
    }

    /// <summary>The target of a group-level parameter, or null when there is no such thing.</summary>
    /// <param name="group">The group.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForGroup(DecentSamplerGroup group, string parameter)
    {
        var folded = Fold(parameter);
        var name = "group[" + Ordinal(group.Index) + "]." + parameter;
        IReadOnlyList<DecentSamplerGroup> groups = [group];

        switch (folded)
        {
            case "grouptuning":
            case "tuning":
                return Number(group, name, parameter, group.GroupTuning, value => group.GroupTuning = value);

            case "globaltuning":
                return Number(group, name, parameter, group.GlobalTuning, value => group.GlobalTuning = value);
        }

        return ForSoundParameter(group, name, parameter, folded, groups, group.Zones);
    }

    /// <summary>
    /// The target of a parameter on one sample or oscillator zone, or null when there is no such thing.
    /// </summary>
    /// <param name="zone">The zone.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForZone(DecentSamplerZone zone, string parameter)
    {
        var folded = Fold(parameter);
        var name = "group[" + Ordinal(zone.Group.Index) + "].zone[" + Ordinal(zone.Index) + "]." + parameter;

        return folded switch
        {
            "tuning" => Number(zone, name, parameter, zone.Tuning, value => zone.Tuning = value),
            _ => ForSoundParameter(zone, name, parameter, folded, [], [zone]),
        };
    }

    /// <summary>The target of a tag-level parameter, or null when there is no such thing.</summary>
    /// <param name="tag">The tag's live state.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForTag(DecentSamplerTagState tag, string parameter)
    {
        var name = "tag[" + tag.Name + "]." + parameter;

        switch (Fold(parameter))
        {
            case "tagenabled":
            case "enabled":
                return Switch(tag, name, parameter, tag.Enabled, value =>
                {
                    tag.Enabled = value;

                    if (tag.Source != null)
                    {
                        tag.Source.Enabled = value;
                    }
                });

            case "tagvolume":
            case "ampvolume":
            case "volume":
                return Number(tag, name, parameter, tag.Volume, value =>
                {
                    tag.Volume = value;

                    if (tag.Source != null)
                    {
                        tag.Source.Volume = value;
                    }
                });

            case "pan":
                return Number(tag, name, parameter, tag.Pan, value =>
                {
                    tag.Pan = value;

                    if (tag.Source != null)
                    {
                        tag.Source.Pan = value;
                    }
                });

            case "tagpolyphony":
            case "polyphony":
                return Integer(tag, name, parameter, tag.Polyphony, value =>
                {
                    tag.Polyphony = value;

                    if (tag.Source != null)
                    {
                        tag.Source.Polyphony = value;
                    }
                });

            default:
                return null;
        }
    }

    /// <summary>The target of a bus-level parameter, or null when there is no such thing.</summary>
    /// <param name="bus">The bus.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForBus(DecentSamplerBus bus, string parameter)
    {
        var folded = Fold(parameter);
        var name = "bus[" + Ordinal(bus.Index) + "]." + parameter;

        if (folded is "busvolume" or "ampvolume" or "volume")
        {
            return Number(bus, name, parameter, bus.BusVolume ?? 1.0, value => bus.BusVolume = value);
        }

        if (TryTrailingIndex(folded, "output", "volume", 8, out var volumeOutput))
        {
            return Number(
                bus, name, parameter, bus.OutputVolume(volumeOutput - 1) ?? 1.0,
                value => bus.SetOutputVolume(volumeOutput - 1, value));
        }

        if (TryTrailingIndex(folded, "output", "target", 8, out var targetOutput))
        {
            return Choice<DecentSamplerOutputTarget>(
                bus, name, parameter,
                bus.OutputTarget(targetOutput - 1) ??
                (targetOutput == 1 ? DecentSamplerOutputTarget.MainOutput : DecentSamplerOutputTarget.NoOutput),
                value => bus.SetOutputTarget(targetOutput - 1, value));
        }

        return null;
    }

    private static void Each<T>(IReadOnlyList<T> items, Action<T> action)
    {
        for (var index = 0; index < items.Count; index++)
        {
            action(items[index]);
        }
    }

    private static double FirstOf(
        IReadOnlyList<DecentSamplerZone> zones, Func<DecentSamplerZone, double> read, double fallback) =>
        zones.Count > 0 ? read(zones[0]) : fallback;

    private DecentSamplerParameter ForSoundParameter(
        object owner,
        string name,
        string parameter,
        string folded,
        IReadOnlyList<DecentSamplerGroup> groups,
        IReadOnlyList<DecentSamplerZone> zones)
    {
        switch (folded)
        {
            case "enabled":
                return groups.Count == 0
                    ? null
                    : Switch(
                        owner, name, parameter, groups[0].Enabled,
                        value => Each(groups, group => group.Enabled = value));

            case "ampvolume":
            case "groupvolume":
            case "volume":
                return groups.Count > 0
                    ? Number(
                        owner, name, parameter, groups[0].Volume,
                        value => Each(groups, group => group.Volume = value))
                    : Number(
                        owner, name, parameter, FirstOf(zones, zone => zone.Volume, 1.0),
                        value => Each(zones, zone => zone.Volume = value));

            case "pan":
                return Number(
                    owner, name, parameter,
                    groups.Count > 0 ? groups[0].Pan : FirstOf(zones, zone => zone.Pan, 0.0),
                    value =>
                    {
                        Each(groups, group => group.Pan = value);
                        Each(zones, zone => zone.Pan = value);
                    });

            case "ampveltrack":
                return Number(
                    owner, name, parameter,
                    groups.Count > 0 ? groups[0].AmpVelTrack : FirstOf(zones, zone => zone.AmpVelTrack, 1.0),
                    value =>
                    {
                        Each(groups, group => group.AmpVelTrack = value);
                        Each(zones, zone => zone.AmpVelTrack = value);
                    });

            case "pitchkeytrack":
                return Number(
                    owner, name, parameter,
                    groups.Count > 0 ? groups[0].PitchKeyTrack : FirstOf(zones, zone => zone.PitchKeyTrack, 1.0),
                    value =>
                    {
                        Each(groups, group => group.PitchKeyTrack = value);
                        Each(zones, zone => zone.PitchKeyTrack = value);
                    });

            case "glidetime":
                return Number(
                    owner, name, parameter,
                    groups.Count > 0 ? groups[0].GlideTime : FirstOf(zones, zone => zone.GlideTime, 0.0),
                    value =>
                    {
                        Each(groups, group => group.GlideTime = value);
                        Each(zones, zone => zone.GlideTime = value);
                    });

            case "glidemode":
                return Choice<DecentSamplerGlideMode>(
                    owner, name, parameter,
                    groups.Count > 0 ? groups[0].GlideMode :
                    zones.Count > 0 ? zones[0].GlideMode : DecentSamplerGlideMode.Legato,
                    value =>
                    {
                        Each(groups, group => group.GlideMode = value);
                        Each(zones, zone => zone.GlideMode = value);
                    });

            case "ampenvenabled":
                return Switch(
                    owner, name, parameter, zones.Count > 0 && zones[0].AmpEnvEnabled,
                    value => Each(zones, zone => zone.AmpEnvEnabled = value));

            case "envattack":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.Attack, 0.0),
                    value => Each(zones, zone => zone.Attack = value));

            case "envattackcurve":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.AttackCurve, -100.0),
                    value => Each(zones, zone => zone.AttackCurve = value));

            case "envdecay":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.Decay, 0.0),
                    value => Each(zones, zone => zone.Decay = value));

            case "envdecaycurve":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.DecayCurve, 100.0),
                    value => Each(zones, zone => zone.DecayCurve = value));

            case "envsustain":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.Sustain, 1.0),
                    value => Each(zones, zone => zone.Sustain = value));

            case "envrelease":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.Release, 0.0),
                    value => Each(zones, zone => zone.Release = value));

            case "envreleasecurve":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.ReleaseCurve, 100.0),
                    value => Each(zones, zone => zone.ReleaseCurve = value));

            case "samplestart":
                return Frames(
                    owner, name, parameter, zones.Count > 0 ? zones[0].Start : 0.0,
                    value => Each(zones, zone => zone.Start = value));

            case "sampleend":
                return Frames(
                    owner, name, parameter, zones.Count > 0 ? zones[0].End ?? 0.0 : 0.0,
                    value => Each(zones, zone => zone.End = value));

            case "loopstart":
                return Frames(
                    owner, name, parameter, zones.Count > 0 ? zones[0].LoopStart ?? 0.0 : 0.0,
                    value => Each(zones, zone => zone.LoopStart = value));

            case "loopend":
                return Frames(
                    owner, name, parameter, zones.Count > 0 ? zones[0].LoopEnd ?? 0.0 : 0.0,
                    value => Each(zones, zone => zone.LoopEnd = value));

            case "lonote":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].LoNote : 0,
                    value => Each(zones, zone => zone.LoNote = value));

            case "hinote":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].HiNote : 127,
                    value => Each(zones, zone => zone.HiNote = value));

            case "lovel":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].LoVel : 0,
                    value => Each(zones, zone => zone.LoVel = value));

            case "hivel":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].HiVel : 127,
                    value => Each(zones, zone => zone.HiVel = value));

            case "rootnote":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].RootNote : 60,
                    value => Each(zones, zone => zone.RootNote = value));

            case "silencingdecay":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.SilencingDecay, 0.0),
                    value => Each(zones, zone => zone.SilencingDecay = value));

            case "silencingmode":
                return Choice<DecentSamplerSilencingMode>(
                    owner, name, parameter,
                    zones.Count > 0 ? zones[0].SilencingMode : DecentSamplerSilencingMode.Fast,
                    value => Each(zones, zone => zone.SilencingMode = value));
        }

        if (TryTrailingIndex(folded, "output", "volume", 8, out var volumeOutput))
        {
            return Number(
                owner, name, parameter,
                groups.Count > 0 ? groups[0].OutputVolumes[volumeOutput - 1] : 1.0,
                value =>
                {
                    Each(groups, group => group.SetOutputVolume(volumeOutput - 1, value));
                    Each(zones, zone => zone.SetOutputVolume(volumeOutput - 1, value));
                });
        }

        if (TryTrailingIndex(folded, "output", "target", 8, out var targetOutput))
        {
            return Choice<DecentSamplerOutputTarget>(
                owner, name, parameter,
                groups.Count > 0
                    ? groups[0].OutputTargets[targetOutput - 1]
                    : targetOutput == 1
                        ? DecentSamplerOutputTarget.MainOutput
                        : DecentSamplerOutputTarget.NoOutput,
                value =>
                {
                    Each(groups, group => group.SetOutputTarget(targetOutput - 1, value));
                    Each(zones, zone => zone.SetOutputTarget(targetOutput - 1, value));
                });
        }

        return ForOscillatorParameter(owner, name, parameter, folded, zones);
    }
}

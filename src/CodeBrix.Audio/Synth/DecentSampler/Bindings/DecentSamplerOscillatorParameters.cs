using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The <c>OSCILLATOR_*</c> parameter targets: the waveform itself, the pluck and wavetable controls,
/// the sixty-four harmonic partial levels and the six FM operators with all twenty of their parameters
/// each.
/// </summary>
/// <remarks>
/// The guide lists these at <c>level="group"</c>, so a binding usually reaches every oscillator zone of
/// one group; <c>level="oscillator"</c> with <c>oscillatorTags</c> reaches one at a time. Both arrive
/// here with the zones already chosen. The values live on the zone, so the oscillator engine - which
/// ships in the companion synthesis package - reads them through the properties it already uses.
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    private static readonly string[] FmSuffixes =
    [
        "ratio", "detune", "mode", "fixedfreq", "level", "velocitysensitivity", "feedback",
        "attack", "decay", "sustain", "release", "egtype",
        "egrate1", "egrate2", "egrate3", "egrate4",
        "eglevel1", "eglevel2", "eglevel3", "eglevel4",
    ];

    private DecentSamplerParameter ForOscillatorParameter(
        object owner,
        string name,
        string parameter,
        string folded,
        IReadOnlyList<DecentSamplerZone> zones)
    {
        if (!folded.StartsWith("oscillator", StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = folded.Substring("oscillator".Length);

        switch (suffix)
        {
            case "waveform":
            case "shape":
                return Choice<DecentSamplerWaveform>(
                    owner, name, parameter,
                    zones.Count > 0 ? zones[0].Waveform : DecentSamplerWaveform.Sine,
                    value => Each(zones, zone =>
                    {
                        zone.Waveform = value;
                        zone.WaveformName = value.ToString();
                    }));

            case "damping":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.Damping, 0.5),
                    value => Each(zones, zone => zone.Damping = value));

            case "plucktype":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.PluckType, 0.5),
                    value => Each(zones, zone => zone.PluckType = value));

            case "wavetableposition":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.WavetablePosition, 0.0),
                    value => Each(zones, zone => zone.WavetablePosition = value));

            case "wavetableframeinterpolation":
                return Switch(
                    owner, name, parameter, zones.Count == 0 || zones[0].WavetableFrameInterpolation,
                    value => Each(zones, zone => zone.WavetableFrameInterpolation = value));

            case "harmonicnumpartials":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].NumPartials : 8,
                    value => Each(zones, zone => zone.NumPartials = value));

            case "harmonictilt":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.HarmonicTilt, 0.0),
                    value => Each(zones, zone => zone.HarmonicTilt = value));

            case "harmonicoddevenbalance":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.HarmonicOddEvenBalance, 0.5),
                    value => Each(zones, zone => zone.HarmonicOddEvenBalance = value));

            case "harmonicnormalization":
                return Number(
                    owner, name, parameter, FirstOf(zones, zone => zone.HarmonicNormalization, 0.0),
                    value => Each(zones, zone => zone.HarmonicNormalization = value));

            case "fmalgorithm":
                return Integer(
                    owner, name, parameter, zones.Count > 0 ? zones[0].FmAlgorithm : 1,
                    value => Each(zones, zone => zone.FmAlgorithm = value));
        }

        if (TryTrailingIndex(suffix, "harmonicpartial", "level", 64, out var partial))
        {
            return Number(
                owner, name, parameter,
                zones.Count > 0 ? zones[0].HarmonicPartialLevels[partial - 1] : 0.0,
                value => Each(zones, zone => zone.SetHarmonicPartialLevel(partial - 1, value)));
        }

        return ForFmOperatorParameter(owner, name, parameter, suffix, zones);
    }

    private DecentSamplerParameter ForFmOperatorParameter(
        object owner,
        string name,
        string parameter,
        string suffix,
        IReadOnlyList<DecentSamplerZone> zones)
    {
        foreach (var fmSuffix in FmSuffixes)
        {
            if (!TryTrailingIndex(suffix, "fmop", fmSuffix, 6, out var operatorNumber))
            {
                continue;
            }

            var index = operatorNumber - 1;
            var first = zones.Count > 0 ? zones[0].FmOperators[index] : null;

            switch (fmSuffix)
            {
                case "ratio":
                    return Number(
                        owner, name, parameter, first?.Ratio ?? 1.0,
                        value => EachOperator(zones, index, item => item.Ratio = value));

                case "detune":
                    return Number(
                        owner, name, parameter, first?.Detune ?? 0.0,
                        value => EachOperator(zones, index, item => item.Detune = value));

                case "mode":
                    return Choice<DecentSamplerFmOperatorMode>(
                        owner, name, parameter, first?.Mode ?? DecentSamplerFmOperatorMode.Ratio,
                        value => EachOperator(zones, index, item => item.Mode = value));

                case "fixedfreq":
                    return Number(
                        owner, name, parameter, first?.FixedFrequency ?? 440.0,
                        value => EachOperator(zones, index, item => item.FixedFrequency = value));

                case "level":
                    return Number(
                        owner, name, parameter, first?.Level ?? 0.0,
                        value => EachOperator(zones, index, item => item.Level = value));

                case "velocitysensitivity":
                    return Number(
                        owner, name, parameter, first?.VelocitySensitivity ?? 0.0,
                        value => EachOperator(zones, index, item => item.VelocitySensitivity = value));

                case "feedback":
                    return Number(
                        owner, name, parameter, first?.Feedback ?? 0.0,
                        value => EachOperator(zones, index, item => item.Feedback = value));

                case "attack":
                    return Number(
                        owner, name, parameter, first?.Attack ?? 0.0,
                        value => EachOperator(zones, index, item => item.Attack = value));

                case "decay":
                    return Number(
                        owner, name, parameter, first?.Decay ?? 0.0,
                        value => EachOperator(zones, index, item => item.Decay = value));

                case "sustain":
                    return Number(
                        owner, name, parameter, first?.Sustain ?? 1.0,
                        value => EachOperator(zones, index, item => item.Sustain = value));

                case "release":
                    return Number(
                        owner, name, parameter, first?.Release ?? 0.0,
                        value => EachOperator(zones, index, item => item.Release = value));

                case "egtype":
                    return Choice<DecentSamplerFmEnvelopeType>(
                        owner, name, parameter, first?.EnvelopeType ?? DecentSamplerFmEnvelopeType.Adsr,
                        value => EachOperator(zones, index, item => item.EnvelopeType = value));
            }

            if (fmSuffix.StartsWith("egrate", StringComparison.Ordinal))
            {
                var stage = fmSuffix[fmSuffix.Length - 1] - '1';
                return Number(
                    owner, name, parameter, first?.EgRates[stage] ?? 0.0,
                    value => EachOperator(zones, index, item => item.EgRates[stage] = value));
            }

            if (fmSuffix.StartsWith("eglevel", StringComparison.Ordinal))
            {
                var stage = fmSuffix[fmSuffix.Length - 1] - '1';
                return Number(
                    owner, name, parameter, first?.EgLevels[stage] ?? 0.0,
                    value => EachOperator(zones, index, item => item.EgLevels[stage] = value));
            }
        }

        return null;
    }

    private static void EachOperator(
        IReadOnlyList<DecentSamplerZone> zones, int index, Action<DecentSamplerFmOperator> action)
    {
        for (var position = 0; position < zones.Count; position++)
        {
            action(zones[position].FmOperators[index]);
        }
    }
}

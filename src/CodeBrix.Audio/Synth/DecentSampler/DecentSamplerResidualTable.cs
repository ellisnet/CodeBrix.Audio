using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Everything in the Decent Sampler format this engine reads and reports but does not act on, grouped
/// and explained. It is the published answer to "what is missing", and it is derived from
/// <see cref="DecentSamplerSupportedFeatures"/> rather than written by hand, so it cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// Every feature the list carries with status <see cref="DecentSamplerFeatureStatus.Parsed"/> belongs
/// to exactly one entry here, and no feature marked <see cref="DecentSamplerFeatureStatus.Implemented"/>
/// belongs to any of them. A test asserts both directions, so adding a feature to the format's
/// vocabulary without either implementing it or explaining it fails the build.
/// </para>
/// <para>
/// The largest entry by far is the one the add-on package answers: reference
/// CodeBrix.Audio.ModestSynth and call <c>ModestSynth.Register()</c> before loading an instrument and
/// every oscillator waveform and creative effect in it becomes live.
/// </para>
/// </remarks>
public static class DecentSamplerResidualTable
{
    private static readonly string[] UiElements =
    [
        "ui", "tab", "labeled-knob", "labeled_knob", "control", "button", "menu", "option", "state",
        "xyPad", "x", "y", "label", "image", "multiFrameImage", "rectangle", "line", "oscilloscope",
        "keyboard", "color",
    ];

    private static readonly string[] UiEnumerations =
    [
        "aspectRatioMode", "customSkinImageOrientation", "hAlign", "vAlign", "orientation", "snapMode",
        "sourceFormat", "style", "type", "valueType",
    ];

    private static readonly string[] AnimationPlaybackModes =
    [
        "forward_loop", "forward_once", "ping_pong_loop", "reverse_loop", "reverse_once", "stopped",
    ];

    private static readonly string[] StoreOwners =
    [
        "DecentSamplerLibraryInfo", "presetMenu", "presetMenu/menu", "presetMenu/preset",
    ];

    private static readonly string[] SoundOwners = ["groups", "group", "sample", "oscillator"];

    private static readonly string[] AddOnEffectTypes =
    [
        "phaser", "pitch_shift", "wave_folder", "wave_shaper", "stereo_simulator", "bit_crusher", "gate",
    ];

    private static readonly string[] AddOnEffectAttributes =
    [
        "algorithm", "amount", "bitDepth", "centerFrequency", "drive", "driveBoost", "highQuality",
        "outputLevel", "pitchShift", "sampleRateReduction", "width",
    ];

    private static readonly string[] FilterEnvelopeAttributes =
    [
        "envelope_amount", "envelope_attack", "envelope_decay", "envelope_sustain", "envelope_release",
    ];

    private static readonly string[] UndefinedEffectAttributes = ["shape", "wetDryMix"];

    private static readonly string[] OscillatorFileAttributes =
    [
        "start", "end", "loopStart", "loopEnd", "loopEnabled", "loopCrossfade", "loopCrossfadeMode",
        "playbackMode",
    ];

    private static readonly HashSet<string> GenerationAttributes = BuildGenerationAttributes();

    private static readonly DecentSamplerResidualEntry[] AllEntries = Build();

    /// <summary>The whole table, in the order the documentation prints it.</summary>
    public static IReadOnlyList<DecentSamplerResidualEntry> Entries => AllEntries;

    /// <summary>The entry that accounts for a feature, or null when the engine implements it.</summary>
    /// <param name="feature">The feature to explain.</param>
    /// <returns>The entry, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="feature"/> is null.</exception>
    public static DecentSamplerResidualEntry Explain(DecentSamplerFeature feature)
    {
        if (feature == null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        var index = Classify(feature);
        return index < 0 ? null : AllEntries[index];
    }

    /// <summary>Whether the residual table accounts for a feature.</summary>
    /// <param name="feature">The feature to look for.</param>
    /// <param name="entry">The entry that accounts for it, or null.</param>
    /// <returns>True when an entry accounts for it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="feature"/> is null.</exception>
    public static bool TryExplain(DecentSamplerFeature feature, out DecentSamplerResidualEntry entry)
    {
        entry = Explain(feature);
        return entry != null;
    }

    /// <summary>The whole table as plain text, one paragraph per entry, for a host to log or show.</summary>
    /// <returns>The text.</returns>
    public static string Describe()
    {
        var builder = new StringBuilder();

        foreach (var entry in AllEntries)
        {
            builder.Append(entry.Name)
                .Append(" (")
                .Append(entry.Features.Count.ToString(CultureInfo.InvariantCulture))
                .Append(entry.Features.Count == 1 ? " feature" : " features")
                .AppendLine(")");

            builder.Append("  ").AppendLine(entry.Reason);
        }

        return builder.ToString();
    }

    private static DecentSamplerResidualEntry[] Build()
    {
        (string Name, string Reason, bool AddOn)[] definitions =
        [
            ("user interface appearance",
                "The <ui> section is parsed completely and becomes a live control model - a host reads " +
                "and writes every control's value, state, text, colour, position and image, and a " +
                "binding moves them - but nothing here DRAWS an interface, so the positions, colours, " +
                "images, fonts, skins, tooltips and frame animations are stored rather than rendered.",
                false),
            ("sound generation and creative effects",
                "Oscillator waveforms and the seven creative effect types come from the " +
                "CodeBrix.Audio.ModestSynth add-on package. Reference it and call ModestSynth.Register() " +
                "before loading an instrument and every one of them plays; without it the oscillator " +
                "group is silent, the effect is bypassed, the rest of the preset plays, and Problems " +
                "names the package and the call. One waveform is not in this list because the 1.29 " +
                "guide does not document it: the 1.30 player's formant oscillator, which takes no " +
                "attributes at all. A preset naming it loads and - with the add-on registered - " +
                "sounds the reference's own fixed formant tone; any formant-* attribute is reported, " +
                "because the reference honours none either.",
                true),
            ("library catalogue and store distribution",
                "DSLibraryInfo's name, version, cover art and preset menu are read and exposed on the " +
                "instrument. productId marks a library distributed through the store, and it is " +
                "reported rather than acted on: nothing here activates, licenses or unlocks anything.",
                false),
            ("format version negotiation",
                "The root element's minVersion and pluginVersion are read and reported in Problems when " +
                "a preset asks for a format version beyond the one this engine documents. They change " +
                "nothing about the sound; a preset that needs a newer feature reports that feature.",
                false),
            ("sample attributes an oscillator zone inherits",
                "An <oscillator> inherits the whole sound-attribute set, and the ones that describe a " +
                "FILE - the start and end offsets, the loop points and crossfade, and playbackMode - " +
                "have nothing to act on, because an oscillator generates its audio instead of reading it.",
                false),
            ("the filter envelope",
                "The guide gives a filter effect five envelope_* attributes. The reference player has no " +
                "audible consumer for them, so they are parsed and kept on the effect and no envelope " +
                "is applied. Measuring one would move them into the engine unchanged.",
                false),
            ("effect spellings the guide's tables never define",
                "Two attribute names appear in the guide's effect EXAMPLES and in no table: shape and " +
                "wetDryMix. They are recognised so a preset carrying one is not reported as broken, and " +
                "kept in the effect's raw values, and nothing reads them.",
                false),
            ("tag pan",
                "A <tag> may carry a pan beside its enabled state, volume and polyphony. The other three " +
                "are live; the reference player has no consumer for the pan, so it is parsed and exposed " +
                "on the tag state and no voice is moved by it.",
                false),
            ("editor bookkeeping",
                "A <sample>'s length is the frame count the preset editor recorded when the library was " +
                "built. The engine reads the real length from the file, so the attribute is kept as " +
                "metadata and never used.",
                false),
        ];

        var features = new List<DecentSamplerFeature>[definitions.Length];

        for (var i = 0; i < definitions.Length; i++)
        {
            features[i] = [];
        }

        foreach (var feature in DecentSamplerSupportedFeatures.Features)
        {
            if (feature.Status != DecentSamplerFeatureStatus.Parsed)
            {
                continue;
            }

            var index = Classify(feature);

            if (index >= 0)
            {
                features[index].Add(feature);
            }
        }

        var entries = new DecentSamplerResidualEntry[definitions.Length];

        for (var i = 0; i < definitions.Length; i++)
        {
            entries[i] = new DecentSamplerResidualEntry(
                definitions[i].Name, definitions[i].Reason, definitions[i].AddOn, features[i]);
        }

        return entries;
    }

    // Which entry accounts for a feature, or -1 when the engine implements it. The rules are written
    // against the feature list's own vocabulary so that a new name lands somewhere or fails the test.
    private static int Classify(DecentSamplerFeature feature)
    {
        var owner = feature.Owner ?? string.Empty;
        var name = feature.Name;

        switch (feature.Category)
        {
            case DecentSamplerFeatureCategory.Waveform:
                return 1;

            case DecentSamplerFeatureCategory.EffectType:
                return Contains(AddOnEffectTypes, name) ? 1 : -1;

            case DecentSamplerFeatureCategory.Element:
                if (Contains(UiElements, name)) { return 0; }
                if (Contains(StoreOwners, name)) { return 2; }
                return name == "DecentSampler" ? 3 : -1;

            case DecentSamplerFeatureCategory.EnumerationValue:
                if (owner == "algorithm" || owner == "fmOpNEgType" || owner == "fmOpNMode") { return 1; }
                if (owner == "mode") { return name is "fixed" or "ratio" ? 1 : -1; }
                if (Contains(UiEnumerations, owner)) { return 0; }
                return owner == "playbackMode" && Contains(AnimationPlaybackModes, name) ? 0 : -1;

            case DecentSamplerFeatureCategory.Attribute:
                if (Contains(UiElements, owner)) { return 0; }
                if (Contains(StoreOwners, owner)) { return 2; }
                if (owner == "DecentSampler") { return 3; }

                if (Contains(SoundOwners, owner))
                {
                    if (GenerationAttributes.Contains(name)) { return 1; }
                    if (owner == "oscillator" && Contains(OscillatorFileAttributes, name)) { return 4; }
                    return owner == "sample" && name == "length" ? 8 : -1;
                }

                if (owner == "effect")
                {
                    if (Contains(AddOnEffectAttributes, name)) { return 1; }
                    if (Contains(FilterEnvelopeAttributes, name)) { return 5; }
                    return Contains(UndefinedEffectAttributes, name) ? 6 : -1;
                }

                return owner == "tag" && name == "pan" ? 7 : -1;

            default:
                return -1;
        }
    }

    private static HashSet<string> BuildGenerationAttributes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "waveform", "shape", "damping", "pluckType",
            "wavetableFile", "wavetableFrameSize", "wavetablePosition", "randomPhase",
            "wavetableFrameInterpolation",
            "numPartials", "harmonicTilt", "harmonicOddEvenBalance", "harmonicNormalization",
            "fmAlgorithm",
        };

        for (var partial = 1; partial <= 64; partial++)
        {
            names.Add("harmonicPartial" + partial.ToString(CultureInfo.InvariantCulture) + "Level");
        }

        string[] suffixes =
        [
            "Ratio", "Detune", "Mode", "FixedFreq", "Level", "VelocitySensitivity", "Feedback",
            "Attack", "Decay", "Sustain", "Release", "EgType",
            "EgRate1", "EgRate2", "EgRate3", "EgRate4",
            "EgLevel1", "EgLevel2", "EgLevel3", "EgLevel4",
        ];

        for (var op = 1; op <= 6; op++)
        {
            foreach (var suffix in suffixes)
            {
                names.Add("fmOp" + op.ToString(CultureInfo.InvariantCulture) + suffix);
            }
        }

        return names;
    }

    private static bool Contains(string[] names, string value)
    {
        foreach (var name in names)
        {
            if (string.Equals(name, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

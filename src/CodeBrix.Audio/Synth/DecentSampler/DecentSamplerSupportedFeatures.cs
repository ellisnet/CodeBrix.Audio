using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Every element, attribute, binding parameter, effect type, modulator, waveform, translation mode and
/// enumeration value of the Decent Sampler format that this engine knows about, each with a status.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth for "does the engine know this?", the counterpart of
/// <see cref="CodeBrix.Audio.Synth.Sfz.SfzSupportedOpcodes"/> for the other sampled format. The parser
/// asks it before deciding that an attribute is unknown, the survey tool measures corpus coverage
/// against it, and a test cross-checks it against the names taken from the developer guide, so the
/// claim can never quietly drift from the code.
/// </para>
/// <para>
/// A feature is <see cref="DecentSamplerFeatureStatus.Parsed"/> when it reaches the object model and
/// <see cref="DecentSamplerFeatureStatus.Implemented"/> when the engine also honours it. Everything
/// starts as parsed; each later phase of the engine flips the features it brings to life.
/// </para>
/// <para>
/// Attribute names are compared case-sensitively, as the format writes them. Binding parameter names,
/// effect types, waveforms and every other enumerated value are compared case-insensitively, because
/// real presets write <c>parameter="value"</c> as often as <c>parameter="VALUE"</c>.
/// </para>
/// </remarks>
public static class DecentSamplerSupportedFeatures
{
    private static readonly List<DecentSamplerFeature> AllFeatures = [];

    private static readonly Dictionary<string, HashSet<string>> AttributesByElement =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> ElementNameSet = new(StringComparer.Ordinal);
    private static readonly HashSet<string> BindingParameterSet = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BindingTypeSet = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BindingLevelSet = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EffectTypeSet = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ModulatorTypeSet = new(StringComparer.Ordinal);
    private static readonly HashSet<string> WaveformSet = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TranslationModeSet = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> EnumerationValues =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, DecentSamplerFeatureStatus> StatusByKey =
        new(StringComparer.Ordinal);

    private static readonly Regex ControllerAttribute =
        new("^(loCC|hiCC|onLoCC|onHiCC)([0-9]{1,3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static DecentSamplerSupportedFeatures()
    {
        // --- the inheritable sound attributes, shared by <groups>, <group>, <sample> and <oscillator>.
        var sound = new List<string>
        {
            "volume", "pan", "tuning", "groupTuning", "globalTuning", "pitchKeyTrack",
            "glideTime", "glideMode", "ampVelTrack", "trigger", "releaseTriggerDecay", "tags",
            "silencedByTags", "silencingMode", "silencingDecay",
            "seqMode", "seqLength", "seqPosition",
            "loNote", "hiNote", "loVel", "hiVel",
            "loCCN", "hiCCN", "onLoCCN", "onHiCCN",
            "playbackMode", "delay", "delayUnit",
            "retriggerEnabled", "retriggerInterval", "retriggerIntervalUnit",
            "start", "end", "loopStart", "loopEnd", "loopCrossfade", "loopCrossfadeMode", "loopEnabled",
            "ampEnvEnabled", "attack", "decay", "sustain", "release",
            "attackCurve", "decayCurve", "releaseCurve",
            "waveform", "damping", "pluckType",
            "wavetableFile", "wavetableFrameSize", "wavetablePosition", "randomPhase",
            "wavetableFrameInterpolation",
            "numPartials", "harmonicTilt", "harmonicOddEvenBalance", "harmonicNormalization",
            "fmAlgorithm",
        };

        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            sound.Add("output" + index + "Target");
            sound.Add("output" + index + "Volume");
        }

        for (var partial = 1; partial <= 64; partial++)
        {
            sound.Add("harmonicPartial" + partial.ToString(CultureInfo.InvariantCulture) + "Level");
        }

        string[] fmOperatorSuffixes =
        [
            "Ratio", "Detune", "Mode", "FixedFreq", "Level", "VelocitySensitivity", "Feedback",
            "Attack", "Decay", "Sustain", "Release", "EgType",
            "EgRate1", "EgRate2", "EgRate3", "EgRate4",
            "EgLevel1", "EgLevel2", "EgLevel3", "EgLevel4",
        ];

        for (var op = 1; op <= 6; op++)
        {
            foreach (var suffix in fmOperatorSuffixes)
            {
                sound.Add("fmOp" + op.ToString(CultureInfo.InvariantCulture) + suffix);
            }
        }

        Element("DecentSampler", "minVersion", "pluginVersion");
        Element("groups", [.. sound]);
        Element("group", [.. sound, "name", "enabled"]);
        Element("sample", [.. sound, "path", "rootNote", "previousNotes", "previousNote", "legatoInterval", "length"]);

        // The guide's own oscillator examples write a root note and the older "shape" spelling of
        // "waveform" on the element, so both are known here even though the tables omit them.
        Element("oscillator", [.. sound, "rootNote", "shape"]);

        Element("effects");
        Element("effect",
            "type", "tags", "frequency", "resonance", "q", "gain", "level", "levelUnit",
            "roomSize", "damping", "wetLevel", "wetDryMix", "delayTime", "delayTimeFormat", "feedback",
            "stereoOffset", "mix", "modDepth", "modRate", "centerFrequency", "irFile", "pitchShift",
            "drive", "threshold", "driveBoost", "outputLevel", "highQuality", "shape",
            "algorithm", "width", "bitDepth", "sampleRateReduction", "amount",
            "ratio", "attack", "release", "inputGain", "outputGain", "autoBypass",
            "envelope_amount", "envelope_attack", "envelope_decay", "envelope_sustain", "envelope_release");

        Element("buses");

        var busAttributes = new List<string> { "busVolume" };
        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            busAttributes.Add("output" + index + "Target");
            busAttributes.Add("output" + index + "Volume");
        }

        Element("bus", [.. busAttributes]);

        Element("midi");
        Element("cc", "number");
        Element("note", "note", "eventType", "enabled", "swallowNotes", "position", "velocity", "length");
        Element("velocity");

        Element("modulators");
        // "rate" is the older spelling of "frequency"; the guide's arpeggiator topic still writes it.
        Element("lfo", "shape", "frequency", "rate", "frequencyFormat", "modAmount", "delayTime", "scope",
            "modBehavior", "trigger", "tags");
        Element("envelope", "attack", "decay", "sustain", "release", "attackCurve", "decayCurve",
            "releaseCurve", "modAmount", "delayTime", "scope", "modBehavior", "tags");
        Element("midiCC", "number", "channel", "modAmount", "scope", "modBehavior", "tags");
        Element("midiVelocity", "modAmount", "scope", "modBehavior", "tags");
        Element("mpeTimbre", "risingSmoothingTime", "fallingSmoothingTime", "modAmount", "scope",
            "modBehavior", "tags");
        Element("mpePressure", "risingSmoothingTime", "fallingSmoothingTime", "modAmount", "scope",
            "modBehavior", "tags");
        Element("random", "mode", "frequency", "trigger", "seed", "modAmount", "scope", "modBehavior", "tags");

        Element("noteSequences");
        Element("sequence", "name", "length", "rate");

        Element("arpeggiator", "enabled", "arpOrder", "arpOctaveRange", "arpOctaveMode", "arpStepCount",
            "arpGateLength", "arpFollowGlobalTempo", "arpSyncDivision", "arpRateMultiplier", "arpOverrideBpm");

        Element("tags");
        Element("tag", "name", "enabled", "volume", "pan", "polyphony");

        Element("ui", "coverArt", "bgImage", "bgColor", "width", "height", "layoutMode", "bgMode");
        Element("tab", "name");

        string[] controlAttributes =
        [
            "x", "y", "width", "height", "parameterName", "style", "showLabel", "label",
            "minValue", "maxValue", "value", "defaultValue", "valueType", "type",
            "textColor", "textSize", "trackForegroundColor", "trackBackgroundColor",
            "tags", "visible", "enabled", "disabledOpacity", "tooltip", "uid",
            "snapMode", "snapStopPoints", "defeatSnapWithShift",
            "customSkinImage", "customSkinHoverImage", "customSkinNumFrames",
            "customSkinImageOrientation", "mouseDragSensitivity",
        ];

        Element("labeled-knob", controlAttributes);
        Element("labeled_knob", controlAttributes);
        Element("control", controlAttributes);
        Element("state", "name", "mainImage", "hoverImage", "clickImage");
        Element("button", "x", "y", "width", "height", "value", "style", "mainImage", "hoverImage",
            "clickImage", "disabledOpacity", "visible", "enabled", "tags", "tooltip", "uid",
            "parameterName", "defaultValue", "name");
        Element("menu", "x", "y", "width", "height", "value", "tags", "visible", "enabled",
            "textColor", "backgroundColor", "highlightedTextColor", "highlightedBackgroundColor",
            "vAlign", "hAlign", "tooltip", "uid", "requireSelection", "placeholderText");
        Element("option", "name");
        Element("xyPad", "x", "y", "width", "height", "markerDiameter", "markerOutlineColor",
            "markerFillColor", "outlineColor", "bgColor", "tooltip", "xValue", "yValue", "tags",
            "visible", "enabled", "parameterName", "uid");
        Element("x");
        Element("y");
        Element("label", "x", "y", "width", "height", "text", "textColor", "textSize", "vAlign",
            "hAlign", "orientation", "tags", "visible", "tooltip", "uid");
        Element("image", "x", "y", "width", "height", "path", "aspectRatioMode", "opacity", "visible",
            "tags", "tooltip", "uid");
        Element("multiFrameImage", "x", "y", "width", "height", "path", "numFrames", "frameRate",
            "opacity", "sourceFormat", "imageStripOrientaton", "playbackMode", "visible", "tags",
            "tooltip", "uid");
        Element("rectangle", "x", "y", "width", "height", "fillColor", "borderColor", "borderThickness",
            "visible", "tags", "tooltip", "uid");
        Element("line", "x1", "y1", "x2", "y2", "lineColor", "lineThickness", "visible", "tags",
            "tooltip", "uid");
        Element("oscilloscope", "x", "y", "width", "height", "backgroundColor", "waveColor",
            "lineThickness", "showCenterLine", "visible", "tags", "tooltip", "uid");
        Element("keyboard", "centerNote");
        Element("color", "loNote", "hiNote", "color");

        Element("binding",
            "type", "level", "position", "controlIndex", "groupIndex", "effectIndex", "modulatorIndex",
            "busIndex", "stateIndex", "bindingIndex", "midiElementIndex", "noteIndex", "colorIndex",
            "seqIndex", "tags", "groupTags", "sampleTags", "oscillatorTags", "effectTags",
            "modulatorTags", "controlTags", "enabled", "identifier", "parameter",
            "translation", "translationOutputMin", "translationOutputMax", "translationReversed",
            "translationTable", "translationValue", "triggerOnLoad", "modBehavior", "modAmount",
            "seqFollowGlobalTempo", "seqTriggerBehavior", "seqPlayerIdentifier",
            "seqTrackMidiInputVelocity", "seqTranspose", "seqTransposeWithRootNote",
            "seqPlaybackRate", "seqLoopMode");

        // --- the DSLibraryInfo.xml sidecar. Its <menu> is a different element from the interface's,
        // so it is registered under its own owner name.
        Element("DecentSamplerLibraryInfo", "name", "productId", "version", "coverArt");
        Element("presetMenu");
        Element("presetMenu/menu", "name");
        Element("presetMenu/preset", "file");

        AddBindingTypes();
        AddBindingLevels();
        AddBindingParameters();
        AddEffectTypes();
        AddModulatorTypes();
        AddWaveforms();
        AddTranslationModes();
        AddEnumerationValues();

        MarkSamplerEngineImplemented();
        MarkEffectsAndBusesImplemented();
        MarkModulatorsImplemented();
        MarkStreamingImplemented();
        MarkMidiAndSequencingImplemented();
        MarkIntegrationImplemented();
    }

    /// <summary>Every listed feature, in the order the class declares them.</summary>
    public static IReadOnlyList<DecentSamplerFeature> Features => AllFeatures;

    /// <summary>Every element name the parser recognises.</summary>
    public static IReadOnlyCollection<string> ElementNames => ElementNameSet;

    /// <summary>Every value of a binding's <c>parameter</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> BindingParameterNames => BindingParameterSet;

    /// <summary>Every value of a binding's <c>type</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> BindingTypeNames => BindingTypeSet;

    /// <summary>Every value of a binding's <c>level</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> BindingLevelNames => BindingLevelSet;

    /// <summary>Every value of an effect's <c>type</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> EffectTypeNames => EffectTypeSet;

    /// <summary>Every element name that may appear beneath <c>&lt;modulators&gt;</c>.</summary>
    public static IReadOnlyCollection<string> ModulatorTypeNames => ModulatorTypeSet;

    /// <summary>Every value of an oscillator's <c>waveform</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> WaveformNames => WaveformSet;

    /// <summary>Every value of a binding's <c>translation</c> attribute the engine knows.</summary>
    public static IReadOnlyCollection<string> TranslationModeNames => TranslationModeSet;

    /// <summary>Whether the parser recognises an element name.</summary>
    /// <param name="elementName">The element name, as the format spells it.</param>
    /// <returns><see langword="true"/> when the element is known.</returns>
    public static bool IsElement(string elementName) =>
        elementName != null && ElementNameSet.Contains(elementName);

    /// <summary>Whether the parser recognises an attribute on a given element.</summary>
    /// <param name="elementName">The element the attribute is written on.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <returns><see langword="true"/> when the attribute is known on that element.</returns>
    public static bool IsAttribute(string elementName, string attributeName)
    {
        if (elementName == null || attributeName == null)
        {
            return false;
        }

        return AttributesByElement.TryGetValue(elementName, out var attributes) &&
               attributes.Contains(CanonicalAttributeName(attributeName));
    }

    /// <summary>Every attribute the parser recognises on one element, in canonical form.</summary>
    /// <param name="elementName">The element name.</param>
    /// <returns>The attribute names. Empty when the element is not known.</returns>
    public static IReadOnlyCollection<string> AttributeNames(string elementName) =>
        elementName != null && AttributesByElement.TryGetValue(elementName, out var attributes)
            ? attributes
            : [];

    /// <summary>Whether the engine knows a binding <c>parameter</c> token. Case-insensitive.</summary>
    /// <param name="parameter">The parameter token, such as <c>AMP_VOLUME</c>.</param>
    /// <returns><see langword="true"/> when the parameter is known.</returns>
    public static bool IsBindingParameter(string parameter) =>
        parameter != null && BindingParameterSet.Contains(parameter);

    /// <summary>Whether the engine knows a binding <c>type</c>. Case-insensitive.</summary>
    /// <param name="type">The binding type, such as <c>effect</c>.</param>
    /// <returns><see langword="true"/> when the type is known.</returns>
    public static bool IsBindingType(string type) => type != null && BindingTypeSet.Contains(type);

    /// <summary>Whether the engine knows a binding <c>level</c>. Case-insensitive.</summary>
    /// <param name="level">The binding level, such as <c>group</c>.</param>
    /// <returns><see langword="true"/> when the level is known.</returns>
    public static bool IsBindingLevel(string level) => level != null && BindingLevelSet.Contains(level);

    /// <summary>Whether the engine knows an effect <c>type</c>. Case-insensitive.</summary>
    /// <param name="type">The effect type, such as <c>reverb</c>.</param>
    /// <returns><see langword="true"/> when the type is known.</returns>
    public static bool IsEffectType(string type) => type != null && EffectTypeSet.Contains(type);

    /// <summary>Whether an element name is one of the seven modulator elements.</summary>
    /// <param name="elementName">The element name, such as <c>lfo</c>.</param>
    /// <returns><see langword="true"/> when the element is a modulator.</returns>
    public static bool IsModulatorType(string elementName) =>
        elementName != null && ModulatorTypeSet.Contains(elementName);

    /// <summary>Whether the engine knows an oscillator <c>waveform</c>. Case-insensitive.</summary>
    /// <param name="waveform">The waveform name, such as <c>pluck1</c>.</param>
    /// <returns><see langword="true"/> when the waveform is known.</returns>
    public static bool IsWaveform(string waveform) => waveform != null && WaveformSet.Contains(waveform);

    /// <summary>Whether the engine knows a binding <c>translation</c> mode. Case-insensitive.</summary>
    /// <param name="translation">The translation mode, such as <c>table</c>.</param>
    /// <returns><see langword="true"/> when the mode is known.</returns>
    public static bool IsTranslationMode(string translation) =>
        translation != null && TranslationModeSet.Contains(translation);

    /// <summary>Whether the engine knows a value of some other enumerated attribute.</summary>
    /// <param name="attributeName">The attribute the value belongs to, such as <c>seqMode</c>.</param>
    /// <param name="value">The value, such as <c>round_robin</c>.</param>
    /// <returns><see langword="true"/> when the value is known for that attribute.</returns>
    public static bool IsEnumerationValue(string attributeName, string value) =>
        attributeName != null && value != null &&
        EnumerationValues.TryGetValue(attributeName, out var values) && values.Contains(value);

    /// <summary>Every value the engine knows for one enumerated attribute.</summary>
    /// <param name="attributeName">The attribute name, such as <c>glideMode</c>.</param>
    /// <returns>The values. Empty when the attribute is not enumerated.</returns>
    public static IReadOnlyCollection<string> EnumerationValueNames(string attributeName) =>
        attributeName != null && EnumerationValues.TryGetValue(attributeName, out var values) ? values : [];

    /// <summary>How far the engine has taken one feature, as the process-wide registry stands now.</summary>
    /// <param name="category">The kind of feature.</param>
    /// <param name="owner">
    /// What it belongs to - the element name for an attribute, the attribute name for an enumeration
    /// value - or an empty string when it stands alone.
    /// </param>
    /// <param name="name">The feature's own name.</param>
    /// <returns>The status, or null when the feature is not listed.</returns>
    /// <remarks>
    /// Two categories - <see cref="DecentSamplerFeatureCategory.Waveform"/> and
    /// <see cref="DecentSamplerFeatureCategory.EffectType"/> - can be brought to life by an ADD-ON, so
    /// their answer depends on what is registered: see the overload that takes a registry.
    /// </remarks>
    public static DecentSamplerFeatureStatus? StatusOf(
        DecentSamplerFeatureCategory category, string owner, string name) =>
        StatusOf(category, owner, name, DecentSamplerExtensions.Shared);

    /// <summary>How far the engine has taken one feature, given a particular set of extensions.</summary>
    /// <param name="category">The kind of feature.</param>
    /// <param name="owner">
    /// What it belongs to - the element name for an attribute, the attribute name for an enumeration
    /// value - or an empty string when it stands alone.
    /// </param>
    /// <param name="name">The feature's own name.</param>
    /// <param name="registry">
    /// The extension registry to answer against - a synthesizer's own, or null to ignore registration
    /// entirely and report only what the core itself implements.
    /// </param>
    /// <returns>The status, or null when the feature is not listed.</returns>
    /// <remarks>
    /// <para>
    /// Every oscillator waveform in the format, and seven of its effect types, live in
    /// CodeBrix.Audio.ModestSynth rather than in the core, so whether they are IMPLEMENTED is a
    /// question about the running application and not about this assembly: they answer
    /// <see cref="DecentSamplerFeatureStatus.Parsed"/> while nothing supplies them and
    /// <see cref="DecentSamplerFeatureStatus.Implemented"/> once a registry has a factory for the name.
    /// Registering an add-on therefore changes this answer and changes nothing about
    /// <see cref="Features"/>, whose statuses describe the core alone.
    /// </para>
    /// </remarks>
    public static DecentSamplerFeatureStatus? StatusOf(
        DecentSamplerFeatureCategory category,
        string owner,
        string name,
        DecentSamplerExtensionRegistry registry)
    {
        if (name == null)
        {
            return null;
        }

        var key = KeyOf(category, owner ?? string.Empty, name);

        if (!StatusByKey.TryGetValue(key, out var status))
        {
            return null;
        }

        if (registry == null || status == DecentSamplerFeatureStatus.Implemented)
        {
            return status;
        }

        var supplied = category switch
        {
            DecentSamplerFeatureCategory.Waveform => registry.IsOscillatorRegistered(name),
            DecentSamplerFeatureCategory.EffectType => registry.IsEffectRegistered(name),
            _ => false,
        };

        return supplied ? DecentSamplerFeatureStatus.Implemented : status;
    }

    /// <summary>
    /// Folds a controller-indexed attribute name onto the feature it belongs to, so that
    /// <c>loCC64</c> and <c>loCC11</c> are both the feature <c>loCCN</c>. Every other name is returned
    /// unchanged.
    /// </summary>
    /// <param name="attributeName">The attribute name as written.</param>
    /// <returns>The canonical name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attributeName"/> is null.</exception>
    public static string CanonicalAttributeName(string attributeName)
    {
        if (attributeName == null)
        {
            throw new ArgumentNullException(nameof(attributeName));
        }

        var match = ControllerAttribute.Match(attributeName);
        return match.Success ? match.Groups[1].Value + "N" : attributeName;
    }

    /// <summary>
    /// Reads a controller-indexed attribute name, giving the family it belongs to and the controller
    /// number: <c>onLoCC64</c> gives <c>onLoCC</c> and 64.
    /// </summary>
    /// <param name="attributeName">The attribute name as written.</param>
    /// <param name="family">The name without its index, such as <c>loCC</c>.</param>
    /// <param name="controller">The controller number.</param>
    /// <returns><see langword="true"/> when the name is a controller-indexed attribute in range.</returns>
    public static bool TryReadControllerAttribute(string attributeName, out string family, out int controller)
    {
        family = null;
        controller = 0;

        if (attributeName == null)
        {
            return false;
        }

        var match = ControllerAttribute.Match(attributeName);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out controller) ||
            controller > 127)
        {
            return false;
        }

        family = match.Groups[1].Value;
        return true;
    }

    private static void Element(string name, params string[] attributes)
    {
        ElementNameSet.Add(name);
        Add(DecentSamplerFeatureCategory.Element, string.Empty, name);

        if (!AttributesByElement.TryGetValue(name, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            AttributesByElement[name] = set;
        }

        foreach (var attribute in attributes)
        {
            if (set.Add(attribute))
            {
                Add(DecentSamplerFeatureCategory.Attribute, name, attribute);
            }
        }
    }

    private static void AddBindingTypes()
    {
        string[] types =
        [
            "amp", "general", "effect", "control", "labeled_knob", "note", "note_binding",
            "velocity_binding", "cc_binding", "button_state_binding", "keyboard_color", "modulator",
            "note_sequence", "arpeggiator",
        ];

        foreach (var type in types)
        {
            BindingTypeSet.Add(type);
            Add(DecentSamplerFeatureCategory.BindingType, string.Empty, type,
                DecentSamplerFeatureStatus.Implemented);
        }
    }

    private static void AddBindingLevels()
    {
        // "groups" is not in the guide's list of levels, but six corpus presets write it and it can
        // only mean the <groups> element, which is what level="instrument" addresses.
        string[] levels =
            ["ui", "instrument", "groups", "group", "sample", "oscillator", "tag", "midi", "bus"];

        foreach (var level in levels)
        {
            BindingLevelSet.Add(level);
            Add(DecentSamplerFeatureCategory.BindingLevel, string.Empty, level,
                DecentSamplerFeatureStatus.Implemented);
        }
    }

    private static void AddBindingParameters()
    {
        var parameters = new List<string>
        {
            // Instrument and group
            "AMP_VOLUME", "GROUP_VOLUME", "GLOBAL_TUNING", "GROUP_TUNING", "TUNING", "PAN",
            "AMP_VEL_TRACK", "AMP_ENV_ENABLED", "ENABLED", "PITCH_KEY_TRACK",
            "ENV_ATTACK", "ENV_ATTACK_CURVE", "ENV_DECAY", "ENV_DECAY_CURVE", "ENV_SUSTAIN",
            "ENV_RELEASE", "ENV_RELEASE_CURVE", "GLIDE_TIME", "GLIDE_MODE",
            "SAMPLE_START", "SAMPLE_END", "LOOP_START", "LOOP_END",
            "LO_NOTE", "HI_NOTE", "LO_VEL", "HI_VEL", "ROOT_NOTE",
            "SILENCING_DECAY", "SILENCING_MODE", "ALL_NOTES_OFF",

            // Tags
            "TAG_ENABLED", "TAG_VOLUME", "TAG_POLYPHONY",

            // Buses
            "BUS_VOLUME",

            // Modulators
            "MOD_AMOUNT", "FREQUENCY", "SHAPE", "MOD_DELAY_TIME", "TRIGGER",

            // Note sequences
            "RATE", "SEQ_INDEX", "SEQ_LOOP_MODE", "SEQ_PLAYBACK_RATE", "SEQ_TRANSPOSE",
            "SEQ_TRANSPOSE_WITH_ROOT_NOTE", "SEQ_TRACK_MIDI_INPUT_VELOCITY",

            // Arpeggiator
            "ARP_ENABLED", "ARP_ORDER", "ARP_OCTAVE_RANGE", "ARP_OCTAVE_MODE", "ARP_STEP_COUNT",
            "ARP_GATE_LENGTH", "ARP_SYNC_DIVISION", "ARP_FOLLOW_GLOBAL_TEMPO", "ARP_RATE_MULTIPLIER",
            "ARP_OVERRIDE_BPM",

            // User interface
            "BG_IMAGE", "VISIBLE", "VALUE", "TEXT", "MIN_VALUE", "MAX_VALUE", "VALUE_TYPE",
            "PATH", "OPACITY", "FRAME_RATE", "CURRENT_FRAME", "PLAYBACK_MODE",
            "X_VALUE", "Y_VALUE", "X", "Y", "WIDTH", "HEIGHT", "X1", "Y1", "X2", "Y2",
            "TEXT_COLOR", "BACKGROUND_COLOR", "HIGHLIGHTED_TEXT_COLOR", "HIGHLIGHTED_BACKGROUND_COLOR",

            // Effects
            "FX_MIX", "FX_IR_FILE", "FX_FILTER_FREQUENCY", "FX_FILTER_Q", "FX_FILTER_GAIN",
            "FX_FILTER_RESONANCE", "FX_REVERB_WET_LEVEL", "FX_REVERB_ROOM_SIZE", "FX_REVERB_DAMPING",
            "FX_MOD_DEPTH", "FX_MOD_RATE", "FX_CENTER_FREQUENCY", "FX_FEEDBACK", "FX_DELAY_TIME",
            "FX_DELAY_TIME_FORMAT", "FX_STEREO_OFFSET", "FX_WET_LEVEL", "LEVEL", "FX_PITCH_SHIFT",
            "FX_DRIVE", "FX_THRESHOLD", "FX_OUTPUT_LEVEL", "FX_DRIVE_BOOST", "FX_BIT_DEPTH",
            "FX_SAMPLE_RATE_REDUCTION", "FX_GATE_AMOUNT", "FX_WIDTH", "FX_RATIO", "FX_ATTACK",
            "FX_RELEASE", "FX_INPUT_GAIN", "FX_OUTPUT_GAIN",

            // Oscillators
            "OSCILLATOR_WAVEFORM", "OSCILLATOR_DAMPING", "OSCILLATOR_PLUCK_TYPE",
            "OSCILLATOR_WAVETABLE_POSITION", "OSCILLATOR_WAVETABLE_FRAME_INTERPOLATION",
            "OSCILLATOR_HARMONIC_NUM_PARTIALS", "OSCILLATOR_HARMONIC_TILT",
            "OSCILLATOR_HARMONIC_ODD_EVEN_BALANCE", "OSCILLATOR_HARMONIC_NORMALIZATION",
            "OSCILLATOR_FM_ALGORITHM",
        };

        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            parameters.Add("OUTPUT_" + index + "_TARGET");
            parameters.Add("OUTPUT_" + index + "_VOLUME");
        }

        for (var partial = 1; partial <= 64; partial++)
        {
            parameters.Add("OSCILLATOR_HARMONIC_PARTIAL_" +
                           partial.ToString(CultureInfo.InvariantCulture) + "_LEVEL");
        }

        string[] fmSuffixes =
        [
            "RATIO", "DETUNE", "MODE", "FIXED_FREQ", "LEVEL", "VELOCITY_SENSITIVITY", "FEEDBACK",
            "ATTACK", "DECAY", "SUSTAIN", "RELEASE", "EG_TYPE",
            "EG_RATE1", "EG_RATE2", "EG_RATE3", "EG_RATE4",
            "EG_LEVEL1", "EG_LEVEL2", "EG_LEVEL3", "EG_LEVEL4",
        ];

        for (var op = 1; op <= 6; op++)
        {
            foreach (var suffix in fmSuffixes)
            {
                parameters.Add("OSCILLATOR_FM_OP" + op.ToString(CultureInfo.InvariantCulture) + "_" + suffix);
            }
        }

        foreach (var parameter in parameters)
        {
            if (BindingParameterSet.Add(parameter))
            {
                Add(DecentSamplerFeatureCategory.BindingParameter, string.Empty, parameter,
                    DecentSamplerFeatureStatus.Implemented);
            }
        }
    }

    private static void AddEffectTypes()
    {
        string[] types =
        [
            "lowpass", "lowpass_4pl", "lowpass_1pl", "bandpass", "highpass", "notch", "peak", "gain",
            "reverb", "delay", "chorus", "phaser", "convolution", "pitch_shift", "wave_folder",
            "wave_shaper", "stereo_simulator", "bit_crusher", "compressor", "gate",
        ];

        foreach (var type in types)
        {
            EffectTypeSet.Add(type);
            Add(DecentSamplerFeatureCategory.EffectType, string.Empty, type);
        }
    }

    private static void AddModulatorTypes()
    {
        string[] types = ["lfo", "envelope", "midiCC", "midiVelocity", "mpeTimbre", "mpePressure", "random"];

        foreach (var type in types)
        {
            ModulatorTypeSet.Add(type);
            Add(DecentSamplerFeatureCategory.ModulatorType, string.Empty, type);
        }
    }

    private static void AddWaveforms()
    {
        string[] waveforms =
        [
            "sine", "saw", "square", "triangle", "noise", "white_noise", "pluck1", "wavetable",
            "harmonic", "fm6op",
        ];

        // "formant" is DELIBERATELY absent. The 1.30 release notes add it, the 1.29 guide this list is
        // built from does not document it, and the parser must go on reporting it so that a preset
        // using it says so rather than sounding wrong in silence. MEASURED (round 2, item 26): it takes
        // no attributes at all and sounds one fixed formant region over the note's fundamental; the
        // add-on registers the name and serves it with a sine, which is a published divergence.
        // DecentSamplerResidualTable's add-on entry says so.

        foreach (var waveform in waveforms)
        {
            WaveformSet.Add(waveform);
            Add(DecentSamplerFeatureCategory.Waveform, string.Empty, waveform);
        }
    }

    private static void AddTranslationModes()
    {
        string[] modes = ["linear", "table", "fixed_value"];

        foreach (var mode in modes)
        {
            TranslationModeSet.Add(mode);
            Add(DecentSamplerFeatureCategory.TranslationMode, string.Empty, mode,
                DecentSamplerFeatureStatus.Implemented);
        }
    }

    private static void AddEnumerationValues()
    {
        Enumeration("glideMode", "always", "legato", "off");
        Enumeration("trigger", "attack", "release", "first", "legato", "continuous", "none");
        Enumeration("seqMode", "always", "random", "true_random", "round_robin");
        Enumeration("silencingMode", "fast", "normal");
        Enumeration("playbackMode", "memory", "disk_streaming", "auto",
            "forward_loop", "forward_once", "reverse_loop", "reverse_once", "ping_pong_loop", "stopped");
        Enumeration("delayUnit", "seconds", "beats", "samples");
        Enumeration("retriggerIntervalUnit", "seconds", "beats", "samples");
        Enumeration("loopCrossfadeMode", "linear", "equal_power");
        Enumeration("levelUnit", "decibels", "linear");
        Enumeration("delayTimeFormat", "seconds", "musical_time");
        Enumeration("algorithm", "lauridsen", "schroeder", "adt");
        Enumeration("scope", "global", "voice");
        Enumeration("modBehavior", "add", "modulate", "multiply", "set");
        Enumeration("shape", "sine", "square", "saw", "triangle");
        Enumeration("frequencyFormat", "hz", "musical_time");
        Enumeration("mode", "note_on", "periodic", "ratio", "fixed");
        Enumeration("channel", "voice");
        Enumeration("eventType", "note_on", "note_off", "any");
        Enumeration("arpOrder", "up", "down", "up_down", "up_down_inclusive", "down_up",
            "down_up_inclusive", "as_played", "random", "random_no_repeat");
        Enumeration("arpOctaveMode", "replayPerOctave", "interleaveByPitch");
        Enumeration("arpSyncDivision",
            "noteOneSixtyFourthTriplet", "noteOneSixtyFourth", "noteOneThirtySecondTriplet",
            "noteOneSixtyFourthDotted", "noteOneThirtySecond", "noteOneSixteenthTriplet",
            "noteOneThirtySecondDotted", "noteOneSixteenth", "noteOneEighthTriplet",
            "noteOneSixteenthDotted", "noteOneEighth", "noteOneFourthTriplet", "noteOneEighthDotted",
            "noteOneFourth", "noteOneHalfTriplet", "noteOneFourthDotted", "noteOneHalf",
            "noteOneTriplet", "noteOneHalfDotted", "noteWhole", "noteWholeDotted");
        Enumeration("valueType", "float", "integer", "multi_state", "musical_time", "percent");
        Enumeration("type", "float", "integer", "multi_state", "musical_time", "percent");
        Enumeration("style", "text", "image", "linear_bar", "linear_bar_vertical", "linear_horizontal",
            "linear_vertical", "rotary", "rotary_horizontal_drag", "rotary_horizontal_vertical_drag",
            "rotary_vertical_drag", "custom_skin_vertical_drag", "custom_skin_horizontal_drag",
            "custom_skin_horizontal_vertical_drag");
        Enumeration("snapMode", "none", "whole_numbers", "tenths", "hundredths", "thousandths",
            "stop_points");
        Enumeration("aspectRatioMode", "preserve", "stretch");
        Enumeration("sourceFormat", "horizontal_image_strip", "vertical_image_strip");
        Enumeration("customSkinImageOrientation", "horizontal", "vertical");
        Enumeration("vAlign", "top", "center", "bottom");
        Enumeration("hAlign", "left", "center", "right");
        Enumeration("orientation", "horizontal", "vertical_up", "vertical_down");
        Enumeration("seqTriggerBehavior", "on", "off", "midi_key");
        Enumeration("seqLoopMode", "forward", "reverse", "random", "random_no_repeat", "no_loop");
        Enumeration("fmOpNMode", "ratio", "fixed");
        Enumeration("fmOpNEgType", "adsr", "dx7");

        var targets = new List<string> { "MAIN_OUTPUT", "NO_OUTPUT" };
        for (var bus = 1; bus <= 16; bus++)
        {
            targets.Add("BUS_" + bus.ToString(CultureInfo.InvariantCulture));
        }

        for (var aux = 1; aux <= 16; aux++)
        {
            targets.Add("AUX_STEREO_OUTPUT_" + aux.ToString(CultureInfo.InvariantCulture));
        }

        Enumeration("outputNTarget", [.. targets]);
    }

    private static void Enumeration(string attributeName, params string[] values)
    {
        if (!EnumerationValues.TryGetValue(attributeName, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EnumerationValues[attributeName] = set;
        }

        foreach (var value in values)
        {
            if (set.Add(value))
            {
                Add(DecentSamplerFeatureCategory.EnumerationValue, attributeName, value);
            }
        }
    }

    private static void Add(DecentSamplerFeatureCategory category, string owner, string name) =>
        Add(category, owner, name, DecentSamplerFeatureStatus.Parsed);

    private static void Add(
        DecentSamplerFeatureCategory category,
        string owner,
        string name,
        DecentSamplerFeatureStatus status)
    {
        // A feature is Parsed until the phase that makes it do something flips it to Implemented. The
        // binding engine flipped the binding types, levels, parameters and translation modes; the
        // sound-producing elements and attributes follow as the voice runtime and the effects land.
        AllFeatures.Add(new DecentSamplerFeature(category, owner, name, status));
        StatusByKey[KeyOf(category, owner, name)] = status;
    }

    // WHAT THE ENGINE ACTUALLY HONOURS TODAY.
    //
    // Everything in this class starts out Parsed. A phase that makes a feature audible flips it here,
    // so the class stays the single source of truth and the claim can never drift from the code. Each
    // phase adds its own method rather than editing another's, which is what keeps parallel work
    // mergeable.
    private static void MarkSamplerEngineImplemented()
    {
        // The sampler engine: groups, zones, voices, triggers, round robins, tags, muting, legato,
        // glide, loops, envelopes, controller filters, release triggers, delays and retriggers.
        string[] soundAttributes =
        [
            "volume", "pan", "tuning", "groupTuning", "globalTuning", "pitchKeyTrack",
            "glideTime", "glideMode", "ampVelTrack", "trigger", "releaseTriggerDecay", "tags",
            "silencedByTags", "silencingMode", "silencingDecay",
            "seqMode", "seqLength", "seqPosition",
            "loNote", "hiNote", "loVel", "hiVel",
            "loCCN", "hiCCN", "onLoCCN", "onHiCCN",
            "delay", "delayUnit",
            "retriggerEnabled", "retriggerInterval", "retriggerIntervalUnit",
            "start", "end", "loopStart", "loopEnd", "loopCrossfade", "loopCrossfadeMode", "loopEnabled",
            "ampEnvEnabled", "attack", "decay", "sustain", "release",
            "attackCurve", "decayCurve", "releaseCurve",
        ];

        var routing = new List<string>();
        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            routing.Add("output" + index + "Target");
            routing.Add("output" + index + "Volume");
        }

        MarkElement("groups", [.. soundAttributes, .. routing]);
        MarkElement("group", [.. soundAttributes, .. routing, "name", "enabled"]);
        MarkElement("sample",
            [.. soundAttributes, .. routing,
             "path", "rootNote", "previousNotes", "previousNote", "legatoInterval"]);

        MarkElement("tags");
        MarkElement("tag", "enabled", "volume", "polyphony");

        MarkEnumeration("trigger", "attack", "release", "first", "legato", "continuous");
        MarkEnumeration("seqMode", "always", "random", "true_random", "round_robin");
        MarkEnumeration("glideMode", "always", "legato", "off");
        MarkEnumeration("silencingMode", "fast", "normal");
        MarkEnumeration("loopCrossfadeMode", "linear", "equal_power");
        MarkEnumeration("delayUnit", "seconds", "beats", "samples");
        MarkEnumeration("retriggerIntervalUnit", "seconds", "beats", "samples");

        // Routing to the main mix and to nowhere is honoured now. BUS_n and AUX_STEREO_OUTPUT_n resolve
        // to their own output slots, but until the effects phase gives buses their chains and the
        // auxiliary pairs their own outputs, both are folded into the main mix - parsed, not honoured.
        MarkEnumeration("outputNTarget", "MAIN_OUTPUT", "NO_OUTPUT");
    }

    private static void MarkEffectsAndBusesImplemented()
    {
        // The effect chain framework at all three levels, the buses, and the routing that reaches them.
        MarkElement("effects");
        MarkElement("buses");

        var busAttributes = new List<string> { "busVolume" };
        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            busAttributes.Add("output" + index + "Target");
            busAttributes.Add("output" + index + "Volume");
        }

        MarkElement("bus", [.. busAttributes]);

        // Every <effect> attribute the CORE effect set acts on. The attributes that belong only to the
        // add-on's effects - pitchShift, drive, driveBoost, outputLevel, highQuality, algorithm, width,
        // bitDepth, sampleRateReduction, amount and centerFrequency - stay Parsed until the add-on
        // registers the types that read them.
        MarkElement(
            "effect",
            "type", "tags", "frequency", "resonance", "q", "gain", "level", "levelUnit",
            "roomSize", "damping", "wetLevel", "delayTime", "delayTimeFormat", "feedback",
            "stereoOffset", "mix", "modDepth", "modRate", "irFile",
            "threshold", "ratio", "attack", "release", "inputGain", "outputGain", "autoBypass");

        // The core effect types. The seven creative types stay Parsed here: the core BYPASSES them and
        // says so, and CodeBrix.Audio.ModestSynth is what makes them sound.
        foreach (var type in DecentSamplerCoreEffects.TypeNames)
        {
            MarkImplemented(DecentSamplerFeatureCategory.EffectType, string.Empty, type);
        }

        // Routing to a bus and to an auxiliary pair is honoured now: a bus runs its own chain and its
        // own volume, and an auxiliary pair is a separate output that a stereo consumer can fold in.
        var targets = new List<string>();
        for (var bus = 1; bus <= 16; bus++)
        {
            targets.Add("BUS_" + bus.ToString(CultureInfo.InvariantCulture));
            targets.Add("AUX_STEREO_OUTPUT_" + bus.ToString(CultureInfo.InvariantCulture));
        }

        MarkEnumeration("outputNTarget", [.. targets]);

        // The gain effect's own unit, and the delay's two time formats.
        MarkEnumeration("levelUnit", "decibels", "linear");
        MarkEnumeration("delayTimeFormat", "seconds", "musical_time");
    }

    // The modulator and MPE phase: every <modulators> element runs, every one of their attributes is
    // honoured, and every modulator parameter Appendix B lists is bindable.
    private static void MarkModulatorsImplemented()
    {
        foreach (var type in ModulatorTypeSet)
        {
            MarkImplemented(DecentSamplerFeatureCategory.ModulatorType, string.Empty, type);
        }

        MarkElement("modulators");
        MarkElement("lfo", "shape", "frequency", "rate", "frequencyFormat", "modAmount", "delayTime",
            "scope", "modBehavior", "trigger", "tags");
        MarkElement("envelope", "attack", "decay", "sustain", "release", "attackCurve", "decayCurve",
            "releaseCurve", "modAmount", "delayTime", "scope", "modBehavior", "tags");
        MarkElement("midiCC", "number", "channel", "modAmount", "scope", "modBehavior", "tags");
        MarkElement("midiVelocity", "modAmount", "scope", "modBehavior", "tags");
        MarkElement("mpeTimbre", "risingSmoothingTime", "fallingSmoothingTime", "modAmount", "scope",
            "modBehavior", "tags");
        MarkElement("mpePressure", "risingSmoothingTime", "fallingSmoothingTime", "modAmount", "scope",
            "modBehavior", "tags");
        MarkElement("random", "mode", "frequency", "trigger", "seed", "modAmount", "scope",
            "modBehavior", "tags");

        MarkEnumeration("scope", "global", "voice");
        MarkEnumeration("modBehavior", "add", "modulate", "multiply", "set");
        MarkEnumeration("shape", "sine", "square", "saw", "triangle");
        MarkEnumeration("frequencyFormat", "hz", "musical_time");
        MarkEnumeration("mode", "note_on", "periodic");
        MarkEnumeration("channel", "voice");
        MarkEnumeration("trigger", "none");
    }

    private static void MarkStreamingImplemented()
    {
        // playbackMode, at every level it may be written, and all three of its values. "memory" and
        // "disk_streaming" are obeyed outright; "auto" is decided by the load options' per-sample
        // threshold and per-instrument budget.
        MarkImplemented(DecentSamplerFeatureCategory.Attribute, "groups", "playbackMode");
        MarkImplemented(DecentSamplerFeatureCategory.Attribute, "group", "playbackMode");
        MarkImplemented(DecentSamplerFeatureCategory.Attribute, "sample", "playbackMode");

        MarkEnumeration("playbackMode", "memory", "disk_streaming", "auto");
    }

    // The <midi> element phase: the note path runs the handlers, the note sequencer plays sequences,
    // and the arpeggiator generates notes from whatever is held.
    private static void MarkMidiAndSequencingImplemented()
    {
        // The three handler kinds. The <note> element's own attribute list carries the SEQUENCE note's
        // position/velocity/length as well, because the feature list registers both <note> elements
        // under one owner name.
        MarkElement("midi");
        MarkElement("cc", "number");
        MarkElement("note", "note", "eventType", "enabled", "swallowNotes", "position", "velocity",
            "length");
        MarkElement("velocity");

        MarkElement("noteSequences");
        MarkElement("sequence", "name", "length", "rate");

        MarkElement("arpeggiator", "enabled", "arpOrder", "arpOctaveRange", "arpOctaveMode",
            "arpStepCount", "arpGateLength", "arpFollowGlobalTempo", "arpSyncDivision",
            "arpRateMultiplier", "arpOverrideBpm");

        MarkEnumeration("eventType", "note_on", "note_off", "any");
        MarkEnumeration("seqTriggerBehavior", "on", "off", "midi_key");
        MarkEnumeration("seqLoopMode", "forward", "reverse", "random", "random_no_repeat", "no_loop");
        MarkEnumeration("arpOrder", "up", "down", "up_down", "up_down_inclusive", "down_up",
            "down_up_inclusive", "as_played", "random", "random_no_repeat");
        MarkEnumeration("arpOctaveMode", "replayPerOctave", "interleaveByPitch");
        MarkEnumeration("arpSyncDivision",
            "noteOneSixtyFourthTriplet", "noteOneSixtyFourth", "noteOneThirtySecondTriplet",
            "noteOneSixtyFourthDotted", "noteOneThirtySecond", "noteOneSixteenthTriplet",
            "noteOneThirtySecondDotted", "noteOneSixteenth", "noteOneEighthTriplet",
            "noteOneSixteenthDotted", "noteOneEighth", "noteOneFourthTriplet", "noteOneEighthDotted",
            "noteOneFourth", "noteOneHalfTriplet", "noteOneFourthDotted", "noteOneHalf",
            "noteOneTriplet", "noteOneHalfDotted", "noteWhole", "noteWholeDotted");
    }

    // The integration and audit pass. Three groups of features the engine demonstrably acts on had
    // never been promoted, because the work that implemented them promoted a CATEGORY rather than the
    // element that carries it.
    private static void MarkIntegrationImplemented()
    {
        // <binding>: the binding engine resolves and honours every attribute of it. The binding TYPE,
        // LEVEL, PARAMETER and TRANSLATION categories were promoted when the engine landed; the
        // element and its own attributes were not.
        MarkElement(
            "binding",
            "type", "level", "position", "controlIndex", "groupIndex", "effectIndex", "modulatorIndex",
            "busIndex", "stateIndex", "bindingIndex", "midiElementIndex", "noteIndex", "colorIndex",
            "seqIndex", "tags", "groupTags", "sampleTags", "oscillatorTags", "effectTags",
            "modulatorTags", "controlTags", "identifier", "parameter", "enabled", "translation",
            "translationOutputMin", "translationOutputMax", "translationReversed", "translationTable",
            "translationValue", "triggerOnLoad", "modBehavior", "modAmount", "seqFollowGlobalTempo",
            "seqTriggerBehavior", "seqPlayerIdentifier", "seqTrackMidiInputVelocity", "seqTranspose",
            "seqTransposeWithRootNote", "seqPlaybackRate", "seqLoopMode");

        // <oscillator>: an oscillator ZONE is played by the same voice runtime a sample zone is, so
        // every inheritable attribute that shapes a voice rather than a file is honoured on it. The
        // file-shaped ones (start, end, the loop attributes, playbackMode) are inherited and have
        // nothing to act on, and the generation attributes belong to the add-on package; both stay
        // Parsed and are named in DecentSamplerResidualTable.
        MarkElement(
            "oscillator",
            "volume", "pan", "tuning", "groupTuning", "globalTuning", "pitchKeyTrack", "rootNote",
            "glideTime", "glideMode", "ampVelTrack", "trigger", "releaseTriggerDecay", "tags",
            "silencedByTags", "silencingMode", "silencingDecay",
            "seqMode", "seqLength", "seqPosition",
            "loNote", "hiNote", "loVel", "hiVel",
            "loCCN", "hiCCN", "onLoCCN", "onHiCCN",
            "delay", "delayUnit",
            "retriggerEnabled", "retriggerInterval", "retriggerIntervalUnit",
            "ampEnvEnabled", "attack", "decay", "sustain", "release",
            "attackCurve", "decayCurve", "releaseCurve");

        for (var output = 1; output <= 8; output++)
        {
            var index = output.ToString(CultureInfo.InvariantCulture);
            MarkImplemented(DecentSamplerFeatureCategory.Attribute, "oscillator", "output" + index + "Target");
            MarkImplemented(DecentSamplerFeatureCategory.Attribute, "oscillator", "output" + index + "Volume");
        }

        // A <tag>'s name is what every tag rule matches on.
        MarkImplemented(DecentSamplerFeatureCategory.Attribute, "tag", "name");
    }

    private static void MarkElement(string elementName, params string[] attributes)
    {
        MarkImplemented(DecentSamplerFeatureCategory.Element, string.Empty, elementName);

        foreach (var attribute in attributes)
        {
            MarkImplemented(DecentSamplerFeatureCategory.Attribute, elementName, attribute);
        }
    }

    private static void MarkEnumeration(string attributeName, params string[] values)
    {
        foreach (var value in values)
        {
            MarkImplemented(DecentSamplerFeatureCategory.EnumerationValue, attributeName, value);
        }
    }

    private static void MarkImplemented(DecentSamplerFeatureCategory category, string owner, string name)
    {
        var key = KeyOf(category, owner, name);

        if (!StatusByKey.ContainsKey(key))
        {
            // A phase can only promote something the list already declares; anything else is a typo.
            throw new InvalidOperationException("Unknown Decent Sampler feature: " + key);
        }

        StatusByKey[key] = DecentSamplerFeatureStatus.Implemented;

        foreach (var feature in AllFeatures)
        {
            if (feature.Category == category && feature.Owner == owner && feature.Name == name)
            {
                feature.Status = DecentSamplerFeatureStatus.Implemented;
            }
        }
    }

    private static string KeyOf(DecentSamplerFeatureCategory category, string owner, string name) =>
        ((int)category).ToString(CultureInfo.InvariantCulture) + "|" + owner + "|" + name;
}

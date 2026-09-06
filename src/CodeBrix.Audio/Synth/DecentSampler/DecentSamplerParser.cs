using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CodeBrix.Audio.Synth.DecentSampler.Internal;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Reads a <c>.dspreset</c> document into a <see cref="DecentSamplerPreset"/>.
/// </summary>
/// <remarks>
/// <para>
/// The parser never rejects a preset over its content. An element it does not know, an attribute it
/// does not know, a value it cannot read: each is recorded in the preset's problem list, kept verbatim
/// on the model, and parsing carries on. The single hard failure is XML that is not well-formed, which
/// raises a <see cref="DecentSamplerParseException"/> carrying the position the reader stopped at.
/// </para>
/// <para>
/// No file beside the preset is opened here - not a sample, not an image, not the library sidecar.
/// That is <see cref="DecentSamplerInstrument"/>'s job.
/// </para>
/// </remarks>
public static class DecentSamplerParser
{
    private static readonly Regex OutputAttribute =
        new("^output([1-8])(Target|Volume)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HarmonicPartialAttribute =
        new("^harmonicPartial([0-9]{1,2})Level$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FmOperatorAttribute =
        new("^fmOp([1-6])(Ratio|Detune|Mode|FixedFreq|Level|VelocitySensitivity|Feedback|Attack|Decay|" +
            "Sustain|Release|EgType|EgRate[1-4]|EgLevel[1-4])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads a preset from a file.</summary>
    /// <param name="path">The <c>.dspreset</c> file.</param>
    /// <returns>The parsed preset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DecentSamplerParseException">The file is not well-formed XML.</exception>
    public static DecentSamplerPreset ParseFile(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The preset file does not exist.", path);
        }

        using (var stream = File.OpenRead(path))
        {
            return Parse(stream, path);
        }
    }

    /// <summary>Reads a preset from a stream.</summary>
    /// <param name="stream">The stream, positioned at the start of the document.</param>
    /// <param name="path">
    /// The path the stream came from, used for problem messages and for resolving relative paths later.
    /// Optional.
    /// </param>
    /// <returns>The parsed preset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="DecentSamplerParseException">The stream is not well-formed XML.</exception>
    public static DecentSamplerPreset Parse(Stream stream, string path = null)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        XDocument document;
        try
        {
            document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new DecentSamplerParseException(
                BuildParseMessage(path, exception), path, exception.LineNumber, exception.LinePosition, exception);
        }

        return Parse(document, path);
    }

    /// <summary>Reads a preset from XML text.</summary>
    /// <param name="xml">The document text.</param>
    /// <param name="path">
    /// The path the text came from, used for problem messages and for resolving relative paths later.
    /// Optional.
    /// </param>
    /// <returns>The parsed preset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is null.</exception>
    /// <exception cref="DecentSamplerParseException">The text is not well-formed XML.</exception>
    public static DecentSamplerPreset ParseText(string xml, string path = null)
    {
        if (xml == null)
        {
            throw new ArgumentNullException(nameof(xml));
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new DecentSamplerParseException(
                BuildParseMessage(path, exception), path, exception.LineNumber, exception.LinePosition, exception);
        }

        return Parse(document, path);
    }

    private static string BuildParseMessage(string path, XmlException exception)
    {
        var where = path == null ? "the preset" : "'" + path + "'";
        return $"{where} is not well-formed XML at line {exception.LineNumber.ToString(CultureInfo.InvariantCulture)}, " +
               $"position {exception.LinePosition.ToString(CultureInfo.InvariantCulture)}: {exception.Message}";
    }

    private static DecentSamplerPreset Parse(XDocument document, string path)
    {
        var preset = new DecentSamplerPreset
        {
            Path = path,
            Name = path == null ? "dspreset" : System.IO.Path.GetFileNameWithoutExtension(path),
        };

        var context = new DecentSamplerParseContext { SourceName = preset.Name };
        var root = document.Root;

        if (root == null)
        {
            context.Report("the document is empty");
            preset.AddProblems(context.Problems);
            return preset;
        }

        if (root.Name.LocalName != "DecentSampler")
        {
            context.Report($"the root element is <{root.Name.LocalName}>, not <DecentSampler>; " +
                           "it is read as one anyway");
        }

        var reader = new DecentSamplerElementReader(root, preset, context);
        preset.ElementName = "DecentSampler";
        preset.MinVersion = reader.Trimmed("minVersion");
        preset.PluginVersion = reader.Trimmed("pluginVersion");
        reader.Finish();

        foreach (var child in root.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "groups":
                    preset.Groups = preset.AddChild(ParseGroups(child, context));
                    break;
                case "effects":
                    preset.Effects = preset.AddChild(ParseEffects(child, context));
                    break;
                case "buses":
                    preset.Buses = preset.AddChild(ParseBuses(child, context));
                    break;
                case "midi":
                    preset.Midi = preset.AddChild(ParseMidi(child, context));
                    break;
                case "modulators":
                    preset.Modulators = preset.AddChild(ParseModulators(child, context));
                    break;
                case "noteSequences":
                    preset.NoteSequences = preset.AddChild(ParseNoteSequences(child, context));
                    break;
                case "arpeggiator":
                    preset.Arpeggiator = preset.AddChild(ParseArpeggiator(child, context));
                    break;
                case "tags":
                    preset.Tags = preset.AddChild(ParseTags(child, context));
                    break;
                case "ui":
                    preset.Ui = preset.AddChild(ParseUi(child, context));
                    break;
                default:
                    Unknown(preset, child, context);
                    break;
            }
        }

        preset.AddProblems(context.Problems);
        return preset;
    }

    private static void Unknown(DecentSamplerElement parent, XElement child, DecentSamplerParseContext context)
    {
        var lineNumber = DecentSamplerElementReader.LineNumberOf(child);
        parent.AddUnknownElement(new DecentSamplerUnknownElement(
            parent.ElementName, child.Name.LocalName, child.ToString(SaveOptions.DisableFormatting), lineNumber));
        context.ReportUnknownElement(parent.ElementName, child.Name.LocalName, lineNumber);
    }

    // ---------------------------------------------------------------- groups, samples, oscillators

    private static DecentSamplerGroupsElement ParseGroups(XElement element, DecentSamplerParseContext context)
    {
        var groups = new DecentSamplerGroupsElement();
        var reader = new DecentSamplerElementReader(element, groups, context);
        ReadSoundAttributes(reader, groups, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "group")
            {
                groups.Add(ParseGroup(child, context));
            }
            else
            {
                Unknown(groups, child, context);
            }
        }

        return groups;
    }

    private static DecentSamplerGroupElement ParseGroup(XElement element, DecentSamplerParseContext context)
    {
        var group = new DecentSamplerGroupElement();
        var reader = new DecentSamplerElementReader(element, group, context);
        group.Name = reader.Text("name");
        group.Enabled = reader.Bool("enabled");
        ReadSoundAttributes(reader, group, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "sample":
                    group.Add(ParseSample(child, context));
                    break;
                case "oscillator":
                    group.Add(ParseOscillator(child, context));
                    break;
                case "effects":
                    group.Effects = group.AddChild(ParseEffects(child, context));
                    break;
                default:
                    Unknown(group, child, context);
                    break;
            }
        }

        return group;
    }

    private static DecentSamplerSampleElement ParseSample(XElement element, DecentSamplerParseContext context)
    {
        var sample = new DecentSamplerSampleElement();
        var reader = new DecentSamplerElementReader(element, sample, context);

        sample.Path = reader.Trimmed("path");
        sample.RootNote = reader.Note("rootNote");
        sample.LegatoInterval = reader.Int("legatoInterval");
        sample.Length = reader.Long("length");

        // "previousNote" is the singular spelling the guide's own prose uses; both are read so a
        // preset written either way keeps its rule.
        var pluralPreviousNotes = reader.Text("previousNotes");
        var singularPreviousNote = reader.Text("previousNote");
        var previousNotes = pluralPreviousNotes ?? singularPreviousNote;
        if (previousNotes != null)
        {
            if (DecentSamplerValues.TryNoteList(previousNotes, out var notes))
            {
                sample.PreviousNotes = notes;
            }
            else
            {
                reader.Bad("previousNotes", previousNotes, "a comma-separated list of MIDI notes");
            }
        }

        ReadSoundAttributes(reader, sample, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            Unknown(sample, child, context);
        }

        return sample;
    }

    private static DecentSamplerOscillatorElement ParseOscillator(
        XElement element, DecentSamplerParseContext context)
    {
        var oscillator = new DecentSamplerOscillatorElement();
        var reader = new DecentSamplerElementReader(element, oscillator, context);

        // The guide's FM examples write the older "shape" spelling of "waveform" on the element.
        if (element.Attribute("waveform") == null && element.Attribute("shape") != null)
        {
            ReadWaveform(reader, oscillator, "shape");
        }

        ReadSoundAttributes(reader, oscillator, context);
        reader.Consume("rootNote");
        reader.Consume("shape");
        reader.Finish();

        foreach (var child in element.Elements())
        {
            Unknown(oscillator, child, context);
        }

        return oscillator;
    }

    private static void ReadWaveform(
        DecentSamplerElementReader reader, DecentSamplerSoundElement element, string attributeName)
    {
        var text = reader.Text(attributeName);
        if (text == null)
        {
            return;
        }

        element.WaveformName = text.Trim();
        element.Waveform = DecentSamplerEnumNames.TryParse<DecentSamplerWaveform>(text, out var waveform)
            ? waveform
            : DecentSamplerWaveform.Unknown;

        if (element.Waveform == DecentSamplerWaveform.Unknown)
        {
            reader.Bad(attributeName, text, "a waveform this engine recognises");
        }
    }

    private static void ReadSoundAttributes(
        DecentSamplerElementReader reader, DecentSamplerSoundElement element, DecentSamplerParseContext context)
    {
        var volumeText = reader.Text("volume");
        if (volumeText != null)
        {
            if (DecentSamplerValues.TryVolume(volumeText, out var linear, out var wasDecibels))
            {
                element.Volume = linear;
                element.VolumeText = volumeText.Trim();
                element.VolumeInDecibels = wasDecibels;
            }
            else
            {
                // The reference player treats a volume it cannot read as silence, so honour that and
                // report it, rather than quietly leaving the level at unity.
                element.Volume = 0.0;
                element.VolumeText = volumeText.Trim();
                reader.Bad("volume", volumeText, "a linear volume or one with a dB suffix");
            }
        }

        element.Pan = reader.Double("pan");
        element.Tuning = reader.Double("tuning");
        element.GroupTuning = reader.Double("groupTuning");
        element.GlobalTuning = reader.Double("globalTuning");
        element.PitchKeyTrack = reader.Double("pitchKeyTrack");
        element.GlideTime = reader.Double("glideTime");
        element.GlideMode = reader.Enumeration<DecentSamplerGlideMode>("glideMode");
        element.AmpVelTrack = reader.Double("ampVelTrack");
        element.Trigger = reader.Enumeration<DecentSamplerTrigger>("trigger");

        var decayText = reader.Text("releaseTriggerDecay");
        if (decayText != null)
        {
            if (DecentSamplerValues.TryDecayRate(decayText, out var decay, out var inDecibels))
            {
                element.ReleaseTriggerDecay = decay;
                element.ReleaseTriggerDecayInDecibels = inDecibels;
            }
            else
            {
                reader.Bad("releaseTriggerDecay", decayText, "a linear factor or a decibels-per-second value");
            }
        }

        element.Tags = reader.Names("tags");
        element.SilencedByTags = reader.Names("silencedByTags");
        element.SilencingMode = reader.Enumeration<DecentSamplerSilencingMode>("silencingMode");
        element.SilencingDecay = reader.Double("silencingDecay");

        element.SeqMode = reader.Enumeration<DecentSamplerSeqMode>("seqMode");
        element.SeqLength = reader.Int("seqLength");
        element.SeqPosition = reader.Int("seqPosition");

        element.LoNote = reader.Note("loNote");
        element.HiNote = reader.Note("hiNote");
        element.LoVel = reader.Int("loVel");
        element.HiVel = reader.Int("hiVel");

        element.PlaybackMode = reader.Enumeration<DecentSamplerPlaybackMode>("playbackMode");
        element.Delay = reader.Double("delay");
        element.DelayUnit = reader.Enumeration<DecentSamplerTimeUnit>("delayUnit");
        element.RetriggerEnabled = reader.Bool("retriggerEnabled");
        element.RetriggerInterval = reader.Double("retriggerInterval");
        element.RetriggerIntervalUnit = reader.Enumeration<DecentSamplerTimeUnit>("retriggerIntervalUnit");

        element.Start = reader.Long("start");
        element.End = reader.Long("end");
        element.LoopStart = reader.Long("loopStart");
        element.LoopEnd = reader.Long("loopEnd");
        element.LoopCrossfade = reader.Long("loopCrossfade");
        element.LoopCrossfadeMode = reader.Enumeration<DecentSamplerLoopCrossfadeMode>("loopCrossfadeMode");
        element.LoopEnabled = reader.Bool("loopEnabled");

        element.AmpEnvEnabled = reader.Bool("ampEnvEnabled");
        element.Attack = reader.Double("attack");
        element.Decay = reader.Double("decay");
        element.Sustain = reader.Double("sustain");
        element.Release = reader.Double("release");
        element.AttackCurve = reader.Double("attackCurve");
        element.DecayCurve = reader.Double("decayCurve");
        element.ReleaseCurve = reader.Double("releaseCurve");

        ReadWaveform(reader, element, "waveform");
        element.Damping = reader.Double("damping");
        element.PluckType = reader.Double("pluckType");
        element.WavetableFile = reader.Trimmed("wavetableFile");
        element.WavetableFrameSize = reader.Int("wavetableFrameSize");
        element.WavetablePosition = reader.Double("wavetablePosition");
        element.RandomPhase = reader.Bool("randomPhase");
        element.WavetableFrameInterpolation = reader.Bool("wavetableFrameInterpolation");

        element.NumPartials = reader.Int("numPartials");
        element.HarmonicTilt = reader.Double("harmonicTilt");
        element.HarmonicOddEvenBalance = reader.Double("harmonicOddEvenBalance");
        element.HarmonicNormalization = reader.Double("harmonicNormalization");
        element.FmAlgorithm = reader.Int("fmAlgorithm");

        ReadIndexedSoundAttributes(reader, element, context);
    }

    private static void ReadIndexedSoundAttributes(
        DecentSamplerElementReader reader, DecentSamplerSoundElement element, DecentSamplerParseContext context)
    {
        var lowControllers = new Dictionary<int, int>();
        var highControllers = new Dictionary<int, int>();
        var lowTriggers = new Dictionary<int, int>();
        var highTriggers = new Dictionary<int, int>();

        foreach (var pair in reader.Remaining().ToList())
        {
            var name = pair.Key;
            var text = pair.Value;

            if (DecentSamplerSupportedFeatures.TryReadControllerAttribute(name, out var family, out var controller))
            {
                reader.Consume(name);

                if (!DecentSamplerValues.TryInt(text, out var value))
                {
                    reader.Bad(name, text, "a controller value");
                    continue;
                }

                switch (family)
                {
                    case "loCC":
                        lowControllers[controller] = value;
                        break;
                    case "hiCC":
                        highControllers[controller] = value;
                        break;
                    case "onLoCC":
                        lowTriggers[controller] = value;
                        break;
                    default:
                        highTriggers[controller] = value;
                        break;
                }

                continue;
            }

            var output = OutputAttribute.Match(name);
            if (output.Success)
            {
                reader.Consume(name);
                var index = int.Parse(output.Groups[1].Value, CultureInfo.InvariantCulture) - 1;

                if (output.Groups[2].Value == "Target")
                {
                    if (DecentSamplerEnumNames.TryParse<DecentSamplerOutputTarget>(text, out var target))
                    {
                        element.SetOutputTarget(index, target);
                    }
                    else
                    {
                        reader.Bad(name, text, "an output target this engine recognises");
                    }
                }
                else if (DecentSamplerValues.TryDouble(text, out var volume))
                {
                    element.SetOutputVolume(index, volume);
                }
                else
                {
                    reader.Bad(name, text, "a number");
                }

                continue;
            }

            var partial = HarmonicPartialAttribute.Match(name);
            if (partial.Success)
            {
                var number = int.Parse(partial.Groups[1].Value, CultureInfo.InvariantCulture);
                if (number >= 1 && number <= 64)
                {
                    reader.Consume(name);

                    if (DecentSamplerValues.TryDouble(text, out var level))
                    {
                        element.SetHarmonicPartialLevel(number - 1, level);
                    }
                    else
                    {
                        reader.Bad(name, text, "a number");
                    }

                    continue;
                }
            }

            var fm = FmOperatorAttribute.Match(name);
            if (fm.Success)
            {
                reader.Consume(name);
                var index = int.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                ReadFmOperatorAttribute(reader, element.EnsureFmOperator(index), fm.Groups[2].Value, name, text);
            }
        }

        AddCcRanges(element, context, reader, lowControllers, highControllers, "loCC", "hiCC", false);
        AddCcRanges(element, context, reader, lowTriggers, highTriggers, "onLoCC", "onHiCC", true);
    }

    private static void AddCcRanges(
        DecentSamplerSoundElement element,
        DecentSamplerParseContext context,
        DecentSamplerElementReader reader,
        Dictionary<int, int> lows,
        Dictionary<int, int> highs,
        string lowName,
        string highName,
        bool isTrigger)
    {
        foreach (var controller in lows.Keys.Union(highs.Keys).OrderBy(number => number))
        {
            var hasLow = lows.TryGetValue(controller, out var low);
            var hasHigh = highs.TryGetValue(controller, out var high);

            if (!hasLow || !hasHigh)
            {
                var missing = hasLow ? highName : lowName;
                context.Report(
                    $"{reader.ElementName}@{(hasLow ? lowName : highName)}" +
                    $"{controller.ToString(CultureInfo.InvariantCulture)} has no matching {missing}" +
                    $"{controller.ToString(CultureInfo.InvariantCulture)}; the missing half is treated as " +
                    $"the widest value (line {reader.LineNumber.ToString(CultureInfo.InvariantCulture)})");
            }

            var range = new DecentSamplerCcRange(controller, hasLow ? low : 0, hasHigh ? high : 127);

            if (isTrigger)
            {
                element.AddCcTrigger(range);
            }
            else
            {
                element.AddCcFilter(range);
            }
        }
    }

    private static void ReadFmOperatorAttribute(
        DecentSamplerElementReader reader,
        DecentSamplerFmOperator fmOperator,
        string suffix,
        string attributeName,
        string text)
    {
        switch (suffix)
        {
            case "Mode":
                if (DecentSamplerEnumNames.TryParse<DecentSamplerFmOperatorMode>(text, out var mode))
                {
                    fmOperator.Mode = mode;
                }
                else
                {
                    reader.Bad(attributeName, text, "ratio or fixed");
                }

                return;

            case "EgType":
                if (DecentSamplerEnumNames.TryParse<DecentSamplerFmEnvelopeType>(text, out var envelopeType))
                {
                    fmOperator.EnvelopeType = envelopeType;
                }
                else
                {
                    reader.Bad(attributeName, text, "adsr or dx7");
                }

                return;
        }

        if (!DecentSamplerValues.TryDouble(text, out var number))
        {
            reader.Bad(attributeName, text, "a number");
            return;
        }

        switch (suffix)
        {
            case "Ratio": fmOperator.Ratio = number; break;
            case "Detune": fmOperator.Detune = number; break;
            case "FixedFreq": fmOperator.FixedFrequency = number; break;
            case "Level": fmOperator.Level = number; break;
            case "VelocitySensitivity": fmOperator.VelocitySensitivity = number; break;
            case "Feedback": fmOperator.Feedback = number; break;
            case "Attack": fmOperator.Attack = number; break;
            case "Decay": fmOperator.Decay = number; break;
            case "Sustain": fmOperator.Sustain = number; break;
            case "Release": fmOperator.Release = number; break;
            default:
                var stage = suffix[^1] - '1';
                if (suffix.StartsWith("EgRate", StringComparison.Ordinal))
                {
                    fmOperator.EgRates[stage] = number;
                }
                else
                {
                    fmOperator.EgLevels[stage] = number;
                }

                break;
        }
    }

    // ---------------------------------------------------------------------------- effects and buses

    private static DecentSamplerEffectsElement ParseEffects(XElement element, DecentSamplerParseContext context)
    {
        var effects = new DecentSamplerEffectsElement();
        var reader = new DecentSamplerElementReader(element, effects, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "effect")
            {
                effects.Add(ParseEffect(child, context));
            }
            else
            {
                Unknown(effects, child, context);
            }
        }

        return effects;
    }

    private static DecentSamplerEffect ParseEffect(XElement element, DecentSamplerParseContext context)
    {
        var effect = new DecentSamplerEffect();
        var reader = new DecentSamplerElementReader(element, effect, context);

        effect.TypeName = reader.Trimmed("type");
        effect.EffectType =
            effect.TypeName != null &&
            DecentSamplerEnumNames.TryParse<DecentSamplerEffectType>(effect.TypeName, out var type)
                ? type
                : DecentSamplerEffectType.Unknown;

        if (effect.EffectType == DecentSamplerEffectType.Unknown)
        {
            context.Report($"effect type not recognised: '{effect.TypeName}' " +
                           $"(line {reader.LineNumber.ToString(CultureInfo.InvariantCulture)})");
        }

        effect.Tags = reader.Names("tags");

        effect.Frequency = reader.Double("frequency");
        effect.Resonance = reader.Double("resonance");
        effect.Q = reader.Double("q");
        effect.Gain = reader.Double("gain");
        effect.Level = reader.Double("level");
        effect.LevelUnit = reader.Enumeration<DecentSamplerGainLevelUnit>("levelUnit");
        effect.RoomSize = reader.Double("roomSize");
        effect.Damping = reader.Double("damping");
        effect.WetLevel = reader.Double("wetLevel");
        effect.DelayTime = reader.Double("delayTime");
        effect.DelayTimeFormat = reader.Enumeration<DecentSamplerDelayTimeFormat>("delayTimeFormat");
        effect.Feedback = reader.Double("feedback");
        effect.StereoOffset = reader.Double("stereoOffset");
        effect.Mix = reader.Double("mix");
        effect.ModDepth = reader.Double("modDepth");
        effect.ModRate = reader.Double("modRate");
        effect.CenterFrequency = reader.Double("centerFrequency");
        effect.IrFile = reader.Trimmed("irFile");
        effect.PitchShift = reader.Double("pitchShift");
        effect.Drive = reader.Double("drive");
        effect.Threshold = reader.Double("threshold");
        effect.DriveBoost = reader.Double("driveBoost");
        effect.OutputLevel = reader.Double("outputLevel");
        effect.HighQuality = reader.Bool("highQuality");
        effect.Algorithm = reader.Enumeration<DecentSamplerStereoSimulatorAlgorithm>("algorithm");
        effect.Width = reader.Double("width");
        effect.BitDepth = reader.Double("bitDepth");
        effect.SampleRateReduction = reader.Double("sampleRateReduction");
        effect.Amount = reader.Double("amount");
        effect.Ratio = reader.Double("ratio");
        effect.Attack = reader.Double("attack");
        effect.Release = reader.Double("release");
        effect.InputGain = reader.Double("inputGain");
        effect.OutputGain = reader.Double("outputGain");
        effect.AutoBypass = reader.Bool("autoBypass");
        effect.EnvelopeAmount = reader.Double("envelope_amount");
        effect.EnvelopeAttack = reader.Double("envelope_attack");
        effect.EnvelopeDecay = reader.Double("envelope_decay");
        effect.EnvelopeSustain = reader.Double("envelope_sustain");
        effect.EnvelopeRelease = reader.Double("envelope_release");

        // Two spellings the guide's own examples use but its tables do not define. They are known so
        // they are not reported as unknown, and kept in Values so nothing is lost.
        reader.Consume("wetDryMix");
        reader.Consume("shape");

        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (DecentSamplerSupportedFeatures.IsAttribute("effect", name))
            {
                effect.SetValue(name, attribute.Value);
            }
        }

        reader.Finish();

        foreach (var child in element.Elements())
        {
            Unknown(effect, child, context);
        }

        return effect;
    }

    private static DecentSamplerBusesElement ParseBuses(XElement element, DecentSamplerParseContext context)
    {
        var buses = new DecentSamplerBusesElement();
        var reader = new DecentSamplerElementReader(element, buses, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "bus")
            {
                buses.Add(ParseBus(child, context));
            }
            else
            {
                Unknown(buses, child, context);
            }
        }

        if (buses.Buses.Count > 16)
        {
            context.Report($"the preset declares {buses.Buses.Count.ToString(CultureInfo.InvariantCulture)} " +
                           "buses; the format allows 16, so the rest are ignored by the engine");
        }

        return buses;
    }

    private static DecentSamplerBus ParseBus(XElement element, DecentSamplerParseContext context)
    {
        var bus = new DecentSamplerBus();
        var reader = new DecentSamplerElementReader(element, bus, context);

        bus.BusVolume = reader.Double("busVolume");

        foreach (var pair in reader.Remaining().ToList())
        {
            var match = OutputAttribute.Match(pair.Key);
            if (!match.Success)
            {
                continue;
            }

            reader.Consume(pair.Key);
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1;

            if (match.Groups[2].Value == "Target")
            {
                if (DecentSamplerEnumNames.TryParse<DecentSamplerOutputTarget>(pair.Value, out var target))
                {
                    bus.SetOutputTarget(index, target);
                }
                else
                {
                    reader.Bad(pair.Key, pair.Value, "an output target this engine recognises");
                }
            }
            else if (DecentSamplerValues.TryDouble(pair.Value, out var volume))
            {
                bus.SetOutputVolume(index, volume);
            }
            else
            {
                reader.Bad(pair.Key, pair.Value, "a number");
            }
        }

        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "effects")
            {
                bus.Effects = bus.AddChild(ParseEffects(child, context));
            }
            else
            {
                Unknown(bus, child, context);
            }
        }

        return bus;
    }

    // -------------------------------------------------------------------------------- the MIDI element

    private static DecentSamplerMidiElement ParseMidi(XElement element, DecentSamplerParseContext context)
    {
        var midi = new DecentSamplerMidiElement();
        var reader = new DecentSamplerElementReader(element, midi, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "cc":
                {
                    var handler = new DecentSamplerMidiCc();
                    var handlerReader = new DecentSamplerElementReader(child, handler, context);
                    handler.Number = handlerReader.Int("number");
                    handlerReader.Finish();
                    ReadBindings(child, handler, context);
                    midi.Add(handler);
                    break;
                }

                case "note":
                {
                    var handler = new DecentSamplerMidiNote();
                    var handlerReader = new DecentSamplerElementReader(child, handler, context);
                    handler.NoteText = handlerReader.Trimmed("note");

                    if (handler.NoteText != null)
                    {
                        if (DecentSamplerValues.TryNoteRange(handler.NoteText, out var low, out var high))
                        {
                            handler.LowNote = low;
                            handler.HighNote = high;
                        }
                        else
                        {
                            handlerReader.Bad("note", handler.NoteText, "a MIDI note or a note range");
                        }
                    }

                    handler.EventType = handlerReader.Enumeration<DecentSamplerMidiEventType>("eventType");
                    handler.Enabled = handlerReader.Bool("enabled");
                    handler.SwallowNotes = handlerReader.Bool("swallowNotes");
                    handlerReader.Finish();
                    ReadBindings(child, handler, context);
                    midi.Add(handler);
                    break;
                }

                case "velocity":
                {
                    var handler = new DecentSamplerMidiVelocity();
                    var handlerReader = new DecentSamplerElementReader(child, handler, context);
                    handlerReader.Finish();
                    ReadBindings(child, handler, context);
                    midi.Add(handler);
                    break;
                }

                default:
                    Unknown(midi, child, context);
                    break;
            }
        }

        return midi;
    }

    // ------------------------------------------------------------------------------------ modulators

    private static DecentSamplerModulatorsElement ParseModulators(
        XElement element, DecentSamplerParseContext context)
    {
        var modulators = new DecentSamplerModulatorsElement();
        var reader = new DecentSamplerElementReader(element, modulators, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            var modulator = CreateModulator(child.Name.LocalName);
            if (modulator == null)
            {
                Unknown(modulators, child, context);
                continue;
            }

            var modulatorReader = new DecentSamplerElementReader(child, modulator, context);
            modulator.ModAmount = modulatorReader.Double("modAmount");
            modulator.Scope = modulatorReader.Enumeration<DecentSamplerModulatorScope>("scope");
            modulator.ModBehavior = modulatorReader.Enumeration<DecentSamplerModBehavior>("modBehavior");
            modulator.Tags = modulatorReader.Names("tags");

            switch (modulator)
            {
                case DecentSamplerLfoModulator lfo:
                    lfo.Shape = modulatorReader.Enumeration<DecentSamplerLfoShape>("shape", out var shapeText);
                    lfo.ShapeName = shapeText?.Trim();
                    // "rate" is the older spelling of "frequency"; the guide's arpeggiator topic
                    // still writes it, so it is read when "frequency" is absent.
                    var lfoFrequency = modulatorReader.Double("frequency");
                    var lfoRate = modulatorReader.Double("rate");
                    lfo.Frequency = lfoFrequency ?? lfoRate;
                    lfo.FrequencyFormat =
                        modulatorReader.Enumeration<DecentSamplerFrequencyFormat>("frequencyFormat");
                    lfo.DelayTime = modulatorReader.Double("delayTime");
                    lfo.Trigger = modulatorReader.Enumeration<DecentSamplerModulatorTrigger>("trigger");
                    break;

                case DecentSamplerEnvelopeModulator envelope:
                    envelope.Attack = modulatorReader.Double("attack");
                    envelope.Decay = modulatorReader.Double("decay");
                    envelope.Sustain = modulatorReader.Double("sustain");
                    envelope.Release = modulatorReader.Double("release");
                    envelope.AttackCurve = modulatorReader.Double("attackCurve");
                    envelope.DecayCurve = modulatorReader.Double("decayCurve");
                    envelope.ReleaseCurve = modulatorReader.Double("releaseCurve");
                    envelope.DelayTime = modulatorReader.Double("delayTime");
                    break;

                case DecentSamplerMidiCcModulator midiCc:
                    midiCc.Number = modulatorReader.Int("number");
                    midiCc.ChannelText = modulatorReader.Trimmed("channel");

                    if (midiCc.ChannelText != null)
                    {
                        if (string.Equals(midiCc.ChannelText, "voice", StringComparison.OrdinalIgnoreCase))
                        {
                            midiCc.ChannelFollowsVoice = true;
                        }
                        else if (DecentSamplerValues.TryInt(midiCc.ChannelText, out var channel) &&
                                 channel >= 1 && channel <= 16)
                        {
                            midiCc.ChannelFollowsVoice = false;
                            midiCc.Channel = channel;
                        }
                        else
                        {
                            modulatorReader.Bad("channel", midiCc.ChannelText, "\"voice\" or a channel from 1 to 16");
                        }
                    }

                    break;

                case DecentSamplerMpeTimbreModulator timbre:
                    timbre.RisingSmoothingTime = modulatorReader.Double("risingSmoothingTime");
                    timbre.FallingSmoothingTime = modulatorReader.Double("fallingSmoothingTime");
                    break;

                case DecentSamplerMpePressureModulator pressure:
                    pressure.RisingSmoothingTime = modulatorReader.Double("risingSmoothingTime");
                    pressure.FallingSmoothingTime = modulatorReader.Double("fallingSmoothingTime");
                    break;

                case DecentSamplerRandomModulator random:
                    random.Mode = modulatorReader.Enumeration<DecentSamplerRandomMode>("mode");
                    random.Frequency = modulatorReader.Double("frequency");
                    random.Trigger = modulatorReader.Enumeration<DecentSamplerModulatorTrigger>("trigger");
                    random.Seed = modulatorReader.Int("seed");
                    break;
            }

            modulatorReader.Finish();
            ReadBindings(child, modulator, context);
            modulators.Add(modulator);
        }

        return modulators;
    }

    private static DecentSamplerModulator CreateModulator(string elementName) =>
        elementName switch
        {
            "lfo" => new DecentSamplerLfoModulator(),
            "envelope" => new DecentSamplerEnvelopeModulator(),
            "midiCC" => new DecentSamplerMidiCcModulator(),
            "midiVelocity" => new DecentSamplerMidiVelocityModulator(),
            "mpeTimbre" => new DecentSamplerMpeTimbreModulator(),
            "mpePressure" => new DecentSamplerMpePressureModulator(),
            "random" => new DecentSamplerRandomModulator(),
            _ => null,
        };

    // ----------------------------------------------------------------- sequences, arpeggiator, tags

    private static DecentSamplerNoteSequencesElement ParseNoteSequences(
        XElement element, DecentSamplerParseContext context)
    {
        var sequences = new DecentSamplerNoteSequencesElement();
        var reader = new DecentSamplerElementReader(element, sequences, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName != "sequence")
            {
                Unknown(sequences, child, context);
                continue;
            }

            var sequence = new DecentSamplerNoteSequence();
            var sequenceReader = new DecentSamplerElementReader(child, sequence, context);
            sequence.Name = sequenceReader.Text("name");
            sequence.Length = sequenceReader.Double("length");
            sequence.Rate = sequenceReader.Double("rate");
            sequenceReader.Finish();

            foreach (var noteElement in child.Elements())
            {
                if (noteElement.Name.LocalName != "note")
                {
                    Unknown(sequence, noteElement, context);
                    continue;
                }

                var note = new DecentSamplerSequenceNote();
                var noteReader = new DecentSamplerElementReader(noteElement, note, context);
                note.Position = noteReader.Double("position");
                note.Velocity = noteReader.Double("velocity");
                note.Note = noteReader.Note("note");
                note.Length = noteReader.Double("length");
                noteReader.Finish();
                sequence.Add(note);
            }

            sequences.Add(sequence);
        }

        return sequences;
    }

    private static DecentSamplerArpeggiator ParseArpeggiator(XElement element, DecentSamplerParseContext context)
    {
        var arpeggiator = new DecentSamplerArpeggiator();
        var reader = new DecentSamplerElementReader(element, arpeggiator, context);

        arpeggiator.Enabled = reader.Bool("enabled");
        arpeggiator.Order = reader.Enumeration<DecentSamplerArpOrder>("arpOrder");
        arpeggiator.OctaveRange = reader.Int("arpOctaveRange");
        arpeggiator.OctaveMode = reader.Enumeration<DecentSamplerArpOctaveMode>("arpOctaveMode");
        arpeggiator.StepCount = reader.Int("arpStepCount");
        arpeggiator.GateLength = reader.Double("arpGateLength");
        arpeggiator.FollowGlobalTempo = reader.Bool("arpFollowGlobalTempo");
        arpeggiator.SyncDivision = reader.Enumeration<DecentSamplerSyncDivision>("arpSyncDivision");
        arpeggiator.RateMultiplier = reader.Double("arpRateMultiplier");
        arpeggiator.OverrideBpm = reader.Double("arpOverrideBpm");
        reader.Finish();

        foreach (var child in element.Elements())
        {
            Unknown(arpeggiator, child, context);
        }

        return arpeggiator;
    }

    private static DecentSamplerTagsElement ParseTags(XElement element, DecentSamplerParseContext context)
    {
        var tags = new DecentSamplerTagsElement();
        var reader = new DecentSamplerElementReader(element, tags, context);
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName != "tag")
            {
                Unknown(tags, child, context);
                continue;
            }

            var tag = new DecentSamplerTag();
            var tagReader = new DecentSamplerElementReader(child, tag, context);
            tag.Name = tagReader.Trimmed("name");
            tag.Enabled = tagReader.Bool("enabled");

            // MEASURED (round 2, item 24): a <tag>'s volume uses a DIFFERENT parser from a <sample>'s
            // or a <group>'s. It is the absolute value of a plain linear number, the "dB" suffix is
            // not recognised, trailing junk is ignored and the documented 0-to-1 range is not
            // enforced. Only "0" silences the tag.
            var tagVolumeText = tagReader.Text("volume");
            if (tagVolumeText != null)
            {
                if (DecentSamplerValues.TryTagVolume(tagVolumeText, out var tagVolume))
                {
                    tag.Volume = tagVolume;
                }
                else
                {
                    tagReader.Bad("volume", tagVolumeText, "a linear volume");
                }
            }

            tag.Pan = tagReader.Double("pan");
            tag.Polyphony = tagReader.Int("polyphony");
            tagReader.Finish();
            tags.Add(tag);
        }

        return tags;
    }

    // -------------------------------------------------------------------------- the user interface

    private static DecentSamplerUi ParseUi(XElement element, DecentSamplerParseContext context)
    {
        var ui = new DecentSamplerUi();
        var reader = new DecentSamplerElementReader(element, ui, context);

        ui.CoverArt = reader.Trimmed("coverArt");
        ui.BgImage = reader.Trimmed("bgImage");
        ui.BgColor = reader.Color("bgColor");
        ui.Width = reader.Double("width");
        ui.Height = reader.Double("height");
        ui.LayoutMode = reader.Trimmed("layoutMode");
        ui.BgMode = reader.Trimmed("bgMode");
        reader.Finish();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "tab":
                    ui.Add(ParseTab(child, ui, context));
                    break;
                case "keyboard":
                    ui.Keyboard = ui.AddChild(ParseKeyboard(child, context));
                    break;
                default:
                {
                    // Some presets put controls straight under <ui> without a tab. Reading them as an
                    // implicit tab keeps their control indexes right.
                    var uiElement = ParseUiElement(child, ui, context);
                    if (uiElement == null)
                    {
                        Unknown(ui, child, context);
                    }
                    else
                    {
                        ui.AddChild(uiElement);
                    }

                    break;
                }
            }
        }

        return ui;
    }

    private static DecentSamplerUiTab ParseTab(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var tab = new DecentSamplerUiTab();
        var reader = new DecentSamplerElementReader(element, tab, context);
        tab.Name = reader.Text("name");
        reader.Finish();

        foreach (var child in element.Elements())
        {
            var uiElement = ParseUiElement(child, ui, context);
            if (uiElement == null)
            {
                Unknown(tab, child, context);
            }
            else
            {
                tab.Add(uiElement);
            }
        }

        return tab;
    }

    private static DecentSamplerUiElement ParseUiElement(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        switch (element.Name.LocalName)
        {
            case "labeled-knob":
            case "labeled_knob":
            case "control":
                return ParseControl(element, ui, context);
            case "button":
                return ParseButton(element, ui, context);
            case "menu":
                return ParseMenu(element, ui, context);
            case "xyPad":
                return ParseXyPad(element, ui, context);
            case "label":
                return ParseLabel(element, ui, context);
            case "image":
                return ParseImage(element, ui, context);
            case "multiFrameImage":
                return ParseMultiFrameImage(element, ui, context);
            case "rectangle":
                return ParseRectangle(element, ui, context);
            case "line":
                return ParseLine(element, ui, context);
            case "oscilloscope":
                return ParseOscilloscope(element, ui, context);
            default:
                return null;
        }
    }

    private static void ReadUiCommon(DecentSamplerElementReader reader, DecentSamplerUiElement element)
    {
        element.X = reader.Double("x");
        element.Y = reader.Double("y");
        element.Width = reader.Double("width");
        element.Height = reader.Double("height");
        element.Visible = reader.Bool("visible");
        element.Enabled = reader.Bool("enabled");
        element.Tags = reader.Names("tags");
        element.Tooltip = reader.Text("tooltip");
        element.Uid = reader.Text("uid");
    }

    private static DecentSamplerUiControl ParseControl(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var control = new DecentSamplerUiControl();
        var reader = new DecentSamplerElementReader(element, control, context);
        ReadUiCommon(reader, control);

        control.Label = reader.Text("label");
        control.ShowLabel = reader.Bool("showLabel");
        control.ParameterName = reader.Text("parameterName");
        control.Style = reader.Enumeration<DecentSamplerControlStyle>("style", out var styleText);
        control.StyleName = styleText?.Trim();
        control.MinValue = reader.Double("minValue");
        control.MaxValue = reader.Double("maxValue");
        control.Value = reader.Double("value");
        control.DefaultValue = reader.Double("defaultValue");

        // "valueType" is the documented spelling; "type" is the older one the guide's own examples use.
        var valueTypeAttribute = element.Attribute("valueType") != null ? "valueType" : "type";
        control.ValueType = reader.Enumeration<DecentSamplerValueType>(valueTypeAttribute, out var valueTypeText);
        control.ValueTypeName = valueTypeText?.Trim();
        reader.Consume("valueType");
        reader.Consume("type");

        control.TextColor = reader.Color("textColor");
        control.TextSize = reader.Double("textSize");
        control.TrackForegroundColor = reader.Color("trackForegroundColor");
        control.TrackBackgroundColor = reader.Color("trackBackgroundColor");
        control.DisabledOpacity = reader.Double("disabledOpacity");
        control.SnapMode = reader.Enumeration<DecentSamplerSnapMode>("snapMode");
        control.SnapStopPoints = reader.Numbers("snapStopPoints");
        control.DefeatSnapWithShift = reader.Bool("defeatSnapWithShift");
        control.CustomSkinImage = reader.Trimmed("customSkinImage");
        control.CustomSkinHoverImage = reader.Trimmed("customSkinHoverImage");
        control.CustomSkinNumFrames = reader.Int("customSkinNumFrames");
        control.CustomSkinImageOrientation =
            reader.Enumeration<DecentSamplerImageOrientation>("customSkinImageOrientation");
        control.MouseDragSensitivity = reader.Int("mouseDragSensitivity");
        reader.Finish();

        ui.Register(control);

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "binding":
                    control.Add(ParseBinding(child, context));
                    break;
                case "state":
                    control.Add(ParseState(child, context));
                    break;
                default:
                    Unknown(control, child, context);
                    break;
            }
        }

        return control;
    }

    private static DecentSamplerUiState ParseState(XElement element, DecentSamplerParseContext context)
    {
        var state = new DecentSamplerUiState();
        var reader = new DecentSamplerElementReader(element, state, context);
        state.Name = reader.Text("name");
        state.MainImage = reader.Trimmed("mainImage");
        state.HoverImage = reader.Trimmed("hoverImage");
        state.ClickImage = reader.Trimmed("clickImage");
        reader.Finish();
        ReadBindings(element, state, context);
        return state;
    }

    private static DecentSamplerUiButton ParseButton(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var button = new DecentSamplerUiButton();
        var reader = new DecentSamplerElementReader(element, button, context);
        ReadUiCommon(reader, button);

        button.Value = reader.Double("value");
        button.Name = reader.Text("name");
        button.ParameterName = reader.Text("parameterName");
        button.DefaultValue = reader.Double("defaultValue");
        button.Style = reader.Enumeration<DecentSamplerButtonStyle>("style", out var styleText);
        button.StyleName = styleText?.Trim();
        button.MainImage = reader.Trimmed("mainImage");
        button.HoverImage = reader.Trimmed("hoverImage");
        button.ClickImage = reader.Trimmed("clickImage");
        button.DisabledOpacity = reader.Double("disabledOpacity");
        reader.Finish();

        ui.Register(button);

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "state":
                    button.Add(ParseState(child, context));
                    break;
                case "binding":
                    button.Add(ParseBinding(child, context));
                    break;
                default:
                    Unknown(button, child, context);
                    break;
            }
        }

        return button;
    }

    private static DecentSamplerUiMenu ParseMenu(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var menu = new DecentSamplerUiMenu();
        var reader = new DecentSamplerElementReader(element, menu, context);
        ReadUiCommon(reader, menu);

        menu.Value = reader.Double("value");
        menu.TextColor = reader.Color("textColor");
        menu.BackgroundColor = reader.Color("backgroundColor");
        menu.HighlightedTextColor = reader.Color("highlightedTextColor");
        menu.HighlightedBackgroundColor = reader.Color("highlightedBackgroundColor");
        menu.VerticalAlignment = reader.Enumeration<DecentSamplerVerticalAlignment>("vAlign");
        menu.HorizontalAlignment = reader.Enumeration<DecentSamplerHorizontalAlignment>("hAlign");
        menu.RequireSelection = reader.Bool("requireSelection");
        menu.PlaceholderText = reader.Text("placeholderText");
        reader.Finish();

        ui.Register(menu);

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "option":
                {
                    var option = new DecentSamplerUiMenuOption();
                    var optionReader = new DecentSamplerElementReader(child, option, context);
                    option.Name = optionReader.Text("name");
                    optionReader.Finish();
                    ReadBindings(child, option, context);
                    menu.Add(option);
                    break;
                }

                case "binding":
                    menu.Add(ParseBinding(child, context));
                    break;

                default:
                    Unknown(menu, child, context);
                    break;
            }
        }

        return menu;
    }

    private static DecentSamplerUiXyPad ParseXyPad(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var pad = new DecentSamplerUiXyPad();
        var reader = new DecentSamplerElementReader(element, pad, context);
        ReadUiCommon(reader, pad);

        pad.ParameterName = reader.Text("parameterName");
        pad.MarkerDiameter = reader.Double("markerDiameter");
        pad.MarkerOutlineColor = reader.Color("markerOutlineColor");
        pad.MarkerFillColor = reader.Color("markerFillColor");
        pad.OutlineColor = reader.Color("outlineColor");
        pad.BgColor = reader.Color("bgColor");
        pad.XValue = reader.Double("xValue");
        pad.YValue = reader.Double("yValue");
        reader.Finish();

        ui.Register(pad);

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "x":
                {
                    var axis = new DecentSamplerUiXyAxis();
                    var axisReader = new DecentSamplerElementReader(child, axis, context);
                    axisReader.Finish();
                    ReadBindings(child, axis, context);
                    pad.XAxis = pad.AddChild(axis);
                    break;
                }

                case "y":
                {
                    var axis = new DecentSamplerUiXyAxis();
                    var axisReader = new DecentSamplerElementReader(child, axis, context);
                    axisReader.Finish();
                    ReadBindings(child, axis, context);
                    pad.YAxis = pad.AddChild(axis);
                    break;
                }

                case "binding":
                    pad.Add(ParseBinding(child, context));
                    break;

                default:
                    Unknown(pad, child, context);
                    break;
            }
        }

        return pad;
    }

    private static DecentSamplerUiLabel ParseLabel(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var label = new DecentSamplerUiLabel();
        var reader = new DecentSamplerElementReader(element, label, context);
        ReadUiCommon(reader, label);

        label.Text = reader.Text("text");
        label.TextColor = reader.Color("textColor");
        label.TextSize = reader.Double("textSize");
        label.VerticalAlignment = reader.Enumeration<DecentSamplerVerticalAlignment>("vAlign");
        label.HorizontalAlignment = reader.Enumeration<DecentSamplerHorizontalAlignment>("hAlign");
        label.Orientation = reader.Enumeration<DecentSamplerTextOrientation>("orientation");
        reader.Finish();

        ui.Register(label);
        ReadBindings(element, label, context);
        return label;
    }

    private static DecentSamplerUiImage ParseImage(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var image = new DecentSamplerUiImage();
        var reader = new DecentSamplerElementReader(element, image, context);
        ReadUiCommon(reader, image);

        image.Path = reader.Trimmed("path");
        image.AspectRatioMode = reader.Enumeration<DecentSamplerAspectRatioMode>("aspectRatioMode");
        image.Opacity = reader.Double("opacity");
        reader.Finish();

        ui.Register(image);
        ReadBindings(element, image, context);
        return image;
    }

    private static DecentSamplerUiMultiFrameImage ParseMultiFrameImage(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var image = new DecentSamplerUiMultiFrameImage();
        var reader = new DecentSamplerElementReader(element, image, context);
        ReadUiCommon(reader, image);

        image.Path = reader.Trimmed("path");
        image.NumFrames = reader.Int("numFrames");
        image.FrameRate = reader.Double("frameRate");
        image.Opacity = reader.Double("opacity");
        image.SourceFormat = reader.Enumeration<DecentSamplerImageStripFormat>("sourceFormat", out var formatText);
        image.SourceFormatName = formatText?.Trim();
        image.PlaybackMode =
            reader.Enumeration<DecentSamplerAnimationPlaybackMode>("playbackMode", out var playbackText);
        image.PlaybackModeName = playbackText?.Trim();

        // The guide's own example misspells the strip orientation attribute; it is known so that a
        // preset copied from the guide does not report an unknown attribute.
        reader.Consume("imageStripOrientaton");
        reader.Finish();

        ui.Register(image);
        ReadBindings(element, image, context);
        return image;
    }

    private static DecentSamplerUiRectangle ParseRectangle(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var rectangle = new DecentSamplerUiRectangle();
        var reader = new DecentSamplerElementReader(element, rectangle, context);
        ReadUiCommon(reader, rectangle);

        rectangle.FillColor = reader.Color("fillColor");
        rectangle.BorderColor = reader.Color("borderColor");
        rectangle.BorderThickness = reader.Double("borderThickness");
        reader.Finish();

        ui.Register(rectangle);
        ReadBindings(element, rectangle, context);
        return rectangle;
    }

    private static DecentSamplerUiLine ParseLine(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var line = new DecentSamplerUiLine();
        var reader = new DecentSamplerElementReader(element, line, context);
        ReadUiCommon(reader, line);

        line.X1 = reader.Double("x1");
        line.Y1 = reader.Double("y1");
        line.X2 = reader.Double("x2");
        line.Y2 = reader.Double("y2");
        line.LineColor = reader.Color("lineColor");
        line.LineThickness = reader.Double("lineThickness");
        reader.Finish();

        ui.Register(line);
        ReadBindings(element, line, context);
        return line;
    }

    private static DecentSamplerUiOscilloscope ParseOscilloscope(
        XElement element, DecentSamplerUi ui, DecentSamplerParseContext context)
    {
        var oscilloscope = new DecentSamplerUiOscilloscope();
        var reader = new DecentSamplerElementReader(element, oscilloscope, context);
        ReadUiCommon(reader, oscilloscope);

        oscilloscope.BackgroundColor = reader.Color("backgroundColor");
        oscilloscope.WaveColor = reader.Color("waveColor");
        oscilloscope.LineThickness = reader.Double("lineThickness");
        oscilloscope.ShowCenterLine = reader.Bool("showCenterLine");
        reader.Finish();

        ui.Register(oscilloscope);
        ReadBindings(element, oscilloscope, context);
        return oscilloscope;
    }

    private static DecentSamplerUiKeyboard ParseKeyboard(XElement element, DecentSamplerParseContext context)
    {
        var keyboard = new DecentSamplerUiKeyboard();
        var reader = new DecentSamplerElementReader(element, keyboard, context);
        keyboard.CenterNote = reader.Note("centerNote");
        reader.Finish();

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName != "color")
            {
                Unknown(keyboard, child, context);
                continue;
            }

            var color = new DecentSamplerUiKeyboardColor();
            var colorReader = new DecentSamplerElementReader(child, color, context);
            color.LoNote = colorReader.Note("loNote");
            color.HiNote = colorReader.Note("hiNote");
            color.Color = colorReader.Color("color");
            colorReader.Finish();
            keyboard.Add(color);
        }

        return keyboard;
    }

    // ------------------------------------------------------------------------------------- bindings

    private static void ReadBindings(
        XElement element, DecentSamplerBindingHost host, DecentSamplerParseContext context)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "binding")
            {
                host.Add(ParseBinding(child, context));
            }
            else
            {
                Unknown(host, child, context);
            }
        }
    }

    private static DecentSamplerBinding ParseBinding(XElement element, DecentSamplerParseContext context)
    {
        var binding = new DecentSamplerBinding();
        var reader = new DecentSamplerElementReader(element, binding, context);

        binding.TypeName = reader.Trimmed("type");
        binding.BindingType =
            binding.TypeName != null &&
            DecentSamplerEnumNames.TryParse<DecentSamplerBindingType>(binding.TypeName, out var type)
                ? type
                : DecentSamplerBindingType.Unknown;

        if (binding.BindingType == DecentSamplerBindingType.Unknown && binding.TypeName != null)
        {
            reader.Bad("type", binding.TypeName, "a binding type this engine recognises");
        }

        binding.LevelName = reader.Trimmed("level");
        binding.Level =
            binding.LevelName != null &&
            DecentSamplerEnumNames.TryParse<DecentSamplerBindingLevel>(binding.LevelName, out var level)
                ? level
                : DecentSamplerBindingLevel.Unknown;

        if (binding.Level == DecentSamplerBindingLevel.Unknown && binding.LevelName != null)
        {
            reader.Bad("level", binding.LevelName, "a binding level this engine recognises");
        }

        binding.Position = reader.Int("position");
        binding.ControlIndex = reader.Int("controlIndex");
        binding.GroupIndex = reader.Int("groupIndex");
        binding.EffectIndex = reader.Int("effectIndex");
        binding.ModulatorIndex = reader.Int("modulatorIndex");
        binding.BusIndex = reader.Int("busIndex");
        binding.StateIndex = reader.Int("stateIndex");
        binding.BindingIndex = reader.Int("bindingIndex");
        var midiElementIndex = reader.Int("midiElementIndex");
        var legacyNoteIndex = reader.Int("noteIndex");
        binding.MidiElementIndex = midiElementIndex ?? legacyNoteIndex;
        binding.ColorIndex = reader.Int("colorIndex");
        binding.SeqIndex = reader.Int("seqIndex");

        binding.Tags = reader.Names("tags");
        binding.GroupTags = reader.Names("groupTags");
        binding.SampleTags = reader.Names("sampleTags");
        binding.OscillatorTags = reader.Names("oscillatorTags");
        binding.EffectTags = reader.Names("effectTags");
        binding.ModulatorTags = reader.Names("modulatorTags");
        binding.ControlTags = reader.Names("controlTags");

        binding.Enabled = reader.Bool("enabled");
        binding.Identifier = reader.Trimmed("identifier");
        binding.Parameter = reader.Trimmed("parameter");

        if (binding.Parameter != null && !DecentSamplerSupportedFeatures.IsBindingParameter(binding.Parameter))
        {
            context.Report($"binding parameter not recognised: '{binding.Parameter}' " +
                           $"(line {reader.LineNumber.ToString(CultureInfo.InvariantCulture)})");
        }

        binding.TranslationName = reader.Trimmed("translation");
        if (binding.TranslationName != null)
        {
            if (DecentSamplerEnumNames.TryParse<DecentSamplerTranslation>(
                    binding.TranslationName, out var translation))
            {
                binding.Translation = translation;
            }
            else
            {
                reader.Bad("translation", binding.TranslationName, "linear, table or fixed_value");
            }
        }

        binding.TranslationOutputMin = reader.Double("translationOutputMin");
        binding.TranslationOutputMax = reader.Double("translationOutputMax");
        binding.TranslationReversed = reader.Bool("translationReversed");

        binding.TranslationTableText = reader.Trimmed("translationTable");
        if (binding.TranslationTableText != null)
        {
            if (DecentSamplerValues.TryTranslationTable(binding.TranslationTableText, out var points))
            {
                binding.TranslationTable = points;
            }
            else
            {
                reader.Bad("translationTable", binding.TranslationTableText,
                    "two or more \"input,output\" pairs separated by semicolons");
            }
        }

        binding.TranslationValue = reader.Text("translationValue");
        binding.TriggerOnLoad = reader.Bool("triggerOnLoad");
        binding.ModBehavior = reader.Enumeration<DecentSamplerModBehavior>("modBehavior");
        binding.ModAmount = reader.Double("modAmount");

        binding.SeqFollowGlobalTempo = reader.Bool("seqFollowGlobalTempo");
        binding.SeqTriggerBehavior = reader.Enumeration<DecentSamplerSeqTriggerBehavior>("seqTriggerBehavior");
        binding.SeqPlayerIdentifier = reader.Trimmed("seqPlayerIdentifier");
        binding.SeqTrackMidiInputVelocity = reader.Double("seqTrackMidiInputVelocity");
        binding.SeqTranspose = reader.Double("seqTranspose");
        binding.SeqTransposeWithRootNote = reader.Double("seqTransposeWithRootNote");
        binding.SeqPlaybackRate = reader.Double("seqPlaybackRate");
        binding.SeqLoopMode = reader.Enumeration<DecentSamplerSeqLoopMode>("seqLoopMode");

        reader.Finish();

        foreach (var child in element.Elements())
        {
            Unknown(binding, child, context);
        }

        return binding;
    }
}

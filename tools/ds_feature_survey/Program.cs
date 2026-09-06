using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Tools.DsFeatureSurvey;

internal static class Program
{
    private sealed class PresetRecord
    {
        public string Library = string.Empty;
        public string Name = string.Empty;
        public int Groups;
        public int Samples;
        public int Oscillators;
        public int Bytes;
        public List<string> Problems = [];
        public List<string> UnknownAttributes = [];
        public List<string> UnknownElements = [];
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: ds_feature_survey <corpus-directory> [output-directory]");
            return 2;
        }

        var corpus = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args.Length > 1 ? args[1] : ".");

        if (!Directory.Exists(corpus))
        {
            Console.Error.WriteLine($"corpus directory not found: {corpus}");
            return 2;
        }

        Directory.CreateDirectory(output);

        var presets = new List<PresetRecord>();
        var elementPaths = new Counter();
        var attributes = new Counter();
        var attributeExamples = new Dictionary<string, string>(StringComparer.Ordinal);
        var effectTypes = new Counter();
        var bindingTypes = new Counter();
        var bindingLevels = new Counter();
        var bindingParameters = new Counter();
        var waveforms = new Counter();
        var modulatorKinds = new Counter();
        var sampleExtensions = new Counter();
        var parseErrors = 0;

        foreach (var libraryDirectory in Directory
                     .EnumerateDirectories(corpus)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var library = Path.GetFileName(libraryDirectory);

            foreach (var file in EnumerateSources(libraryDirectory))
            {
                foreach (var pair in ReadPresets(file, ref parseErrors))
                {
                    var record = Record(pair.Key, library, file);
                    presets.Add(record);

                    Collect(pair.Key, pair.Value, library, elementPaths, attributes, attributeExamples,
                        effectTypes, bindingTypes, bindingLevels, bindingParameters, waveforms,
                        modulatorKinds, sampleExtensions);
                }
            }
        }

        WriteLibraries(Path.Combine(output, "libraries.md"), presets);
        WriteAttributes(Path.Combine(output, "attributes.md"), elementPaths, attributes, attributeExamples,
            effectTypes, bindingTypes, bindingLevels, bindingParameters, waveforms, modulatorKinds,
            sampleExtensions);
        WriteCoverage(Path.Combine(output, "coverage.md"), presets, attributes, bindingParameters,
            effectTypes, waveforms);

        Console.WriteLine($"presets parsed: {presets.Count.ToString(CultureInfo.InvariantCulture)} in " +
                          $"{presets.Select(p => p.Library).Distinct().Count().ToString(CultureInfo.InvariantCulture)} " +
                          $"libraries; parse errors: {parseErrors.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine();
        Console.WriteLine(CoverageTable(presets, attributes, bindingParameters, effectTypes, waveforms));
        Console.WriteLine($"reports written to {output}");
        return 0;
    }

    private static IEnumerable<string> EnumerateSources(string libraryDirectory)
    {
        var files = new List<string>();

        try
        {
            files.AddRange(Directory.EnumerateFiles(libraryDirectory, "*.dspreset", SearchOption.AllDirectories));
            files.AddRange(Directory.EnumerateFiles(libraryDirectory, "*.dslibrary", SearchOption.AllDirectories));
            files.AddRange(Directory.EnumerateFiles(libraryDirectory, "*.dsbundle", SearchOption.AllDirectories));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"could not list {libraryDirectory}: {exception.Message}");
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static IEnumerable<KeyValuePair<DecentSamplerPreset, XDocument>> ReadPresets(
        string file, ref int parseErrors)
    {
        var results = new List<KeyValuePair<DecentSamplerPreset, XDocument>>();
        var extension = Path.GetExtension(file);

        if (string.Equals(extension, ".dspreset", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var xml = File.ReadAllText(file);
                results.Add(new KeyValuePair<DecentSamplerPreset, XDocument>(
                    DecentSamplerParser.ParseText(xml, file), XDocument.Parse(xml)));
            }
            catch (Exception exception)
            {
                parseErrors++;
                Console.Error.WriteLine($"parse error in {file}: {exception.Message}");
            }

            return results;
        }

        try
        {
            using var archive = new DecentSamplerArchiveContainer(file);

            foreach (var entry in archive.FindPresets())
            {
                try
                {
                    string xml;
                    using (var stream = archive.OpenFile(entry))
                    using (var text = new StreamReader(stream))
                    {
                        xml = text.ReadToEnd();
                    }

                    var preset = DecentSamplerParser.ParseText(xml, entry);
                    results.Add(new KeyValuePair<DecentSamplerPreset, XDocument>(preset, XDocument.Parse(xml)));
                }
                catch (Exception exception)
                {
                    parseErrors++;
                    Console.Error.WriteLine($"parse error in {file}!{entry}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            parseErrors++;
            Console.Error.WriteLine($"could not open {file}: {exception.Message}");
        }

        return results;
    }

    private static PresetRecord Record(DecentSamplerPreset preset, string library, string file)
    {
        var groups = preset.Groups?.Groups ?? [];

        return new PresetRecord
        {
            Library = library,
            Name = preset.Name,
            Groups = groups.Count,
            Samples = groups.Sum(group => group.Samples.Count),
            Oscillators = groups.Sum(group => group.Oscillators.Count),
            Bytes = (int)SafeLength(file),
            Problems = preset.Problems.ToList(),
            UnknownAttributes = preset.AllUnknownAttributes()
                .Select(attribute => attribute.ElementName + "@" + attribute.Name)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            UnknownElements = preset.AllUnknownElements()
                .Select(element => element.ParentElementName + "/" + element.Name)
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList(),
        };
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void Collect(
        DecentSamplerPreset preset,
        XDocument document,
        string library,
        Counter elementPaths,
        Counter attributes,
        Dictionary<string, string> attributeExamples,
        Counter effectTypes,
        Counter bindingTypes,
        Counter bindingLevels,
        Counter bindingParameters,
        Counter waveforms,
        Counter modulatorKinds,
        Counter sampleExtensions)
    {
        foreach (var element in preset.Descendants())
        {
            elementPaths.Add(element.ElementName, library, preset.Name);

            switch (element)
            {
                case DecentSamplerEffect effect:
                    effectTypes.Add(effect.TypeName ?? "(none)", library, preset.Name);
                    break;

                case DecentSamplerBinding binding:
                    bindingTypes.Add(binding.TypeName ?? "(none)", library, preset.Name);
                    bindingLevels.Add(binding.LevelName ?? "(none)", library, preset.Name);
                    bindingParameters.Add(binding.Parameter ?? "(none)", library, preset.Name);
                    break;

                case DecentSamplerModulator modulator:
                    modulatorKinds.Add(modulator.ElementName, library, preset.Name);
                    break;

                case DecentSamplerSampleElement sample when sample.Path != null:
                    sampleExtensions.Add(
                        Path.GetExtension(sample.Path).ToLowerInvariant(), library, preset.Name);
                    break;
            }

            if (element is DecentSamplerSoundElement sound && sound.WaveformName != null)
            {
                waveforms.Add(sound.WaveformName, library, preset.Name);
            }
        }

        // The model keeps only the attributes the parser did NOT understand, so attribute usage is
        // counted from the raw XML: that is what makes the coverage figure a measurement rather than a
        // restatement of what the engine already knows.
        if (document.Root == null)
        {
            return;
        }

        foreach (var element in document.Root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var name = element.Name.LocalName + "@" +
                           DecentSamplerSupportedFeatures.CanonicalAttributeName(attribute.Name.LocalName);
                attributes.Add(name, library, preset.Name);
                attributeExamples.TryAdd(name, attribute.Value);
            }
        }
    }

    private static void WriteLibraries(string path, List<PresetRecord> presets)
    {
        var text = new StringBuilder();
        text.AppendLine("# Presets");
        text.AppendLine();
        text.AppendLine("| library | preset | groups | samples | oscillators | bytes | problems |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");

        foreach (var preset in presets)
        {
            text.AppendLine(
                $"| {preset.Library} | {preset.Name} | {preset.Groups} | {preset.Samples} | " +
                $"{preset.Oscillators} | {preset.Bytes} | {preset.Problems.Count} |");
        }

        text.AppendLine();
        text.AppendLine("# Problems, per preset");

        foreach (var preset in presets.Where(preset => preset.Problems.Count > 0))
        {
            text.AppendLine();
            text.AppendLine($"## {preset.Library} / {preset.Name}");
            text.AppendLine();

            foreach (var problem in preset.Problems)
            {
                text.AppendLine("- " + problem);
            }
        }

        File.WriteAllText(path, text.ToString());
    }

    private static void WriteAttributes(
        string path,
        Counter elementPaths,
        Counter attributes,
        Dictionary<string, string> attributeExamples,
        Counter effectTypes,
        Counter bindingTypes,
        Counter bindingLevels,
        Counter bindingParameters,
        Counter waveforms,
        Counter modulatorKinds,
        Counter sampleExtensions)
    {
        var text = new StringBuilder();

        Section(text, "Elements", elementPaths, null);
        Section(text, "Attributes", attributes, attributeExamples);
        Section(text, "Effect types", effectTypes, null);
        Section(text, "Binding types", bindingTypes, null);
        Section(text, "Binding levels", bindingLevels, null);
        Section(text, "Binding parameters", bindingParameters, null);
        Section(text, "Oscillator waveforms", waveforms, null);
        Section(text, "Modulators", modulatorKinds, null);
        Section(text, "Sample extensions", sampleExtensions, null);

        File.WriteAllText(path, text.ToString());
    }

    private static void Section(
        StringBuilder text, string title, Counter counter, Dictionary<string, string> examples)
    {
        text.AppendLine("# " + title);
        text.AppendLine();

        if (counter.Names.Count == 0)
        {
            text.AppendLine("_none_");
            text.AppendLine();
            return;
        }

        text.AppendLine(examples == null
            ? "| name | libraries | presets | uses |"
            : "| name | libraries | presets | uses | example |");
        text.AppendLine(examples == null ? "| --- | ---: | ---: | ---: |" : "| --- | ---: | ---: | ---: | --- |");

        foreach (var name in counter.Ranked())
        {
            var row = $"| {name} | {counter.LibraryCount(name)} | {counter.PresetCount(name)} | " +
                      $"{counter.UseCount(name)} |";

            if (examples != null)
            {
                examples.TryGetValue(name, out var example);
                row += $" {example} |";
            }

            text.AppendLine(row);
        }

        text.AppendLine();
    }

    private static void WriteCoverage(
        string path,
        List<PresetRecord> presets,
        Counter attributes,
        Counter bindingParameters,
        Counter effectTypes,
        Counter waveforms)
    {
        var text = new StringBuilder();
        text.AppendLine("# Coverage against DecentSamplerSupportedFeatures");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(CoverageTable(presets, attributes, bindingParameters, effectTypes, waveforms));
        text.AppendLine("```");
        text.AppendLine();

        var incomplete = presets
            .Where(preset => preset.UnknownAttributes.Count > 0 || preset.UnknownElements.Count > 0)
            .ToList();

        text.AppendLine("# Presets with anything the engine does not recognise");
        text.AppendLine();

        if (incomplete.Count == 0)
        {
            text.AppendLine("_none: every element and attribute in the corpus is known to the engine._");
        }
        else
        {
            foreach (var preset in incomplete)
            {
                text.AppendLine($"## {preset.Library} / {preset.Name}");
                text.AppendLine();

                foreach (var name in preset.UnknownElements)
                {
                    text.AppendLine($"- element {name}");
                }

                foreach (var name in preset.UnknownAttributes)
                {
                    text.AppendLine($"- attribute {name}");
                }

                text.AppendLine();
            }
        }

        File.WriteAllText(path, text.ToString());
    }

    private static string CoverageTable(
        List<PresetRecord> presets,
        Counter attributes,
        Counter bindingParameters,
        Counter effectTypes,
        Counter waveforms)
    {
        var text = new StringBuilder();

        var clean = presets.Count(preset =>
            preset.UnknownAttributes.Count == 0 && preset.UnknownElements.Count == 0);

        var unknownParameters = bindingParameters.Names
            .Where(name => name != "(none)" && !DecentSamplerSupportedFeatures.IsBindingParameter(name))
            .OrderBy(name => name, StringComparer.Ordinal).ToList();

        var unknownEffects = effectTypes.Names
            .Where(name => name != "(none)" && !DecentSamplerSupportedFeatures.IsEffectType(name))
            .OrderBy(name => name, StringComparer.Ordinal).ToList();

        var unknownWaveforms = waveforms.Names
            .Where(name => !DecentSamplerSupportedFeatures.IsWaveform(name))
            .OrderBy(name => name, StringComparer.Ordinal).ToList();

        text.AppendLine("category                       corpus   known   unknown");
        text.AppendLine("---------------------------------------------------------");
        text.AppendLine(Row("presets fully recognised", presets.Count, clean, presets.Count - clean));
        text.AppendLine(Row("binding parameters", bindingParameters.Names.Count(n => n != "(none)"),
            bindingParameters.Names.Count(n => n != "(none)") - unknownParameters.Count,
            unknownParameters.Count));
        text.AppendLine(Row("effect types", effectTypes.Names.Count(n => n != "(none)"),
            effectTypes.Names.Count(n => n != "(none)") - unknownEffects.Count, unknownEffects.Count));
        text.AppendLine(Row("oscillator waveforms", waveforms.Names.Count,
            waveforms.Names.Count - unknownWaveforms.Count, unknownWaveforms.Count));
        var unknownAttributes = attributes.Names
            .Where(name => !IsKnownAttribute(name))
            .OrderBy(name => name, StringComparer.Ordinal).ToList();

        text.AppendLine(Row("attribute names", attributes.Names.Count,
            attributes.Names.Count - unknownAttributes.Count, unknownAttributes.Count));
        text.AppendLine();
        text.AppendLine($"features listed by DecentSamplerSupportedFeatures: " +
                        $"{DecentSamplerSupportedFeatures.Features.Count.ToString(CultureInfo.InvariantCulture)}");

        if (unknownParameters.Count > 0)
        {
            text.AppendLine("unknown binding parameters: " + string.Join(", ", unknownParameters));
        }

        if (unknownEffects.Count > 0)
        {
            text.AppendLine("unknown effect types: " + string.Join(", ", unknownEffects));
        }

        if (unknownWaveforms.Count > 0)
        {
            text.AppendLine("unknown waveforms: " + string.Join(", ", unknownWaveforms));
        }

        if (unknownAttributes.Count > 0)
        {
            text.AppendLine("unrecognised attributes: " + string.Join(", ", unknownAttributes));
        }

        return text.ToString();
    }

    private static bool IsKnownAttribute(string qualifiedName)
    {
        var at = qualifiedName.IndexOf('@');
        return at > 0 && DecentSamplerSupportedFeatures.IsAttribute(
            qualifiedName.Substring(0, at), qualifiedName.Substring(at + 1));
    }

    private static string Row(string label, int corpus, int known, int unknown) =>
        label.PadRight(30) +
        corpus.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
        known.ToString(CultureInfo.InvariantCulture).PadLeft(8) +
        unknown.ToString(CultureInfo.InvariantCulture).PadLeft(10);

    private sealed class Counter
    {
        private readonly Dictionary<string, int> _uses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _libraries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _presets = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Names => _uses.Keys;

        public void Add(string name, string library, string preset)
        {
            _uses.TryGetValue(name, out var uses);
            _uses[name] = uses + 1;

            if (!_libraries.TryGetValue(name, out var libraries))
            {
                libraries = new HashSet<string>(StringComparer.Ordinal);
                _libraries[name] = libraries;
            }

            libraries.Add(library);

            if (!_presets.TryGetValue(name, out var presets))
            {
                presets = new HashSet<string>(StringComparer.Ordinal);
                _presets[name] = presets;
            }

            presets.Add(library + "/" + preset);
        }

        public int UseCount(string name) => _uses.TryGetValue(name, out var uses) ? uses : 0;

        public int LibraryCount(string name) =>
            _libraries.TryGetValue(name, out var libraries) ? libraries.Count : 0;

        public int PresetCount(string name) => _presets.TryGetValue(name, out var presets) ? presets.Count : 0;

        // Ranked by the number of LIBRARIES that use a name, never by raw occurrences: one sprawling
        // library must not decide the ranking. The same rule the SFZ survey follows.
        public IEnumerable<string> Ranked() =>
            _uses.Keys
                .OrderByDescending(LibraryCount)
                .ThenByDescending(PresetCount)
                .ThenBy(name => name, StringComparer.Ordinal);
    }
}

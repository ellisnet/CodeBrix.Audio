using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// Builds every effect chain in a folder of real sample libraries and renders a bar through each, so
/// that "the core effect set covers what libraries actually use" is measured rather than asserted.
/// Opt-in: point CODEBRIX_AUDIO_DS_CORPUS at a folder of unpacked libraries.
/// </summary>
/// <remarks>
/// ONE LIBRARY AT A TIME, disposed before the next, for the same memory reason the engine's own corpus
/// test gives. Nothing from a corpus is ever copied into the repository.
/// </remarks>
public class DecentSamplerEffectCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const int SampleRate = 44100;

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_corpus_effect_chain_builds_with_nothing_unsupported_and_renders_a_bar()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var report = new StringBuilder();
        var failures = new List<string>();
        var chains = 0;
        var effects = 0;

        //Act
        foreach (var path in Sources())
        {
            try
            {
                var counted = RenderOne(path, report, failures);
                chains += counted.Chains;
                effects += counted.Effects;
            }
            catch (Exception exception)
            {
                failures.Add(Path.GetFileName(path) + ": " + exception);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture, "{0} chains, {1} effects", chains, effects));

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        failures.Should().BeEmpty();
    }

    private static (int Chains, int Effects) RenderOne(
        string path, StringBuilder report, List<string> failures)
    {
        var chains = 0;
        var effects = 0;

        foreach (var presetName in PresetNamesIn(path))
        {
            var options = presetName == null
                ? null
                : new DecentSamplerLoadOptions { PresetName = presetName };

            using var instrument = DecentSamplerInstrument.Load(path, options);

            var declared = Chains(instrument).ToArray();
            chains += declared.Length;
            effects += declared.Sum(chain => chain.Effects.Count);

            var synthesizer = new DecentSamplerSynthesizer(instrument, SampleRate);

            // Every effect type the preset asks for must be one the core registers; an unregistered one
            // shows up here as an "effect type '...' needs" line.
            var unsupported = synthesizer.UnsupportedFeatures
                .Where(feature => feature.StartsWith("effect:", StringComparison.Ordinal))
                .ToArray();

            if (unsupported.Length > 0)
            {
                failures.Add(instrument.Name + ": " + string.Join(", ", unsupported));
            }

            synthesizer.NoteOn(0, 60, 100);

            var left = new float[SampleRate * 2];
            var right = new float[left.Length];
            synthesizer.Render(left, right);

            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-52} chains {1,3}  effects {2,3}  buses {3,3}  aux {4,2}  peak {5:0.0000}",
                instrument.Name,
                declared.Length,
                declared.Sum(chain => chain.Effects.Count),
                instrument.Buses.Count,
                synthesizer.AuxiliaryOutputCount,
                Peak(left)));
        }

        return (chains, effects);
    }

    // Every <effects> element in the preset: the instrument chain, each bus chain and each group chain.
    private static IEnumerable<DecentSamplerEffectsElement> Chains(DecentSamplerInstrument instrument)
    {
        if (instrument.Effects != null)
        {
            yield return instrument.Effects;
        }

        foreach (var bus in instrument.Buses)
        {
            if (bus.Effects != null)
            {
                yield return bus.Effects;
            }
        }

        foreach (var group in instrument.Groups)
        {
            if (group.Effects != null)
            {
                yield return group.Effects;
            }
        }
    }

    private static IReadOnlyList<string> PresetNamesIn(string path)
    {
        if (path.EndsWith(".dspreset", StringComparison.OrdinalIgnoreCase))
        {
            return [null];
        }

        using var container = DecentSamplerContainer.Open(path);
        var presets = container.FindPresets();

        return presets.Count <= 1
            ? [null]
            : presets.Select(Path.GetFileNameWithoutExtension).ToArray();
    }

    private static double Peak(float[] samples)
    {
        var peak = 0.0;

        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dslibrary", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dsbundle", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Runs the binding engine over a folder of real sample libraries: every binding in every preset must
/// resolve, and the initial state must be applied without throwing. Opt-in through
/// CODEBRIX_AUDIO_DS_CORPUS; skipped when it is unset. Nothing from the corpus enters the repository.
/// </summary>
/// <remarks>
/// Sample decoding is off, because resolving bindings needs the model, not the audio, and the corpus
/// decodes to several gigabytes.
/// </remarks>
public class DecentSamplerBindingCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_binding_in_the_corpus_resolves_to_something()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var options = new DecentSamplerLoadOptions { DecodeSamples = false };
        var unresolved = new List<string>();
        var presets = 0;

        //Act
        foreach (var path in Sources())
        {
            using var instrument = DecentSamplerInstrument.Load(path, options);
            presets++;

            unresolved.AddRange(instrument.Problems
                .Where(problem => problem.StartsWith("Unresolved binding:", StringComparison.Ordinal))
                .Select(problem => instrument.Name + ": " + problem));
        }

        //Assert
        presets.Should().BeGreaterThan(0);
        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void every_preset_in_the_corpus_builds_a_control_surface_and_applies_its_initial_state()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var options = new DecentSamplerLoadOptions { DecodeSamples = false };
        var controls = 0;
        var presets = 0;

        //Act
        foreach (var path in Sources())
        {
            using var instrument = DecentSamplerInstrument.Load(path, options);
            presets++;
            controls += instrument.Controls.Count;

            instrument.Controls.Should().AllSatisfy(control =>
                control.Index.Should().BeGreaterThanOrEqualTo(0));
            instrument.Groups.Should().AllSatisfy(group =>
                group.Volume.Should().BeGreaterThanOrEqualTo(0.0));
        }

        //Assert
        presets.Should().BeGreaterThan(0);
        controls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void no_corpus_preset_reports_an_unknown_binding_level()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var options = new DecentSamplerLoadOptions { DecodeSamples = false };
        var reported = new List<string>();

        //Act
        foreach (var path in Sources())
        {
            using var instrument = DecentSamplerInstrument.Load(path, options);

            reported.AddRange(instrument.Problems
                .Where(problem => problem.Contains("binding@level", StringComparison.Ordinal)));
        }

        //Assert
        reported.Should().BeEmpty();
    }

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dslibrary", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dsbundle", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal);
}

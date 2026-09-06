using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Runs the parser and the loader over a folder of real sample libraries, which is the only way to know
/// whether the format support survives contact with libraries nobody here wrote. Opt-in: point
/// CODEBRIX_AUDIO_DS_CORPUS at a folder of unpacked libraries. Without it the tests skip. Nothing from
/// the corpus is ever copied into the repository.
/// </summary>
/// <remarks>
/// Sample decoding is off. A nine-library corpus decodes to several gigabytes of 32-bit float, one
/// library 2.6 GB on its own, so the corpus run resolves every sample path without decoding a byte -
/// which is what these tests are checking anyway.
/// </remarks>
public class DecentSamplerCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_preset_in_the_corpus_parses_with_no_unrecognised_element_or_attribute()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var unrecognised = new SortedSet<string>(StringComparer.Ordinal);
        var presets = 0;

        //Act
        foreach (var preset in Presets())
        {
            presets++;

            foreach (var attribute in preset.AllUnknownAttributes())
            {
                unrecognised.Add($"attribute {attribute.ElementName}@{attribute.Name}");
            }

            foreach (var element in preset.AllUnknownElements())
            {
                unrecognised.Add($"element <{element.Name}> under <{element.ParentElementName}>");
            }
        }

        //Assert
        presets.Should().BeGreaterThan(0);
        unrecognised.Should().BeEmpty();
    }

    [Fact]
    public void every_sample_in_the_corpus_resolves_to_a_real_file()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var options = new DecentSamplerLoadOptions { DecodeSamples = false };
        var unresolved = new List<string>();
        var zones = 0;

        //Act
        foreach (var path in Sources())
        {
            using var instrument = DecentSamplerInstrument.Load(path, options);

            foreach (var zone in instrument.Zones.Where(zone => zone.Path != null))
            {
                zones++;

                if (instrument.GetResolvedSamplePath(zone) == null)
                {
                    unresolved.Add($"{instrument.Name}: {zone.Path}");
                }
            }
        }

        //Assert
        zones.Should().BeGreaterThan(0);
        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void every_preset_in_the_corpus_resolves_into_groups_and_zones()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var zones = 0;

        //Act
        foreach (var preset in Presets())
        {
            foreach (var group in preset.ResolveGroups())
            {
                foreach (var zone in group.Zones)
                {
                    zones++;
                    zone.LoNote.Should().BeInRange(0, 127);
                    zone.HiNote.Should().BeInRange(0, 127);
                    zone.Volume.Should().BeGreaterThanOrEqualTo(0.0);
                }
            }
        }

        //Assert
        zones.Should().BeGreaterThan(0);
    }

    private static IEnumerable<string> Sources()
    {
        foreach (var path in Directory
                     .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dslibrary", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dsbundle", SearchOption.AllDirectories))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return path;
        }
    }

    private static IEnumerable<DecentSamplerPreset> Presets()
    {
        foreach (var path in Sources())
        {
            if (path.EndsWith(".dspreset", StringComparison.OrdinalIgnoreCase))
            {
                yield return DecentSamplerParser.ParseFile(path);
                continue;
            }

            using var archive = new DecentSamplerArchiveContainer(path);

            foreach (var entry in archive.FindPresets())
            {
                using var stream = archive.OpenFile(entry);
                yield return DecentSamplerParser.Parse(stream, path);
            }
        }
    }
}

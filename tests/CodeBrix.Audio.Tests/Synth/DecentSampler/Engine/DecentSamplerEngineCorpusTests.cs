using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Builds a synthesizer for every preset in a folder of real sample libraries and renders a bar of a
/// C-major chord through each, which is the only way to know the engine survives contact with libraries
/// nobody here wrote. Opt-in: point CODEBRIX_AUDIO_DS_CORPUS at a folder of unpacked libraries. Nothing
/// from the corpus is ever copied into the repository.
/// </summary>
/// <remarks>
/// ONE LIBRARY AT A TIME, disposed before the next. The nine-library corpus this was written against
/// decodes to 4.7 GB of 32-bit float, one library 2.6 GB on its own, so holding two at once is the
/// difference between a test run and a swap storm.
/// </remarks>
public class DecentSamplerEngineCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const int SampleRate = 44100;

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_corpus_preset_builds_a_synthesizer_and_renders_a_bar()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var report = new StringBuilder();
        var presets = 0;
        var failures = new List<string>();

        //Act
        foreach (var path in Sources())
        {
            try
            {
                presets += RenderOne(path, report);
            }
            catch (Exception exception)
            {
                failures.Add(Path.GetFileName(path) + ": " + exception);
            }

            // The whole library's decoded audio is released before the next one is opened.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        failures.Should().BeEmpty();
        presets.Should().BeGreaterThan(0);
    }

    // Loads one library, renders a bar of a C-major chord through every preset it holds, and appends a
    // line per preset: how many voices sounded and how loud it was.
    private static int RenderOne(string path, StringBuilder report)
    {
        var presetNames = PresetNamesIn(path);
        var rendered = 0;

        foreach (var presetName in presetNames)
        {
            var options = presetName == null
                ? null
                : new DecentSamplerLoadOptions { PresetName = presetName };

            using var instrument = DecentSamplerInstrument.Load(path, options);

            var synthesizer = new DecentSamplerSynthesizer(instrument, SampleRate);
            var chord = MidRangeChord(instrument);

            foreach (var note in chord)
            {
                synthesizer.NoteOn(0, note, 100);
            }

            var voices = synthesizer.ActiveVoiceCount;

            // One bar at 120 BPM is two seconds; the note is held for it and released at the end.
            var left = new float[SampleRate * 2];
            var right = new float[left.Length];
            synthesizer.Render(left, right);

            foreach (var note in chord)
            {
                synthesizer.NoteOff(0, note);
            }

            var tailLeft = new float[SampleRate / 2];
            var tailRight = new float[tailLeft.Length];
            synthesizer.Render(tailLeft, tailRight);

            var peak = Math.Max(Peak(left), Peak(right));

            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-52} notes {1,-14} voices {2,4}  peak {3:0.0000}  problems {4}",
                instrument.Name,
                string.Join(",", chord),
                voices,
                peak,
                instrument.Problems.Count));

            rendered++;
        }

        return rendered;
    }

    // The names of the presets a container holds; a single .dspreset yields one null, meaning "the only
    // preset there is".
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

    // Three notes a major triad apart, chosen inside the range the instrument actually maps, so a
    // library that only covers two octaves still sounds.
    private static int[] MidRangeChord(DecentSamplerInstrument instrument)
    {
        var low = 127;
        var high = 0;

        foreach (var zone in instrument.Zones)
        {
            low = Math.Min(low, zone.LoNote);
            high = Math.Max(high, zone.HiNote);
        }

        if (low > high)
        {
            return [60];
        }

        var root = Math.Clamp((low + high) / 2, low, Math.Max(low, high - 7));

        return
        [
            root,
            Math.Min(high, root + 4),
            Math.Min(high, root + 7),
        ];
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

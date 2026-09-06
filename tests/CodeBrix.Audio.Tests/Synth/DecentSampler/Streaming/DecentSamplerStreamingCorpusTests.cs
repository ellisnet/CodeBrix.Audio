using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// The two libraries the memory policy exists for, loaded under a budget a quarter their decoded size
/// and rendered. Opt-in: point CODEBRIX_AUDIO_DS_CORPUS at a folder of unpacked libraries. Nothing from
/// the corpus is ever copied into the repository.
/// </summary>
/// <remarks>
/// The survey these thresholds came from: eagerly decoding the nine-library corpus costs 4.7 GB of
/// 32-bit float, of which Global Swarm is 2.6 GB on its own and The Spellsinger nearly 1 GB at 96 kHz.
/// The point of this test is that a 256 MB budget holds and the audio still comes out.
/// </remarks>
public class DecentSamplerStreamingCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const long BudgetBytes = 256L * 1024 * 1024;

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Theory]
    [InlineData("Global Swarm")]
    [InlineData("Spellsinger")]
    public void a_large_library_loads_under_the_budget_and_renders_a_bar(string libraryName)
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var preset = FindPreset(libraryName);
        Assert.SkipWhen(preset == null, "The corpus folder has no library called " + libraryName + ".");

        var report = new StringBuilder();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetBefore = Environment.WorkingSet;

        //Act
        using var instrument = DecentSamplerInstrument.Load(preset, new DecentSamplerLoadOptions
        {
            InstrumentMemoryBudgetBytes = BudgetBytes,
        });

        var managedAfterLoad = GC.GetTotalMemory(forceFullCollection: true);

        var synthesizer = new DecentSamplerSynthesizer(instrument, new DecentSamplerSynthesizerSettings(44100)
        {
            StreamingMode = DecentSamplerStreamingMode.Offline,
            MasterVolume = 1f,
        });

        // One bar at 120 BPM is two seconds.
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        synthesizer.NoteOn(0, 67, 100);

        var left = new float[44100 * 2];
        var right = new float[44100 * 2];
        synthesizer.Render(left, right);

        var managedAfterRender = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetAfter = Environment.WorkingSet;

        report.AppendLine(CultureInfo.InvariantCulture, $"library: {Path.GetFileName(preset)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"zones: {instrument.Zones.Count}");
        report.AppendLine(CultureInfo.InvariantCulture, $"{instrument.MemoryPolicySummary}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"in memory: {Megabytes(instrument.DecodedByteCount)}, streamed samples: {instrument.StreamedSampleCount}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"managed before: {Megabytes(managedBefore)}, after load: {Megabytes(managedAfterLoad)}, " +
            $"after render: {Megabytes(managedAfterRender)}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"working set before: {Megabytes(workingSetBefore)}, after: {Megabytes(workingSetAfter)}");

        var underruns = instrument.Problems
            .Where(problem => problem.Contains("streaming underrun on", StringComparison.Ordinal))
            .ToArray();

        foreach (var problem in underruns)
        {
            report.AppendLine(problem);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());

        //Assert
        instrument.DecodedByteCount.Should().BeLessThanOrEqualTo(BudgetBytes);
        underruns.Should().BeEmpty();
        DecentSamplerRenderProbe.Peak(left).Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_whole_corpus_loads_without_decoding_a_byte()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var report = new StringBuilder();
        var loaded = 0;

        // THREAD-LOCAL, deliberately. GC.GetTotalMemory is process-wide, and this assembly runs its
        // tests in parallel, so a heap reading taken here also counts whatever every other test was
        // holding at that instant - which made this assertion fail at random. Allocated-bytes-for-
        // this-thread counts only what THIS test caused, including its garbage, so it is both stricter
        // and immune to the neighbours.
        var before = GC.GetAllocatedBytesForCurrentThread();

        //Act - every preset in the corpus, held at once, with nothing decoded.
        var instruments = new List<DecentSamplerInstrument>();

        try
        {
            foreach (var preset in Presets())
            {
                instruments.Add(DecentSamplerInstrument.Load(
                    preset, new DecentSamplerLoadOptions { DecodeSamples = false }));
                loaded++;
            }

            var after = GC.GetAllocatedBytesForCurrentThread();

            report.AppendLine(CultureInfo.InvariantCulture,
                $"{loaded} presets held at once; this thread allocated {Megabytes(after - before)} " +
                $"building every model");
            TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());

            //Assert - the whole corpus decodes to 4.7 GB; building the model of all of it, garbage
            // included, costs a fraction of that and not one audio file is opened.
            loaded.Should().BeGreaterThan(0);
            instruments.Should().AllSatisfy(instrument => instrument.DecodedByteCount.Should().Be(0));
            (after - before).Should().BeLessThan(BudgetBytes);
        }
        finally
        {
            foreach (var instrument in instruments)
            {
                instrument.Dispose();
            }
        }
    }

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    private static string FindPreset(string libraryName)
    {
        foreach (var directory in Directory.EnumerateDirectories(CorpusFolder))
        {
            if (!Path.GetFileName(directory).Contains(libraryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var preset = Directory
                .EnumerateFiles(directory, "*.dspreset", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();

            if (preset != null)
            {
                return preset;
            }
        }

        return null;
    }

    private static IEnumerable<string> Presets() =>
        Directory
            .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
}

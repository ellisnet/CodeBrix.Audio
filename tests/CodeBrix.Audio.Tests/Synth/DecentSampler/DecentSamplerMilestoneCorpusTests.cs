using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// The milestone the whole Decent Sampler engine was built for: every preset in a folder of real
/// sample libraries loads with nothing unsupported, nothing unexplained in its problems, and plays a
/// bar through the same offline path a consumer renders a MIDI file with. Opt-in: point
/// <c>CODEBRIX_AUDIO_DS_CORPUS</c> at a folder of unpacked libraries. Nothing from the corpus is ever
/// copied into the repository.
/// </summary>
/// <remarks>
/// <para>
/// ONE LIBRARY AT A TIME, disposed before the next, under an explicit memory budget. The corpus this
/// was written against decodes to several gigabytes of 32-bit float, one library over two of them on
/// its own, so the budget is what makes the pass survivable - and proving the memory policy holds it
/// is half the point of the test.
/// </para>
/// <para>
/// The render goes through <see cref="SoundFontRenderer"/>, which is the offline half of what
/// <c>MidiMusicPlayer</c> drives: the same instrument, the same synthesizer, the same sequencer, with
/// the streaming mode switched to offline because a render that runs faster than real time must not
/// depend on a background reader.
/// </para>
/// </remarks>
public class DecentSamplerMilestoneCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const int SampleRate = 44100;

    /// <summary>
    /// The memory budget every library is loaded under. Well below what the largest preset would
    /// decode to, so the streaming policy has to do its work.
    /// </summary>
    private const long MemoryBudgetBytes = 256L * 1024 * 1024;

    /// <summary>
    /// The authoring mistakes the libraries themselves carry. Every one is a real typo in a shipped
    /// preset, reported exactly as it should be and NOT worked around in the parser. A problem line
    /// this list does not cover is a defect in the engine.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <c>damping="O.2"</c> - a capital letter O where a zero belongs, on a reverb. Six presets across
    /// five libraries.
    /// </description></item>
    /// <item><description>
    /// A library distributed through the store carries a <c>productId</c>, which is reported and never
    /// acted on.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static readonly string[] KnownAuthoringMistakes =
    [
        "effect@damping=\"O.2\" is not a number",
        "productId",
    ];

    /// <summary>
    /// Lines the ENGINE reports about its own decisions rather than about the preset. They belong in
    /// <c>Problems</c> because something the preset asked for was not done as written, and they are
    /// not defects.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// "memory policy: ..." - the instrument memory budget forced samples to stream that the format's
    /// own default would have decoded into memory. The whole line is also on
    /// <see cref="DecentSamplerInstrument.MemoryPolicySummary"/>.
    /// </description></item>
    /// <item><description>
    /// "round robin" - a preset mixes round-robin sets of different lengths, so the instrument-wide
    /// sequence length leaves some positions with no zone. The reference player does the same.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static readonly string[] KnownEngineReports =
    [
        "memory policy:",
        "round robin",
    ];

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_corpus_preset_loads_clean_and_plays_a_bar_through_the_offline_path()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var report = new StringBuilder();
        var unsupported = new List<string>();
        var unexplained = new List<string>();
        var overBudget = new List<string>();
        var silent = new List<string>();
        var presets = 0;

        var sequence = OneBarChord();

        //Act
        foreach (var path in Sources())
        {
            foreach (var presetName in PresetNamesIn(path))
            {
                using (var instrument = Load(path, presetName))
                {
                    presets++;

                    foreach (var feature in instrument.UnsupportedFeatures)
                    {
                        unsupported.Add(instrument.Name + ": " + feature);
                    }

                    foreach (var problem in instrument.Problems)
                    {
                        if (!KnownAuthoringMistakes.Concat(KnownEngineReports).Any(known =>
                                problem.Contains(known, StringComparison.Ordinal)))
                        {
                            unexplained.Add(instrument.Name + ": " + problem);
                        }
                    }

                    if (instrument.DecodedByteCount > MemoryBudgetBytes)
                    {
                        overBudget.Add(instrument.Name + ": " +
                            (instrument.DecodedByteCount / 1024 / 1024).ToString(CultureInfo.InvariantCulture) +
                            " MB held");
                    }

                    var samples = SoundFontRenderer.Render(
                        instrument, Transposed(sequence, instrument), SampleRate, TimeSpan.FromSeconds(0.5));

                    var peak = Peak(samples);

                    if (peak <= 0.0)
                    {
                        silent.Add(instrument.Name);
                    }

                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-52} held {1,7} MB  streamed {2,4}  peak {3:0.0000}  problems {4}",
                        instrument.Name,
                        instrument.DecodedByteCount / 1024 / 1024,
                        instrument.StreamedSampleCount,
                        peak,
                        instrument.Problems.Count));
                }

                // The whole library's decoded audio is released before the next preset is opened.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        unsupported.Should().BeEmpty();
        unexplained.Should().BeEmpty();
        overBudget.Should().BeEmpty();
        silent.Should().BeEmpty();
        presets.Should().BeGreaterThan(0);
    }

    [Fact]
    public void every_corpus_preset_reports_what_its_memory_policy_decided()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var missing = new List<string>();
        var presets = 0;

        //Act
        foreach (var path in Sources())
        {
            foreach (var presetName in PresetNamesIn(path))
            {
                using (var instrument = Load(path, presetName))
                {
                    presets++;

                    if (instrument.Zones.Count > 0 &&
                        string.IsNullOrWhiteSpace(instrument.MemoryPolicySummary))
                    {
                        missing.Add(instrument.Name);
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        //Assert
        missing.Should().BeEmpty();
        presets.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static DecentSamplerInstrument Load(string path, string presetName) =>
        DecentSamplerInstrument.Load(
            path,
            new DecentSamplerLoadOptions
            {
                PresetName = presetName,
                InstrumentMemoryBudgetBytes = MemoryBudgetBytes,
            });

    // One bar of a C-major triad at 120 BPM, held for the whole bar.
    private static MidiSequence OneBarChord()
    {
        const int ticksPerQuarter = 480;
        var events = new MidiEventCollection(1, ticksPerQuarter);

        events.AddEvent(new TempoEvent(500000, 0), 1);

        foreach (var note in new[] { 60, 64, 67 })
        {
            events.AddEvent(new NoteOnEvent(0, 1, note, 100, ticksPerQuarter * 4), 1);
            events.AddEvent(
                new NoteEvent(ticksPerQuarter * 4, 1, MidiCommandCode.NoteOff, note, 0), 1);
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    // Moves the bar into a key range the instrument actually maps, because a library of evolving
    // textures may cover two octaves somewhere else entirely.
    private static MidiSequence Transposed(MidiSequence sequence, DecentSamplerInstrument instrument)
    {
        var shift = RootOf(instrument) - 60;
        return shift == 0 ? sequence : Rebuild(shift);
    }

    private static MidiSequence Rebuild(int shift)
    {
        const int ticksPerQuarter = 480;
        var events = new MidiEventCollection(1, ticksPerQuarter);

        events.AddEvent(new TempoEvent(500000, 0), 1);

        foreach (var note in new[] { 60, 64, 67 })
        {
            var key = Math.Clamp(note + shift, 0, 127);
            events.AddEvent(new NoteOnEvent(0, 1, key, 100, ticksPerQuarter * 4), 1);
            events.AddEvent(new NoteEvent(ticksPerQuarter * 4, 1, MidiCommandCode.NoteOff, key, 0), 1);
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    private static int RootOf(DecentSamplerInstrument instrument)
    {
        var low = 127;
        var high = 0;

        foreach (var zone in instrument.Zones)
        {
            low = Math.Min(low, zone.LoNote);
            high = Math.Max(high, zone.HiNote);
        }

        return low > high ? 60 : Math.Clamp((low + high) / 2, low, Math.Max(low, high - 7));
    }

    private static double Peak(float[] interleaved)
    {
        var peak = 0.0;

        foreach (var sample in interleaved)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
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

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dslibrary", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dsbundle", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal);
}

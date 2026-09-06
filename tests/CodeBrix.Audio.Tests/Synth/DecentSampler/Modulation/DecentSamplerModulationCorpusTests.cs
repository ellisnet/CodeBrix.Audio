using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// Proves the modulator runtime changes nothing for a preset that declares no modulators, which is
/// every preset in the reference corpus. Opt-in: point CODEBRIX_AUDIO_DS_CORPUS at a folder of
/// unpacked libraries. Nothing from the corpus is ever copied into the repository.
/// </summary>
/// <remarks>
/// The comparison is against a render made by the same build with the runtime switched off
/// (<see cref="DecentSamplerSynthesizerSettings.EnableModulators"/>), so it is a real before-and-after
/// rather than a stored expectation that could drift.
/// </remarks>
public class DecentSamplerModulationCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const int SampleRate = 44100;

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void every_corpus_preset_renders_the_same_with_the_modulator_runtime_on_or_off()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var presets = 0;
        var differences = new List<string>();

        //Act
        foreach (var path in Sources())
        {
            foreach (var presetName in PresetNamesIn(path))
            {
                var options = presetName == null
                    ? null
                    : new DecentSamplerLoadOptions { PresetName = presetName };

                using (var instrument = DecentSamplerInstrument.Load(path, options))
                {
                    var withRuntime = Render(instrument, modulators: true);
                    var withoutRuntime = Render(instrument, modulators: false);

                    if (!withRuntime.SequenceEqual(withoutRuntime))
                    {
                        differences.Add(
                            Path.GetFileName(path) + " / " + (presetName ?? "(only preset)") +
                            ": modulators " + instrument.Modulators.Count);
                    }

                    presets++;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        //Assert
        differences.Should().BeEmpty();
        presets.Should().BeGreaterThan(0);
    }

    private static float[] Render(DecentSamplerInstrument instrument, bool modulators)
    {
        var settings = new DecentSamplerSynthesizerSettings(SampleRate)
        {
            MasterVolume = 1f,
            EnableModulators = modulators,

            // The comparison is BYTE FOR BYTE and neither render is an audio-device callback, so the
            // streamed presets in the corpus - Global Swarm, The Spellsinger, several Winter Voices
            // sets - must not be read by the background reader thread: a render that runs faster than
            // real time outruns it and writes starved blocks as silence.
            StreamingMode = DecentSamplerStreamingMode.Offline,
        };

        var synthesizer = new DecentSamplerSynthesizer(instrument, settings);

        foreach (var note in Chord(instrument))
        {
            synthesizer.NoteOn(0, note, 100);
        }

        var left = new float[SampleRate];
        var right = new float[left.Length];
        synthesizer.Render(left, right);

        var interleaved = new float[left.Length * 2];

        for (var frame = 0; frame < left.Length; frame++)
        {
            interleaved[frame * 2] = left[frame];
            interleaved[(frame * 2) + 1] = right[frame];
        }

        return interleaved;
    }

    private static int[] Chord(DecentSamplerInstrument instrument)
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

        return [root, Math.Min(high, root + 4), Math.Min(high, root + 7)];
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

    private static IEnumerable<string> Sources()
    {
        string[] patterns = ["*.dspreset", "*.dslibrary", "*.dsbundle"];

        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(CorpusFolder, pattern, SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }
}

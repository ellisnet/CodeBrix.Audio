using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// What making the <c>&lt;midi&gt;</c> element live did to real libraries. Opt-in: point
/// <c>CODEBRIX_AUDIO_DS_CORPUS</c> at a folder of unpacked Decent Sampler libraries. Nothing from the
/// corpus is ever copied into the repository.
/// </summary>
/// <remarks>
/// The corpus uses <c>&lt;cc&gt;</c> handlers only - no sequences and no arpeggiator - so the two
/// things worth proving are that a controller nothing binds changes NOTHING, byte for byte, and that
/// a controller a preset does bind changes the sound. The first is the regression guard for every
/// phase before this one; the second is the feature.
/// </remarks>
public class DecentSamplerSequencingCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_DS_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of unpacked Decent Sampler libraries to run the corpus tests.";

    private const int SampleRate = 44100;
    private const int RenderFrames = SampleRate / 2;

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void an_unbound_controller_leaves_every_corpus_preset_byte_identical()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var report = new StringBuilder();
        var differences = new List<string>();
        var presets = 0;

        //Act
        foreach (var path in Sources())
        {
            foreach (var name in PresetNamesIn(path))
            {
                using var instrument = Load(path, name);
                var bound = BoundControllers(instrument);

                var quiet = Render(instrument, null);
                var noisy = Render(instrument, Unbound(bound));

                presets++;

                if (!quiet.SequenceEqual(noisy))
                {
                    differences.Add(instrument.Name);
                }

                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-52} bound CCs {1}",
                    instrument.Name,
                    bound.Count == 0 ? "none" : string.Join(",", bound)));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        differences.Should().BeEmpty();
        presets.Should().BeGreaterThan(0);
    }

    [Fact]
    public void a_bound_controller_changes_what_a_corpus_preset_renders()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var tested = 0;
        var changed = 0;
        var deaf = new List<string>();
        var inaudible = new List<string>();

        //Act
        foreach (var path in Sources())
        {
            foreach (var name in PresetNamesIn(path))
            {
                using var instrument = Load(path, name);
                var bound = BoundControllers(instrument);

                if (bound.Count == 0)
                {
                    continue;
                }

                tested++;

                // EVERY preset's handler must fire and write a parameter. ParameterVersion counts
                // exactly that, and is the assertion that holds whatever the preset wires its knob to.
                var before = instrument.ParameterVersion;

                foreach (var controller in bound)
                {
                    // The value cannot be 0: a controller starts at 0 and a <cc> binding fires on a
                    // change, both measured against the reference player.
                    RenderNothing(instrument, controller, 127);
                }

                if (instrument.ParameterVersion == before)
                {
                    deaf.Add(instrument.Name);
                }

                // Then the audible half. Comparing the two ENDS of the controller rather than
                // "before and after" is what makes this work on a preset whose knobs already sit at
                // the value a full controller would send - Micah's Choir opens with both of its
                // MIDI-driven knobs at 1.0. A preset can still be inaudible at both ends: Winter
                // Voices drives a 60-to-22 000 Hz filter knob over a range of 0 to 1, so every
                // controller value lands under the filter's own one-hertz floor.
                var low = Render(instrument, bound.Select(number => (number, 1)).ToArray());
                var high = Render(instrument, bound.Select(number => (number, 127)).ToArray());

                if (low.SequenceEqual(high))
                {
                    inaudible.Add(instrument.Name);
                }
                else
                {
                    changed++;
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        //Assert - every <cc> handler fires, and the sound follows for all but the odd preset whose
        // own translation range cannot reach anything audible.
        TestContext.Current.TestOutputHelper?.WriteLine(
            "presets with a <cc> handler: " + tested.ToString(CultureInfo.InvariantCulture) +
            ", audibly changed: " + changed.ToString(CultureInfo.InvariantCulture) +
            ", inaudible at both ends of the controller: " + string.Join(", ", inaudible));

        tested.Should().BeGreaterThan(0);
        deaf.Should().BeEmpty();
        changed.Should().BeGreaterThan(0);
    }

    // Sends one controller through a throwaway synthesizer, which is enough to fire the instrument's
    // own <cc> handlers: they live on the instrument, not on the synthesizer.
    private static void RenderNothing(DecentSamplerInstrument instrument, int controller, int value)
    {
        var synthesizer = new DecentSamplerSynthesizer(instrument, OfflineSettings());
        synthesizer.ProcessMidiMessage(0, 0xB0, controller, value);
    }

    // These tests compare two renders BYTE FOR BYTE, and neither render is an audio-device callback:
    // they run as fast as the machine allows. A preset the memory policy streams - Global Swarm, The
    // Spellsinger, several Winter Voices sets - would outrun the real-time mode's background reader,
    // and a starved block is written as silence, so the comparison would be against a race rather than
    // against the engine. Offline is the mode for any render that is not a callback.
    private static DecentSamplerSynthesizerSettings OfflineSettings() =>
        new DecentSamplerSynthesizerSettings(SampleRate)
        {
            StreamingMode = DecentSamplerStreamingMode.Offline,
        };

    // The controller numbers the preset's <midi><cc> handlers listen on.
    private static IReadOnlyList<int> BoundControllers(DecentSamplerInstrument instrument)
    {
        var numbers = new List<int>();

        foreach (var handler in instrument.MidiHandlers)
        {
            if (handler is DecentSamplerMidiCc cc && cc.Number != null &&
                !numbers.Contains(cc.Number.Value))
            {
                numbers.Add(cc.Number.Value);
            }
        }

        numbers.Sort();
        return numbers;
    }

    // Six controllers the preset does not listen on, so sending them must change nothing.
    private static (int Controller, int Value)[] Unbound(IReadOnlyList<int> bound)
    {
        var messages = new List<(int, int)>();

        for (var controller = 127; controller >= 0 && messages.Count < 6; controller--)
        {
            if (!bound.Contains(controller) && controller != 64 && controller < 120)
            {
                messages.Add((controller, 100));
            }
        }

        return [.. messages];
    }

    // Renders half a second of a mid-range chord, optionally after a set of controller messages.
    private static float[] Render(
        DecentSamplerInstrument instrument, (int Controller, int Value)[] controllers)
    {
        var synthesizer = new DecentSamplerSynthesizer(instrument, OfflineSettings());

        if (controllers != null)
        {
            foreach (var (controller, value) in controllers)
            {
                synthesizer.ProcessMidiMessage(0, 0xB0, controller, value);
            }
        }

        foreach (var note in MidRangeChord(instrument))
        {
            synthesizer.NoteOn(0, note, 100);
        }

        var left = new float[RenderFrames];
        var right = new float[RenderFrames];
        synthesizer.Render(left, right);

        var both = new float[RenderFrames * 2];
        Array.Copy(left, 0, both, 0, RenderFrames);
        Array.Copy(right, 0, both, RenderFrames, RenderFrames);
        return both;
    }

    private static DecentSamplerInstrument Load(string path, string presetName) =>
        DecentSamplerInstrument.Load(
            path,
            presetName == null ? null : new DecentSamplerLoadOptions { PresetName = presetName });

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

    private static IEnumerable<string> Sources() =>
        Directory
            .EnumerateFiles(CorpusFolder, "*.dspreset", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dslibrary", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(CorpusFolder, "*.dsbundle", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal);
}

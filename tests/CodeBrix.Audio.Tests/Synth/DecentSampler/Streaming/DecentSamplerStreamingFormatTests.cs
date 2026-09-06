using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// Streaming out of every container and every sample format the guide names: a folder of WAV, AIFF and
/// FLAC, and a <c>.dslibrary</c> archive read in place.
/// </summary>
/// <remarks>
/// An archive entry is the awkward case. A deflated entry cannot be seeked, so the streaming source
/// gets a view that re-opens the entry and skips forward, and these tests are what proves that view
/// hands out the same audio a plain file would.
/// </remarks>
public class DecentSamplerStreamingFormatTests
{
    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="{MODE}">
              <sample path="{PATH}" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_streamed_flac_zone_matches_the_decoded_one()
    {
        //Arrange
        using var presets = DecentSamplerTestPresets.Create();
        presets.WriteFlac("Samples/tone.flac");

        //Act
        var difference = Compare(presets, "Samples/tone.flac", blocks: 200);

        //Assert
        difference.Should().Be(-1);
    }

    [Fact]
    public void a_streamed_aiff_zone_matches_the_decoded_one()
    {
        //Arrange
        using var presets = DecentSamplerTestPresets.Create();
        presets.WriteAiff("Samples/tone.aiff", value: 0.5f, frames: 20000);

        //Act
        var difference = Compare(presets, "Samples/tone.aiff", blocks: 200);

        //Assert
        difference.Should().Be(-1);
    }

    [Fact]
    public void a_streamed_zone_inside_a_dslibrary_archive_matches_the_decoded_one()
    {
        //Arrange - the same preset and sample, packed into a zip and read in place.
        using var presets = DecentSamplerTestPresets.Create();
        presets.WriteWav("Samples/tone.wav", value: 0.25f, frames: 30000);

        var memoryPreset = WritePreset(presets, "memory", "Samples/tone.wav", "memory.dspreset");
        var streamingPreset = WritePreset(
            presets, "disk_streaming", "Samples/tone.wav", "streaming.dspreset");

        var archive = presets.BuildLibraryArchive(
            "library.dslibrary", "Lib", "Samples/tone.wav", memoryPreset, streamingPreset);

        var options = new DecentSamplerLoadOptions { StreamingPreloadFrames = 64 };

        using var decoded = DecentSamplerInstrument.Load(
            archive, Options(options, "memory"));
        using var streamed = DecentSamplerInstrument.Load(
            archive, Options(options, "streaming"));

        //Act
        var memory = DecentSamplerStreamingWorld.Play(decoded, 60, 300);
        var fromArchive = DecentSamplerStreamingWorld.Play(streamed, 60, 300);

        //Assert
        streamed.StreamedSampleCount.Should().Be(1);
        decoded.StreamedSampleCount.Should().Be(0);
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, fromArchive.Left).Should().Be(-1);
        DecentSamplerRenderProbe.Rms(fromArchive.Left).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void a_streamed_archive_zone_loops_across_the_seam()
    {
        //Arrange - a loop inside an archive is the case that makes the entry view seek backwards.
        using var presets = DecentSamplerTestPresets.Create();
        presets.WriteWav("Samples/tone.wav", value: 0.25f, frames: 30000);

        const string looped = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="{MODE}">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127"
                          loopStart="10000" loopEnd="19999" loopEnabled="true" />
                </group>
              </groups>
            </DecentSampler>
            """;

        presets.WritePreset(looped.Replace("{MODE}", "memory", StringComparison.Ordinal), "memory.dspreset");
        presets.WritePreset(
            looped.Replace("{MODE}", "disk_streaming", StringComparison.Ordinal), "streaming.dspreset");

        var archive = presets.BuildLibraryArchive(
            "library.dslibrary", "Lib", "Samples/tone.wav", "memory.dspreset", "streaming.dspreset");

        var options = new DecentSamplerLoadOptions { StreamingPreloadFrames = 64 };

        using var decoded = DecentSamplerInstrument.Load(archive, Options(options, "memory"));
        using var streamed = DecentSamplerInstrument.Load(archive, Options(options, "streaming"));

        //Act
        var memory = DecentSamplerStreamingWorld.Play(decoded, 60, 700);
        var fromArchive = DecentSamplerStreamingWorld.Play(streamed, 60, 700);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, fromArchive.Left).Should().Be(-1);
    }

    private static DecentSamplerLoadOptions Options(DecentSamplerLoadOptions options, string presetName)
    {
        var copy = options.Clone();
        copy.PresetName = presetName;
        return copy;
    }

    private static string WritePreset(
        DecentSamplerTestPresets presets, string mode, string samplePath, string fileName)
    {
        presets.WritePreset(
            Preset.Replace("{MODE}", mode, StringComparison.Ordinal)
                  .Replace("{PATH}", samplePath, StringComparison.Ordinal),
            fileName);

        return fileName;
    }

    private static int Compare(DecentSamplerTestPresets presets, string samplePath, int blocks)
    {
        var options = new DecentSamplerLoadOptions { StreamingPreloadFrames = 64 };

        using var decoded = DecentSamplerInstrument.Load(
            presets.WritePreset(
                Preset.Replace("{MODE}", "memory", StringComparison.Ordinal)
                      .Replace("{PATH}", samplePath, StringComparison.Ordinal),
                "memory.dspreset"),
            options);

        using var streamed = DecentSamplerInstrument.Load(
            presets.WritePreset(
                Preset.Replace("{MODE}", "disk_streaming", StringComparison.Ordinal)
                      .Replace("{PATH}", samplePath, StringComparison.Ordinal),
                "streaming.dspreset"),
            options);

        streamed.StreamedSampleCount.Should().Be(1);

        var memory = DecentSamplerStreamingWorld.Play(decoded, 60, blocks);
        var fromDisk = DecentSamplerStreamingWorld.Play(streamed, 60, blocks);

        DecentSamplerRenderProbe.Rms(fromDisk.Left).Should().BeGreaterThan(0.01);

        return DecentSamplerStreamingWorld.FirstDifference(memory.Left, fromDisk.Left);
    }
}

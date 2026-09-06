using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// Loading a library without decoding it, and decoding a sample the first time a note wants it.
/// </summary>
/// <remarks>
/// This is what lets a corpus test load nine libraries that would decode to 4.7 GB, and what lets a
/// preset browser open a library without paying for its audio. The price is that the first note on a
/// zone is silent, which the instrument reports once per file.
/// </remarks>
public class DecentSamplerLazyDecodeTests
{
    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void loading_without_decoding_opens_no_audio_and_still_resolves_the_path()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", frames: 20000);

        //Act
        using var instrument = Load(fixtures);

        //Assert
        instrument.DecodedSampleCount.Should().Be(0);
        instrument.DecodedByteCount.Should().Be(0);
        instrument.PendingDecodeCount.Should().Be(1);
        instrument.GetResolvedSamplePath(instrument.Zones[0]).Should().NotBeNull();
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void the_first_note_is_silent_and_the_second_one_sounds()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);
        using var instrument = Load(fixtures);

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
            settings.StreamingMode = DecentSamplerStreamingMode.Offline);

        //Act - the first note asks for the file; the render call that follows decodes it.
        synthesizer.NoteOn(0, 60, 100);
        var (firstLeft, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 20);

        synthesizer.NoteOff(0, 60);
        synthesizer.NoteOn(0, 60, 100);
        var (secondLeft, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 20);

        //Assert
        DecentSamplerRenderProbe.Peak(firstLeft).Should().Be(0.0);
        DecentSamplerRenderProbe.Rms(secondLeft).Should().BeGreaterThan(0.1);
        instrument.DecodedSampleCount.Should().Be(1);
        instrument.PendingDecodeCount.Should().Be(0);
    }

    [Fact]
    public void the_miss_is_reported_once()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);
        using var instrument = Load(fixtures);

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
            settings.StreamingMode = DecentSamplerStreamingMode.Offline);

        //Act
        for (var note = 0; note < 4; note++)
        {
            synthesizer.NoteOn(0, 60 + note, 100);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
            synthesizer.NoteOff(0, 60 + note);
        }

        //Assert
        instrument.Problems.Should().ContainSingle(problem =>
            problem.Contains("decoded on first use", StringComparison.Ordinal));
    }

    [Fact]
    public void a_lazily_decoded_zone_renders_what_an_eagerly_decoded_one_does()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 20000);

        using var eager = DecentSamplerInstrument.Load(fixtures.WritePreset(Preset, "eager.dspreset"));
        using var lazy = DecentSamplerInstrument.Load(
            fixtures.WritePreset(Preset, "lazy.dspreset"),
            new DecentSamplerLoadOptions { DecodeSamples = false });

        //Act
        var eagerRender = Play(eager, warmUp: false);
        var lazyRender = Play(lazy, warmUp: true);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(eagerRender, lazyRender).Should().Be(-1);
    }

    [Fact]
    public void a_missing_file_is_still_a_load_problem_without_decoding()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();

        //Act
        using var instrument = Load(fixtures);

        //Assert
        instrument.Problems.Should().ContainSingle(problem =>
            problem.Contains("sample not found", StringComparison.Ordinal));
        instrument.PendingDecodeCount.Should().Be(0);
    }

    private static DecentSamplerInstrument Load(DecentSamplerEngineFixtures fixtures) =>
        DecentSamplerInstrument.Load(
            fixtures.WritePreset(Preset), new DecentSamplerLoadOptions { DecodeSamples = false });

    private static float[] Play(DecentSamplerInstrument instrument, bool warmUp)
    {
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
            settings.StreamingMode = DecentSamplerStreamingMode.Offline);

        if (warmUp)
        {
            // The note that asks for the file is the one that goes without it.
            synthesizer.NoteOn(0, 60, 100);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);
            synthesizer.NoteOff(0, 60);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 60);
        }

        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 100);
        return left;
    }
}

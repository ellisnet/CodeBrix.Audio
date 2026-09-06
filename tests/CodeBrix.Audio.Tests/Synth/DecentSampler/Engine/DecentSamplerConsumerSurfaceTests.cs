using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The surface a CONSUMER of the package touches: every container form, the shared instrument cache,
/// the offline renderer, the streaming mode an offline render needs, the player's Decent Sampler
/// options, and the container accessor a host resolves its own files through.
/// </summary>
/// <remarks>
/// Nothing here opens an audio device. <see cref="MidiMusicPlayer"/>'s <c>Load</c> does, so only the
/// properties and the shared cache are exercised on it; the device-driven dispatch has its own opt-in
/// test in <see cref="DecentSamplerIntegrationTests"/>.
/// </remarks>
public class DecentSamplerConsumerSurfaceTests
{
    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" release="0.05">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    // The whole file streams, whatever its size, so the streaming path is exercised without a
    // hundred-megabyte fixture.
    private const string StreamingPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" release="0.05" playbackMode="disk_streaming">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    // ---------------------------------------------------------------------------------------
    // Every container form
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("preset")]
    [InlineData("folder")]
    [InlineData("dslibrary")]
    [InlineData("dsbundle")]
    public void every_container_form_loads_and_plays(string form)
    {
        //Arrange
        using var fixtures = Fixtures();
        var path = Container(fixtures, form);

        //Act
        using var instrument = DecentSamplerInstrument.Load(path);
        var synthesizer = new DecentSamplerSynthesizer(instrument, Offline());
        synthesizer.NoteOn(0, 69, 100);
        var render = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 200);

        //Assert
        instrument.Zones.Count.Should().Be(1);
        instrument.Problems.Should().BeEmpty();
        DecentSamplerRenderProbe.Rms(render.Left).Should().BeGreaterThan(0.01);
    }

    [Theory]
    [InlineData("preset")]
    [InlineData("folder")]
    [InlineData("dslibrary")]
    [InlineData("dsbundle")]
    public void the_players_shared_cache_takes_every_container_form(string form)
    {
        //Arrange
        using var fixtures = Fixtures();
        var path = Container(fixtures, form);

        //Act
        var first = MidiMusicPlayer.SharedDecentSamplerCache.Get(path);
        var second = MidiMusicPlayer.SharedDecentSamplerCache.Get(path);

        //Assert
        second.Should().BeSameAs(first);
        first.Zones.Count.Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------
    // The offline renderer and the streaming mode it needs
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void an_offline_render_of_a_streaming_preset_is_reproducible()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);
        var sequence = BuildSequence();

        //Act - two renders through the same instrument, each with a fresh synthesizer.
        var first = SoundFontRenderer.Render(instrument, sequence, 44100, TimeSpan.FromSeconds(0.25));
        var second = SoundFontRenderer.Render(instrument, sequence, 44100, TimeSpan.FromSeconds(0.25));

        //Assert
        instrument.StreamedSampleCount.Should().Be(1);
        first.Should().Equal(second);
        DecentSamplerRenderProbe.Peak(first).Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void the_renderer_takes_a_real_time_synthesizer_offline_for_the_render_and_puts_it_back()
    {
        //Arrange - a synthesizer built for a live device, handed to the offline renderer.
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);
        var synthesizer = new DecentSamplerSynthesizer(instrument, new DecentSamplerSynthesizerSettings(44100));
        var sequence = BuildSequence();

        //Act
        synthesizer.StreamingMode.Should().Be(DecentSamplerStreamingMode.RealTime);
        var samples = SoundFontRenderer.Render(synthesizer, sequence, TimeSpan.FromSeconds(0.25));

        //Assert - the render was deterministic-by-construction, and the mode came back.
        synthesizer.StreamingMode.Should().Be(DecentSamplerStreamingMode.RealTime);
        DecentSamplerRenderProbe.Peak(samples).Should().BeGreaterThan(0.01);
        instrument.Problems.Should().NotContain(problem => problem.Contains("streaming underrun"));
    }

    [Fact]
    public void the_streaming_mode_can_be_switched_on_a_built_synthesizer()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);
        var synthesizer = new DecentSamplerSynthesizer(instrument, new DecentSamplerSynthesizerSettings(44100));

        //Act
        synthesizer.StreamingMode = DecentSamplerStreamingMode.Offline;
        synthesizer.NoteOn(0, 69, 100);
        var render = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 400);

        //Assert
        synthesizer.StreamingMode.Should().Be(DecentSamplerStreamingMode.Offline);
        DecentSamplerRenderProbe.Rms(render.Left).Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void the_renderer_plays_any_midi_synthesizer_at_its_own_rate()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        var synthesizer = new DecentSamplerSynthesizer(instrument, Offline(22050));

        //Act
        var samples = SoundFontRenderer.Render(synthesizer, BuildSequence(), TimeSpan.Zero);

        //Assert - two channels at 22,050 frames a second for the sequence's own length.
        samples.Length.Should().Be((int)Math.Ceiling(BuildSequence().Length.TotalSeconds * 22050) * 2);
    }

    // ---------------------------------------------------------------------------------------
    // The player's Decent Sampler options
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void the_players_decent_sampler_options_have_the_documented_defaults()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var auxiliary = player.DropAuxiliaryOutputs;
        var mpe = player.MpeMode;
        var bendRange = player.MpeMemberBendRange;

        //Assert - auxiliary pairs fold into the mix, MPE is off, and a member bends 48 semitones.
        auxiliary.Should().BeFalse();
        mpe.Should().Be(CodeBrix.Audio.Synth.Mpe.MpeMode.Off);
        bendRange.Should().Be(48.0);
    }

    [Fact]
    public void a_synthesizer_reports_the_auxiliary_pairs_its_preset_names()
    {
        //Arrange - one group sent to the third auxiliary pair, so the count is the HIGHEST pair named.
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" output1Target="AUX_STEREO_OUTPUT_3" output1Volume="1">
                  <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var synthesizer = new DecentSamplerSynthesizer(instrument, Offline());

        //Assert
        synthesizer.AuxiliaryOutputCount.Should().Be(3);
        synthesizer.FoldAuxiliaryOutputs.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // The container a host resolves its own files through
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("folder")]
    [InlineData("dslibrary")]
    public void an_instrument_resolves_a_file_beside_its_preset_through_its_container(string form)
    {
        //Arrange
        using var fixtures = Fixtures();
        var path = Container(fixtures, form);
        using var instrument = DecentSamplerInstrument.Load(path);

        //Act - the capitalisation deliberately does not match, and the slash direction is the other one.
        var resolved = instrument.Container.TryResolve("samples\\TONE.WAV", out var key);
        using var stream = resolved ? instrument.Container.OpenFile(key) : null;

        //Assert
        resolved.Should().BeTrue();
        instrument.Container.CacheKeyFor(key).Should().NotBeNullOrWhiteSpace();
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void an_instruments_container_is_closed_with_the_instrument()
    {
        //Arrange
        using var fixtures = Fixtures();
        var instrument = DecentSamplerInstrument.Load(Container(fixtures, "dslibrary"));
        var container = instrument.Container;

        //Act
        instrument.Dispose();
        var afterwards = () => container.OpenFile("Lib/Samples/tone.wav");

        //Assert - the instrument owns it, so a consumer must not keep it past the instrument.
        afterwards.Should().Throw<ObjectDisposedException>();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static DecentSamplerSynthesizerSettings Offline(int sampleRate = 44100) =>
        new DecentSamplerSynthesizerSettings(sampleRate)
        {
            StreamingMode = DecentSamplerStreamingMode.Offline,
        };

    private static DecentSamplerEngineFixtures Fixtures()
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100);
        return fixtures;
    }

    // Writes the same one-zone library in the requested container form and returns what a consumer
    // would hand to Load: a preset file, the folder holding it, or an archive.
    private static string Container(DecentSamplerEngineFixtures fixtures, string form)
    {
        var presetPath = fixtures.WritePreset(Preset);

        switch (form)
        {
            case "preset":
                return presetPath;

            case "folder":
                return fixtures.Directory;

            default:
                var archivePath = Path.Combine(
                    Path.GetTempPath(), "codebrix-dsc-" + Path.GetRandomFileName() + "." + form);

                using (var stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    Pack(archive, "Lib/instrument.dspreset", presetPath);
                    Pack(archive, "Lib/Samples/tone.wav", fixtures.PathFor("Samples/tone.wav"));
                }

                return archivePath;
        }
    }

    private static void Pack(ZipArchive archive, string entryName, string sourcePath)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

        using var source = File.OpenRead(sourcePath);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static MidiSequence BuildSequence()
    {
        const int ticksPerQuarter = 480;
        var events = new MidiEventCollection(1, ticksPerQuarter);

        events.AddEvent(new TempoEvent(500000, 0), 1);
        events.AddEvent(new NoteOnEvent(0, 1, 69, 100, ticksPerQuarter), 1);
        events.AddEvent(new NoteEvent(ticksPerQuarter, 1, MidiCommandCode.NoteOff, 69, 0), 1);
        events.PrepareForExport();

        return MidiSequence.FromEvents(events);
    }
}

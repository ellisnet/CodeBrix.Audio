using System;
using System.Linq;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// A Decent Sampler preset used as the instrument behind a stems song's MIDI part. This is the whole
/// of the join between the two halves of the library: <see cref="SunoPlayerOptions.InstrumentFactory"/>
/// returns any <c>IMidiSynthesizer</c>, and a <see cref="DecentSamplerSynthesizer"/> is one.
/// </summary>
/// <remarks>
/// The render is offline, so the synthesizer is built in
/// <see cref="DecentSamplerStreamingMode.Offline"/>: the factory is the only place a consumer can say
/// so, because the player never sees the settings.
/// </remarks>
[Collection("SunoStems")]
public class SunoDecentSamplerInstrumentTests
{
    private const int RenderRate = 22050;

    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" release="0.05">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_decent_sampler_preset_plays_a_stems_song_midi_part()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);

        var song = SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path), new SunoLoadOptions());
        var asked = 0;

        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentFactory = (stem, rate) =>
            {
                asked++;
                return new DecentSamplerSynthesizer(
                    instrument,
                    new DecentSamplerSynthesizerSettings(rate)
                    {
                        StreamingMode = DecentSamplerStreamingMode.Offline,
                    });
            },
        });

        foreach (var track in player.Tracks.Where(track => track.HasMidiSource))
        {
            track.ActiveSource = TrackSource.Midi;
        }

        //Act
        var mix = player.Render(RenderRate, TimeSpan.Zero);

        //Assert - the factory was asked once per MIDI part, at the render's own rate, and the preset
        // is audible in the mix.
        asked.Should().Be(player.Tracks.Count(track => track.HasMidiSource));
        Energy(mix).Should().BeGreaterThan(0.0);
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void one_instrument_serves_every_part_and_the_render_repeats()
    {
        //Arrange - a factory may be called several times and from a worker thread, so the INSTRUMENT is
        // shared and a new synthesizer is returned every time.
        using var temporary = new TemporaryFolder();
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);

        var song = SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path), new SunoLoadOptions());

        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentFactory = (stem, rate) => new DecentSamplerSynthesizer(
                instrument,
                new DecentSamplerSynthesizerSettings(rate)
                {
                    StreamingMode = DecentSamplerStreamingMode.Offline,
                }),
        });

        foreach (var track in player.Tracks.Where(track => track.HasMidiSource))
        {
            track.ActiveSource = TrackSource.Midi;
        }

        //Act
        var first = player.Render(RenderRate, TimeSpan.Zero);
        var second = player.Render(RenderRate, TimeSpan.Zero);

        //Assert
        first.Should().Equal(second);
    }

    private static DecentSamplerEngineFixtures Fixtures()
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100);
        return fixtures;
    }

    private static double Energy(float[] samples)
    {
        var total = 0.0;
        foreach (var sample in samples) { total += Math.Abs(sample); }
        return total;
    }
}

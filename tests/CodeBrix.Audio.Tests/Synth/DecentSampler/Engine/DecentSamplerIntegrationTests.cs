using System;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// How the rest of the library reaches the Decent Sampler engine: the offline renderer's overloads, the
/// music player's shared instrument cache, and its container dispatch.
/// </summary>
/// <remarks>
/// The music player's <c>Load</c> opens the shared audio device, so only its argument checking and its
/// cache are exercised here. The device-driven dispatch has an opt-in test at the bottom, on the same
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS switch the rest of the suite uses.
/// </remarks>
[Collection("SharedAudioOutput")]
public class DecentSamplerIntegrationTests
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

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
    public void the_renderer_plays_a_sequence_through_a_decent_sampler_instrument()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        var sequence = BuildSequence();

        //Act
        var samples = SoundFontRenderer.Render(instrument, sequence, 44100, TimeSpan.FromSeconds(0.5));

        //Assert
        samples.Length.Should().Be((int)Math.Ceiling((sequence.Length.TotalSeconds + 0.5) * 44100) * 2);
        Peak(samples).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void the_renderer_rejects_bad_arguments()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        var sequence = BuildSequence();

        //Act
        var nullInstrument = () => SoundFontRenderer.Render((DecentSamplerInstrument)null, sequence);
        var nullSequence = () => SoundFontRenderer.Render(instrument, null);
        var badRate = () => SoundFontRenderer.Render(instrument, sequence, 0);
        var badTail = () => SoundFontRenderer.Render(instrument, sequence, 44100, TimeSpan.FromSeconds(-1));

        //Assert
        nullInstrument.Should().Throw<ArgumentNullException>();
        nullSequence.Should().Throw<ArgumentNullException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badTail.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_renderer_writes_a_wav_file()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        var output = fixtures.PathFor("render.wav");

        //Act
        SoundFontRenderer.RenderToWavFile(instrument, BuildSequence(), output, 44100, TimeSpan.FromSeconds(0.2));

        //Assert
        File.Exists(output).Should().BeTrue();
        new FileInfo(output).Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void the_renderer_writes_a_wav_stream()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        using var stream = new MemoryStream();

        //Act
        SoundFontRenderer.RenderToWavStream(
            instrument, BuildSequence(), stream, 44100, TimeSpan.FromSeconds(0.2), leaveOpen: true);

        //Assert
        stream.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void the_renderer_takes_the_tempo_from_the_sequence()
    {
        //Arrange - a zone delayed by one beat: at 60 BPM that is a whole second, at 240 a quarter of one.
        const string delayed = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" delay="1" delayUnit="beats">
                  <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(delayed);

        //Act
        var slow = SoundFontRenderer.Render(instrument, BuildSequence(60), 44100, TimeSpan.FromSeconds(1.0));
        var fast = SoundFontRenderer.Render(instrument, BuildSequence(240), 44100, TimeSpan.FromSeconds(1.0));

        //Assert - the fast render has sound in its first half second; the slow one does not.
        Peak(fast, 0, 44100).Should().BeGreaterThan(0.05);
        Peak(slow, 0, 30000).Should().BeLessThan(0.001);
    }

    [Fact]
    public void the_player_rejects_null_arguments()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(Preset);
        using var player = new MidiMusicPlayer();

        //Act
        var nullInstrument = () => player.Load((DecentSamplerInstrument)null, BuildSequence());
        var nullSequence = () => player.Load(instrument, null);

        //Assert
        nullInstrument.Should().Throw<ArgumentNullException>();
        nullSequence.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_players_shared_cache_loads_a_preset_once()
    {
        //Arrange
        using var fixtures = Fixtures();
        var path = fixtures.WritePreset(Preset);

        //Act
        var first = MidiMusicPlayer.SharedDecentSamplerCache.Get(path);
        var second = MidiMusicPlayer.SharedDecentSamplerCache.Get(path);

        //Assert
        second.Should().BeSameAs(first);
        first.Zones.Count.Should().Be(1);
    }

    [Fact]
    public void the_players_shared_cache_opens_a_library_folder()
    {
        //Arrange
        using var fixtures = Fixtures();
        fixtures.WritePreset(Preset, "nested/instrument.dspreset");
        fixtures.WriteConstantWav("nested/Samples/tone.wav", value: 0.5f);

        //Act
        var instrument = MidiMusicPlayer.SharedDecentSamplerCache.Get(Path.Combine(fixtures.Directory, "nested"));

        //Assert
        instrument.Zones.Count.Should().Be(1);
    }

    [Fact]
    public void plays_a_decent_sampler_preset_through_the_music_player()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var fixtures = Fixtures();
        var presetPath = fixtures.WritePreset(Preset);
        var midiPath = fixtures.PathFor("song.mid");
        WriteSequence(midiPath);

        using var player = new MidiMusicPlayer();

        //Act
        player.Load(presetPath, midiPath);

        //Assert - the extension dispatched to the Decent Sampler engine.
        player.IsLoaded.Should().BeTrue();
        player.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    private static DecentSamplerEngineFixtures Fixtures()
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100 * 2);
        return fixtures;
    }

    private static MidiSequence BuildSequence(int beatsPerMinute = 120)
    {
        var events = BuildEvents(beatsPerMinute);
        return MidiSequence.FromEvents(events);
    }

    private static void WriteSequence(string path) =>
        MidiFile.Export(path, BuildEvents(120));

    private static MidiEventCollection BuildEvents(int beatsPerMinute)
    {
        const int ticksPerQuarter = 480;
        var events = new MidiEventCollection(1, ticksPerQuarter);

        events.AddEvent(new TempoEvent(60000000 / beatsPerMinute, 0), 1);

        var tick = 0L;
        foreach (var note in new[] { 60, 64, 67 })
        {
            events.AddEvent(new NoteOnEvent(tick, 1, note, 100, ticksPerQuarter), 1);
            events.AddEvent(new NoteEvent(tick + ticksPerQuarter, 1, MidiCommandCode.NoteOff, note, 0), 1);
            tick += ticksPerQuarter;
        }

        events.PrepareForExport();
        return events;
    }

    private static double Peak(float[] interleaved) => Peak(interleaved, 0, interleaved.Length);

    private static double Peak(float[] interleaved, int offset, int count)
    {
        var peak = 0.0;
        var end = Math.Min(interleaved.Length, offset + count);

        for (var i = offset; i < end; i++)
        {
            peak = Math.Max(peak, Math.Abs(interleaved[i]));
        }

        return peak;
    }
}

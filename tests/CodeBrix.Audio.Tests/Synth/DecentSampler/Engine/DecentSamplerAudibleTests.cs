using System;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The tests in this project that MAKE A SOUND through a Decent Sampler preset: one in-memory and one
/// STREAMED FROM DISK. Opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1; skipped otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Both play the same five-note motif every audible test in this suite plays - one recognisable
/// phrase means a good run is obvious by ear and a broken one sounds broken - through a synthetic
/// preset built from a sine sample, so what is audible is the whole Decent Sampler chain: the
/// preset, the zone runtime, the envelope, the mixer and the device.
/// </para>
/// <para>
/// The streamed one is the only test anywhere that exercises the RealTime streaming path against a
/// real device clock. Every other streaming test renders Offline, where the reader runs on the
/// calling thread and starvation cannot happen; only a device callback can show a ring buffer that
/// did not refill in time. It uses a small preload head so that nearly every frame comes off the
/// reader thread, and it asserts that Problems stays empty, which is where a starved block reports
/// itself - so a reader that cannot keep up fails the test as well as being audible as a gap.
/// </para>
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class DecentSamplerAudibleTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    private const string StreamedPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0.4" attack="0.01" decay="0" sustain="1" release="0.08"
                   playbackMode="disk_streaming">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127"
                      loopStart="4410" loopEnd="8819" loopEnabled="true" />
            </group>
          </groups>
          <effects>
            <effect type="reverb" roomSize="0.4" damping="0.4" wetLevel="0.15" />
          </effects>
        </DecentSampler>
        """;

    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0.4" attack="0.01" decay="0" sustain="1" release="0.08">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127"
                      loopStart="4410" loopEnd="8819" loopEnabled="true" />
            </group>
          </groups>
          <effects>
            <effect type="reverb" roomSize="0.4" damping="0.4" wetLevel="0.15" />
          </effects>
        </DecentSampler>
        """;

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public DecentSamplerAudibleTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void plays_the_close_encounters_motif_through_a_decent_sampler_preset()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100);
        using var instrument = fixtures.LoadPreset(Preset);

        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Load(instrument, BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        var fired = ended.Wait(player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert - hearing five notes is the point; the rest proves the transport ran to the
        // sequence's natural end and then stopped.
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void plays_the_close_encounters_motif_from_a_streamed_preset()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - a 4,096-frame head against a 44,100-frame file, so the head covers the first
        // 93 ms and the reader thread supplies everything after it, including the whole loop.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100);

        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(StreamedPreset, "streamed.dspreset"),
            new DecentSamplerLoadOptions { StreamingPreloadFrames = 4096 });

        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Load(instrument, BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        var fired = ended.Wait(player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert - the same five notes, off the disk this time, and no starvation reported.
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        instrument.Problems.Should().BeEmpty();
    }

    // The five-note motif, at 1,000 ticks per quarter note so that at the default 120 BPM one tick is
    // exactly half a millisecond and the 0.30 s notes and 0.06 s gaps come out exact.
    private static MidiSequence BuildMotifSequence()
    {
        const int ticksPerQuarter = 1000;
        const int noteTicks = 600;
        const int gapTicks = 120;
        const int velocity = 100;
        const int channel = 1;

        int[] notes = [79, 81, 77, 65, 72];   // G5, A5, F5, F4, C5

        var events = new MidiEventCollection(1, ticksPerQuarter);

        var tick = 0L;
        foreach (var note in notes)
        {
            events.AddEvent(new NoteOnEvent(tick, channel, note, velocity, noteTicks), 1);
            events.AddEvent(new NoteEvent(tick + noteTicks, channel, MidiCommandCode.NoteOff, note, 0), 1);
            tick += noteTicks + gapTicks;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    [Fact]
    public void the_motif_sequence_has_the_expected_duration()
    {
        //Arrange & Act
        // No device needed: this checks the sequence the audible test plays is timed as intended, so a
        // failure there is not first blamed on the audio path.
        var sequence = BuildMotifSequence();

        //Assert - five steps of 0.36 s, minus the trailing gap of the last one.
        sequence.Length.TotalSeconds.Should().BeApproximately(1.74, 0.05);
    }
}

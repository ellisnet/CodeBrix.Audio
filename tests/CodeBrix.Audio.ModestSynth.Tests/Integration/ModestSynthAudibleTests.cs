using System;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The one test in this project that MAKES A SOUND. Opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1;
/// skipped otherwise.
/// </summary>
/// <remarks>
/// It plays the five-note motif every audible test in this family plays - one recognisable phrase
/// means a good run is obvious by ear and a broken one sounds broken - through a Decent Sampler preset
/// whose group holds an <c>oscillator</c> rather than a <c>sample</c>. What is audible is therefore
/// the whole add-on chain: <c>ModestSynth.Register()</c>, the waveform, the adapter, the core's voice
/// runtime and the device.
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class ModestSynthAudibleTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    // A four-operator FM tone with a little phaser on it: unmistakably a SYNTH rather than a
    // recording, so the ear can tell this test from the sampled one that plays the same notes.
    private const string OscillatorPreset =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<DecentSampler minVersion=\"1.11.0\">\n" +
        "  <groups>\n" +
        "    <group attack=\"0.005\" decay=\"0.25\" sustain=\"0.6\" release=\"0.12\" ampVelTrack=\"0.4\">\n" +
        "      <oscillator waveform=\"fm6op\" fmAlgorithm=\"1\" fmOp2Ratio=\"2\" fmOp2Level=\"0.35\" />\n" +
        "    </group>\n" +
        "  </groups>\n" +
        "  <effects>\n" +
        "    <effect type=\"phaser\" mix=\"0.35\" modRate=\"0.4\" modDepth=\"0.6\" " +
        "centerFrequency=\"700\" feedback=\"0.5\" />\n" +
        "  </effects>\n" +
        "</DecentSampler>\n";

    /// <summary>Registers the add-on and resets the shared output before each test, for isolation.</summary>
    public ModestSynthAudibleTests()
    {
        ModestSynth.Register();
        SharedAudioOutput.Shutdown();
    }

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void plays_the_close_encounters_motif_through_an_oscillator_preset()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(OscillatorPreset);

        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Load(instrument, BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        bool fired = ended.Wait(
            player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert - hearing five synthesized notes is the point; the rest proves the transport ran to
        // the sequence's natural end and then stopped.
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void the_motif_sequence_has_the_expected_duration()
    {
        //Arrange & Act
        // No device needed: this checks the sequence the audible test plays is timed as intended, so a
        // failure there is not first blamed on the audio path.
        MidiSequence sequence = BuildMotifSequence();

        //Assert - five steps of 0.36 s, minus the trailing gap of the last one.
        sequence.Length.TotalSeconds.Should().BeApproximately(1.74, 0.05);
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

        long tick = 0L;

        foreach (int note in notes)
        {
            events.AddEvent(new NoteOnEvent(tick, channel, note, velocity, noteTicks), 1);
            events.AddEvent(new NoteEvent(tick + noteTicks, channel, MidiCommandCode.NoteOff, note, 0), 1);
            tick += noteTicks + gapTicks;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }
}

using System;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiMusicPlayer"/>. The tests that open the audio device and make a sound are
/// opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1; the rest exercise the pre-load surface with no
/// hardware. Shares the non-parallel "SharedAudioOutput" collection with the other player tests.
/// </summary>
[Collection("SharedAudioOutput")]
public sealed class MidiMusicPlayerTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public MidiMusicPlayerTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- pre-load surface (no device) -----

    [Fact]
    public void a_new_player_is_unloaded_and_stopped()
    {
        //Arrange & Act
        using var player = new MidiMusicPlayer();

        //Assert
        player.IsLoaded.Should().BeFalse();
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.Position.Should().Be(TimeSpan.Zero);
        player.Duration.Should().Be(TimeSpan.Zero);
        player.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void volume_and_looping_persist_before_a_load()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        player.Volume = 0.35f;
        player.IsLooping = true;

        //Assert
        player.Volume.Should().Be(0.35f);
        player.IsLooping.Should().BeTrue();
    }

    [Fact]
    public void play_throws_when_nothing_is_loaded()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var act = () => player.Play();

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void pause_and_stop_are_harmless_when_nothing_is_loaded()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var act = () => { player.Pause(); player.Stop(); };

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void load_rejects_null_arguments()
    {
        //Arrange
        using var player = new MidiMusicPlayer();
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);

        //Act
        var nullSoundFont = () => player.Load(null, BuildMotifSequence());
        var nullSequence = () => player.Load(soundFont, null);

        //Assert
        nullSoundFont.Should().Throw<ArgumentNullException>();
        nullSequence.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void dispose_is_idempotent()
    {
        //Arrange
        var player = new MidiMusicPlayer();

        //Act
        var act = () => { player.Dispose(); player.Dispose(); };

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void the_motif_sequence_has_the_expected_duration()
    {
        //Arrange & Act
        // No device needed: this checks the sequence the audible test plays is timed as intended,
        // so a failure there is not first blamed on the audio path.
        var sequence = BuildMotifSequence();

        //Assert
        // Five steps of 0.36s, minus the trailing gap of the last one.
        sequence.Length.TotalSeconds.Should().BeApproximately(1.74, 0.05);
    }

    // ----- audible (device) -----

    [Fact]
    public void plays_the_close_encounters_motif_through_a_soundfont()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();

        var deadline = DateTime.UtcNow + player.Duration + TimeSpan.FromSeconds(2);
        while (player.Position < player.Duration && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(25);
        }

        //Assert
        // Getting here having heard five notes is the point; the state check just proves the
        // transport ran rather than the sequence never starting.
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Playing);
    }

    [Fact]
    public void pause_and_resume_hold_the_position()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        Thread.Sleep(500);
        player.Pause();
        var paused = player.Position;
        Thread.Sleep(300);
        var stillPaused = player.Position;
        player.Play();

        //Assert
        paused.Should().BeGreaterThan(TimeSpan.Zero);
        stillPaused.Should().Be(paused);
        player.PlaybackState.Should().Be(PlaybackState.Playing);
    }

    [Fact]
    public void seek_moves_the_position()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        Thread.Sleep(200);
        player.Seek(TimeSpan.FromSeconds(1));
        var afterSeek = player.Position;

        //Assert
        afterSeek.TotalSeconds.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void stop_rewinds_to_the_start()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        Thread.Sleep(500);
        player.Stop();

        //Assert
        player.Position.Should().Be(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    /// <summary>
    /// The five-tone "Close Encounters" motif as a MIDI sequence: G5, A5, F5, F4, C5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same tune as <c>TestAudio.BuildCloseEncountersSamples</c>, and for the same reason: one
    /// recognisable phrase means a good run is obvious by ear and a broken one sounds broken. Here
    /// it is played as MIDI notes through the synthetic SoundFont rather than synthesized directly,
    /// so what it proves is the whole chain - sequence, synthesizer, mixer, device.
    /// </para>
    /// <para>
    /// 1000 ticks per quarter note at the default 120 BPM makes one tick exactly half a
    /// millisecond, so the 0.30s notes and 0.06s gaps of the original are expressed exactly.
    /// </para>
    /// <para>
    /// Every note is 65 or above, which keeps them all in the fixture's upper key range and so on
    /// one sample - the tune arrives in a consistent timbre rather than changing halfway through.
    /// </para>
    /// </remarks>
    private static MidiSequence BuildMotifSequence()
    {
        const int ticksPerQuarter = 1000;   // at 120 BPM: 1 tick = 0.5 ms
        const int noteTicks = 600;          // 0.30 s
        const int gapTicks = 120;           // 0.06 s
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
}

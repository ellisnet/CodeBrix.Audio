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
    public void auxiliary_outputs_are_folded_into_the_mix_unless_they_are_dropped()
    {
        //Arrange
        using var player = new MidiMusicPlayer();
        var byDefault = player.DropAuxiliaryOutputs;

        //Act
        player.DropAuxiliaryOutputs = true;

        //Assert
        byDefault.Should().BeFalse();
        player.DropAuxiliaryOutputs.Should().BeTrue();
    }

    [Fact]
    public void speed_defaults_to_one_and_persists_before_a_load()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var initial = player.Speed;
        player.Speed = 0.5f;

        //Assert
        initial.Should().Be(1.0f);
        player.Speed.Should().Be(0.5f);
    }

    [Fact]
    public void speed_rejects_a_negative_value()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var act = () => player.Speed = -0.1f;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void both_message_hooks_persist_before_a_load()
    {
        //Arrange
        using var player = new MidiMusicPlayer();
        MidiMessageObserver observer = (_, _, _, _) => { };
        MidiSequencer.MessageHook filter = (_, _, _, _, _) => { };

        //Act
        player.MidiMessageProcessed = observer;
        player.MidiMessageFilter = filter;

        //Assert
        player.MidiMessageProcessed.Should().BeSameAs(observer);
        player.MidiMessageFilter.Should().BeSameAs(filter);
    }

    [Fact]
    public void the_tempo_source_reports_the_midi_defaults_before_a_load()
    {
        //Arrange & Act
        using var player = new MidiMusicPlayer();

        //Assert
        player.TempoSource.Should().NotBeNull();
        player.TempoSource.BeatsPerMinute.Should().Be(120.0);
        player.TempoSource.BeatPosition.Should().Be(0.0);
        player.TempoSource.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void the_sequence_is_null_before_a_load()
    {
        //Arrange & Act
        using var player = new MidiMusicPlayer();

        //Assert
        player.Sequence.Should().BeNull();
    }

    [Fact]
    public void sending_midi_is_harmless_when_nothing_is_loaded()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var act = () =>
        {
            player.SendMidiMessage(0, 0xB0, 7, 100);
            player.SetChannelVolume(0, 0.5f);
            player.SetChannelPan(0, -1f);
            player.SetChannelProgram(0, 42);
        };

        //Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void the_per_channel_calls_reject_an_out_of_range_channel(int channel)
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var send = () => player.SendMidiMessage(channel, 0xB0, 7, 100);
        var volume = () => player.SetChannelVolume(channel, 1f);
        var pan = () => player.SetChannelPan(channel, 0f);
        var program = () => player.SetChannelProgram(channel, 0);

        //Assert
        send.Should().Throw<ArgumentOutOfRangeException>();
        volume.Should().Throw<ArgumentOutOfRangeException>();
        pan.Should().Throw<ArgumentOutOfRangeException>();
        program.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void set_channel_program_rejects_an_out_of_range_program(int program)
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        var act = () => player.SetChannelProgram(0, program);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
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
        var nullSoundFont = () => player.Load((SoundFont)null, BuildMotifSequence());
        var nullSequence = () => player.Load(soundFont, (MidiSequence)null);

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
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        // The deadline allows for the sequence, the ring-out of the final note, and the
        // stuck-voice tail cap that bounds it.
        var fired = ended.Wait(player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert
        // Getting here having heard five notes is the point; the end checks prove the transport
        // ran to the sequence's natural end and then actually stopped.
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void plays_the_close_encounters_motif_through_an_sfz_instrument()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - the same five notes, rendered by the OTHER synthesizer: a synthetic SFZ
        // instrument built from a sine sample, so the whole SFZ chain is what is audible.
        using var fixture = Sfz.SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 440, frames: 44100);
        var instrument = fixture.Load("""
            <region> sample=tone.wav pitch_keycenter=69 loop_mode=loop_continuous ampeg_attack=0.01 ampeg_release=0.08
            """);

        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        player.Load(instrument, BuildMotifSequence());
        player.Volume = 0.7f;

        //Act
        player.Play();
        var fired = ended.Wait(player.Duration + TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);

        //Assert
        fired.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
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

    [Fact]
    public void the_message_observer_reports_the_notes_as_the_motif_plays()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        // The end-to-end proof of the observer hook: the same five audible notes, counted as the
        // synthesizer is told to play them. This is the "note drives something outside the audio"
        // path a game builds screen shakes and particle spawns on.
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();

        var notes = new System.Collections.Concurrent.ConcurrentQueue<int>();
        player.MidiMessageProcessed = (_, command, data1, data2) =>
        {
            if (command == 0x90 && data2 > 0)
            {
                notes.Enqueue(data1);
            }
        };

        var sequence = BuildMotifSequence();
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), sequence);
        player.Volume = 0.7f;

        //Act
        player.Play();

        var deadline = DateTime.UtcNow + player.Duration + TimeSpan.FromSeconds(2);
        while (notes.Count < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(25);
        }

        //Assert
        notes.Should().Equal(79, 81, 77, 65, 72);
        player.Sequence.Should().BeSameAs(sequence);
    }

    // ----- a stream that is still being written (device) -----

    [Fact]
    public void load_accepts_a_stream_and_reports_it_instead_of_a_sequence()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendNextBar();

        //Act
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);

        //Assert - one or the other is loaded, never both.
        player.IsLoaded.Should().BeTrue();
        player.Stream.Should().BeSameAs(stream);
        player.Sequence.Should().BeNull();
        player.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void a_stream_loaded_empty_plays_starved_then_plays_what_arrives()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - nothing written yet: the player is playing silence, not stopped.
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);
        player.Volume = 0.7f;

        //Act
        player.Play();
        Thread.Sleep(300);
        var starvedAtFirst = player.IsStarved;
        var stateWhileStarved = player.PlaybackState;
        var positionWhileStarved = player.Position;

        producer.AppendAllBars();
        Thread.Sleep(700);

        //Assert
        starvedAtFirst.Should().BeTrue();
        stateWhileStarved.Should().Be(PlaybackState.Playing);
        positionWhileStarved.Should().Be(TimeSpan.Zero);

        player.IsStarved.Should().BeFalse();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Playing);
    }

    [Fact]
    public void duration_grows_as_the_producer_appends()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendNextBar();

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);

        //Act
        var afterOneBar = player.Duration;
        producer.AppendNextBar();
        var afterTwoBars = player.Duration;

        //Assert - a progress bar built on this has a moving end until the producer completes.
        afterOneBar.Should().BeGreaterThan(TimeSpan.Zero);
        afterTwoBars.Should().BeGreaterThan(afterOneBar);
    }

    [Fact]
    public void stop_rewinds_a_stream_to_the_start()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - everything written, so there is a piece to hear twice.
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendAllBars();

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);
        player.Volume = 0.7f;

        //Act
        player.Play();
        Thread.Sleep(600);
        var played = player.Position;
        player.Stop();

        //Assert - the stream keeps everything it was given, so it plays again from the top.
        played.Should().BeGreaterThan(TimeSpan.Zero);
        player.Position.Should().Be(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.Stream.Should().BeSameAs(stream);

        player.Play();
        Thread.Sleep(400);
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void playback_ended_is_raised_after_complete()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - a fifth of a second of music, and a producer that has stopped writing without
        // saying so.
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        var stream = new MidiStream(1000);
        stream.AppendNote(0, 1, 72, 100, 400);

        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);
        player.Volume = 0.7f;

        //Act
        player.Play();
        var endedWhileWriting = ended.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        stream.Complete();
        var endedAfterComplete = ended.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        //Assert - Complete() is what ends a stream; without it the player waits, playing, for ever.
        endedWhileWriting.Should().BeFalse();
        endedAfterComplete.Should().BeTrue();
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void load_rejects_a_stream_already_playing_elsewhere()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - one player per stream at a time.
        using var scope = new AudibleTestScope();
        using var first = new MidiMusicPlayer();
        using var second = new MidiMusicPlayer();
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);

        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        new StreamingTestProducer(stream).AppendNextBar();

        first.Load(soundFont, stream);

        //Act
        var act = () => second.Load(soundFont, stream);

        //Assert
        act.Should().Throw<InvalidOperationException>();
        first.Stream.Should().BeSameAs(stream);
        second.Stream.Should().BeNull();

        // ... and letting go hands it on.
        first.Dispose();
        var again = () => second.Load(soundFont, stream);
        again.Should().NotThrow();
        second.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void is_looping_is_ignored_for_a_stream()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange - a growing timeline has no end to loop at, so a completed stream must still end.
        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();

        var stream = new MidiStream(1000);
        stream.AppendNote(0, 1, 72, 100, 400);
        stream.Complete();

        player.IsLooping = true;
        player.Load(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), stream);
        player.Volume = 0.7f;

        //Act
        var setAfterLoad = () => player.IsLooping = true;
        player.Play();
        var fired = ended.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        //Assert - neither refused nor thrown: the property is the PLAYER's and keeps its value for
        // the next sequence loaded, and the stream played once and ended.
        setAfterLoad.Should().NotThrow();
        player.IsLooping.Should().BeTrue();
        fired.Should().BeTrue();
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

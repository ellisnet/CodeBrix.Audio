using System;
using System.IO;
using CodeBrix.Audio.Playback;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Covers <see cref="PlayerTrack"/> and its two concrete forms: the per-track properties, their
/// clamping, and what a track will and will not let you switch to. No audio device is involved.
/// </summary>
public class PlayerTrackTests
{
    [Fact]
    public void a_new_audio_track_takes_its_name_from_the_file_and_plays_its_audio()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var path = song.WriteConstantWav("bass.wav", 0.5f, 0.25f, frames: 4410);

        //Act
        var track = new AudioTrack(path);

        //Assert
        track.Name.Should().Be("bass");
        track.HasAudioSource.Should().BeTrue();
        track.HasMidiSource.Should().BeFalse();
        track.ActiveSource.Should().Be(TrackSource.Audio);
        track.Gain.Should().Be(1.0f);
        track.MidiSourceGain.Should().Be(1.0f);
        track.Pan.Should().Be(0.0f);
        track.Offset.Should().Be(TimeSpan.Zero);
        track.Mute.Should().BeFalse();
        track.Solo.Should().BeFalse();
    }

    [Fact]
    public void an_audio_track_reports_the_length_of_its_file()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var path = song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 22050);

        //Act
        var track = new AudioTrack(path);

        //Assert
        track.AudioDuration.TotalSeconds.Should().BeApproximately(0.5, 1e-6);
        track.Duration.TotalSeconds.Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void a_new_midi_track_plays_its_midi_and_holds_short_notes_for_sixty_milliseconds()
    {
        //Arrange
        var sequence = MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 4, stepTicks: 1000);

        //Act
        var track = new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory(), "drums");

        //Assert
        track.Name.Should().Be("drums");
        track.HasMidiSource.Should().BeTrue();
        track.HasAudioSource.Should().BeFalse();
        track.ActiveSource.Should().Be(TrackSource.Midi);
        track.MinimumNoteHold.Should().Be(TimeSpan.FromMilliseconds(60));
        track.IgnoreNoteOff.Should().BeFalse();
    }

    [Fact]
    public void a_track_can_hold_both_sources_and_switch_between_them()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var path = song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 4410);
        var track = new AudioTrack(path);

        //Act
        track.SetMidiSource(
            MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 500, stepTicks: 1000),
            MultiTrackTestSong.SoundFontFactory());
        track.ActiveSource = TrackSource.Midi;

        //Assert
        track.HasAudioSource.Should().BeTrue();
        track.HasMidiSource.Should().BeTrue();
        track.ActiveSource.Should().Be(TrackSource.Midi);
    }

    [Fact]
    public void switching_to_a_source_the_track_does_not_have_throws()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var track = new AudioTrack(song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 4410));

        //Act
        var act = () => track.ActiveSource = TrackSource.Midi;

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void a_track_reports_the_longer_of_its_two_sources_as_its_duration()
    {
        //Arrange - half a second of audio, two seconds of MIDI.
        using var song = MultiTrackTestSong.Create();
        var track = new AudioTrack(song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 22050));

        //Act
        track.SetMidiSource(
            MultiTrackTestSong.BuildSingleNoteSequence(60, noteTicks: 500, totalTicks: 4000),
            MultiTrackTestSong.SoundFontFactory());

        //Assert
        track.MidiDuration.TotalSeconds.Should().BeApproximately(2.0, 1e-3);
        track.Duration.TotalSeconds.Should().BeApproximately(2.0, 1e-3);
    }

    [Theory]
    [InlineData(-2.0f, -1.0f)]
    [InlineData(2.0f, 1.0f)]
    [InlineData(-0.25f, -0.25f)]
    public void pan_clamps_to_the_stereo_field(float value, float expected)
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var track = new AudioTrack(song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 100));

        //Act
        track.Pan = value;

        //Assert
        track.Pan.Should().Be(expected);
    }

    [Fact]
    public void gains_clamp_at_zero_rather_than_going_negative()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var track = new AudioTrack(song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 100));

        //Act
        track.Gain = -1.0f;
        track.MidiSourceGain = -3.0f;

        //Assert
        track.Gain.Should().Be(0.0f);
        track.MidiSourceGain.Should().Be(0.0f);
    }

    [Fact]
    public void minimum_note_hold_clamps_a_negative_value_to_zero()
    {
        //Arrange
        var track = new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 100, stepTicks: 200),
            MultiTrackTestSong.SoundFontFactory());

        //Act
        track.MinimumNoteHold = TimeSpan.FromMilliseconds(-10);

        //Assert
        track.MinimumNoteHold.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(128)]
    public void gm_program_rejects_a_value_outside_the_general_midi_range(int program)
    {
        //Arrange
        var track = new MidiTrack(
            MultiTrackTestSong.BuildNoteSequence([60], noteTicks: 100, stepTicks: 200),
            MultiTrackTestSong.SoundFontFactory());

        //Act
        var act = () => track.GmProgram = program;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_audio_track_reads_the_format_from_the_content_not_the_extension()
    {
        //Arrange - a WAV under a name that says nothing about what it holds.
        using var song = MultiTrackTestSong.Create();
        var path = song.WriteConstantWav("stem.stem", 0.5f, 0.5f, frames: 4410);

        //Act
        var track = new AudioTrack(path);

        //Assert
        track.AudioDuration.TotalSeconds.Should().BeApproximately(0.1, 1e-6);
    }

    [Fact]
    public void an_audio_track_rejects_content_that_is_not_audio()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var path = Path.Combine(song.Directory, "notes.txt");
        File.WriteAllText(path, "this is not a sound");

        //Act
        var act = () => new AudioTrack(path);

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(TestAssets.VorbisToneStereo)]
    [InlineData(TestAssets.FlacToneStereo)]
    public void an_audio_track_opens_the_compressed_formats_too(string fixture)
    {
        //Arrange & Act
        var track = new AudioTrack(TestAssets.Path(fixture));

        //Assert
        track.AudioDuration.Should().BeGreaterThan(TimeSpan.Zero);
        track.HasAudioSource.Should().BeTrue();
    }

    [Fact]
    public void an_audio_track_opens_an_mp3_stream_by_its_frame_sync()
    {
        //Arrange - a hand-built silent MPEG-1 Layer III stream, no ID3 tag in front of it.
        var bytes = TestAudio.BuildSilentMp3(frameCount: 40);

        //Act
        var track = new AudioTrack(new MemoryStream(bytes), leaveOpen: false, name: "silence");

        //Assert
        track.AudioDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void an_audio_track_can_be_built_from_a_stream()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var bytes = File.ReadAllBytes(song.WriteConstantWav("stem.wav", 0.5f, 0.5f, frames: 4410));

        //Act
        var track = new AudioTrack(new MemoryStream(bytes), leaveOpen: false, name: "from-stream");

        //Assert
        track.Name.Should().Be("from-stream");
        track.AudioDuration.TotalSeconds.Should().BeApproximately(0.1, 1e-6);
    }
}

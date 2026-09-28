using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using Xunit;

namespace CodeBrix.Audio.MidiConnect.Tests;

public class MidiRecordingTests
{
    internal static MidiTimedMessage Message(double seconds, params byte[] bytes) => new(TimeSpan.FromSeconds(seconds), new MidiPacket(bytes));

    [Fact]
    public void Notes_pair_fifo_and_measure_key_release_independent_of_sustain()
    {
        //Arrange, Act
        var recording = new MidiRecording(new[] {
            Message(0, 0x90, 60, 100), Message(.1, 0xB0, 64, 127), Message(.2, 0x90, 60, 90),
            Message(.3, 0x80, 60, 45), Message(.4, 0x90, 60, 0), Message(.5, 0x91, 60, 80)
        }, TimeSpan.FromSeconds(1), null);
        //Assert
        Assert.Equal(3, recording.Notes.Count);
        Assert.Equal(TimeSpan.FromSeconds(.3), recording.Notes[0].Duration);
        Assert.Equal(45, recording.Notes[0].ReleaseVelocity);
        Assert.Equal(TimeSpan.FromSeconds(.2), recording.Notes[1].Duration);
        Assert.True(recording.Notes[2].IsTruncated);
        Assert.Equal(2, recording.Notes[2].Channel);
        Assert.Equal(TimeSpan.FromSeconds(.5), recording.Notes[2].Duration);
    }

    [Fact]
    public void File_round_trip_retains_sysex_common_realtime_expression_and_trailing_silence()
    {
        //Arrange
        var recording = new MidiRecording(new[] {
            Message(0, 0xF0, 0x7D, 1, 2, 0xF7), Message(.1, 0xC0, 7),
            Message(.2, 0x90, 60, 100), Message(.3, 0xE0, 0, 80), Message(.4, 0xD0, 20),
            Message(.5, 0xA0, 60, 10), Message(.6, 0xF2, 1, 2), Message(.7, 0xF8),
            Message(.8, 0x80, 60, 60), Message(.9, 0xFF)
        }, TimeSpan.FromSeconds(2), null);
        using var stream = new MemoryStream();
        //Act
        MidiFile.Export(stream, recording.ToMidiEventCollection(releaseAtEnd: false), leaveOpen: true);
        stream.Position = 0;
        var read = new MidiFile(stream, MidiReadMode.Strict);
        var playback = MidiPlaybackSequence.FromMidiFile(read);
        //Assert
        Assert.Empty(read.Problems);
        Assert.Equal(recording.Messages.Select(m => m.Message.ToString()), playback.Messages.Select(m => m.Message.ToString()));
        Assert.Equal(recording.Messages.Select(m => m.Time), playback.Messages.Select(m => m.Time));
        Assert.Equal(recording.Duration, playback.Duration);
    }

    [Fact]
    public void Export_releases_held_notes_without_mutating_capture()
    {
        //Arrange
        var recording = new MidiRecording(new[] { Message(0, 0x90, 60, 100) }, TimeSpan.FromSeconds(1), null);
        //Act
        var exported = recording.ToMidiEventCollection();
        //Assert
        Assert.Single(recording.Messages);
        Assert.Contains(exported[0], e => e is NoteEvent note && note.CommandCode == MidiCommandCode.NoteOff && note.AbsoluteTime == 1920);
    }

    [Fact]
    public void Tempo_changes_apply_across_tracks_without_rounding_drift()
    {
        //Arrange
        var events = new MidiEventCollection(1, 480);
        var conductor = events.AddTrack();
        conductor.Add(new TempoEvent(500000, 0));
        conductor.Add(new TempoEvent(1000000, 480));
        conductor.Add(new MetaEvent(MetaEventType.EndTrack, 0, 1440));
        events.AddTrack().Add(new NoteEvent(960, 1, MidiCommandCode.NoteOff, 60, 0));
        //Act
        var sequence = MidiPlaybackSequence.FromEvents(events);
        //Assert
        Assert.Equal(TimeSpan.FromSeconds(1.5), sequence.Messages[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(2.5), sequence.Duration);
    }

    [Fact]
    public void Smpte_drop_frame_uses_30000_over_1001_frames_per_second()
    {
        //Arrange
        var events = new MidiEventCollection(0, unchecked((short)0xE350));
        events.AddTrack().Add(new NoteEvent(2400, 1, MidiCommandCode.NoteOff, 60, 0));
        //Act, Assert
        Assert.Equal(TimeSpan.FromTicks(10010000), MidiPlaybackSequence.FromEvents(events).Duration);
    }

    [Fact]
    public void Split_sysex_is_rejected_before_any_device_output()
    {
        //Arrange
        var events = new MidiEventCollection(0, 480);
        events.AddTrack().Add(SysexEvent.FromFileData(0, MidiCommandCode.Sysex, new byte[] { 0x7D, 1 }));
        //Act, Assert
        Assert.Throws<NotSupportedException>(() => MidiPlaybackSequence.FromEvents(events));
    }
}

using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiSequence"/> reading content the Standard MIDI File specification does not
/// allow. The playback reader has always shrugged off a great deal in silence; these tests hold it
/// to the same fixture as the editable reader and to the same Problems reporting.
/// </summary>
public class MidiSequenceLeniency
{
    [Fact]
    public void a_file_with_every_known_departure_loads_by_default()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.Bytes());

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Length.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void a_clean_file_reports_no_problems()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.CleanBytes());

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_note_that_is_never_released_is_released_at_the_end_of_its_track_and_reported()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.Bytes());

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("still sounding"));
    }

    [Fact]
    public void the_mangled_track_name_is_kept_byte_for_byte()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.Bytes());

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        var name = sequence.TextMetas.First(m => m.Kind == MetaEventType.SequenceTrackName);
        name.ToArray().Should().Equal(SunoShapedMidi.MangledTrackName());
        name.TrackNumber.Should().Be(0);
    }

    [Fact]
    public void text_metas_carry_a_best_effort_string()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.CleanBytes());

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.TextMetas.Select(m => m.Text).Should().Equal(["Fake Song", "Fake Song (Drums)"]);
    }

    [Fact]
    public void a_track_without_an_end_of_track_event_is_completed_and_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("without an end-of-track event"));
    }

    [Fact]
    public void a_track_without_an_end_of_track_event_is_rejected_in_strict_mode()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var act = () => ReadStrict(bytes);

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void running_status_survives_a_meta_event()
    {
        //Arrange - a text meta between two channel messages, the second using running status
        var track = new MidiTrackBuilder()
            .NoteOn(0, 1, 60, 100)
            .Meta(0, 0x01, (byte)'x')
            .RawBytes(0x00, 0x40, 0x64)
            .RawBytes(0x0A, 0x3C, 0x00)
            .RawBytes(0x00, 0x40, 0x00)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert - both notes were understood, so neither was left sounding
        sequence.Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_running_status_byte_with_nothing_to_run_from_abandons_the_track_and_is_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder().RawBytes(0x00, 0x3C, 0x40).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("before any status byte"));
    }

    [Fact]
    public void a_system_real_time_byte_does_not_eat_the_bytes_that_follow_it()
    {
        //Arrange - a timing clock between two notes; read as a channel message it swallows a note
        var track = new MidiTrackBuilder()
            .NoteOn(0, 1, 60, 100)
            .Raw(0, 0xF8)
            .NoteOff(10, 1, 60, 0)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert - the note off was found, so nothing was left sounding
        sequence.Problems.Should().NotContain(p => p.Contains("still sounding"));
        sequence.Problems.Should().Contain(p => p.Contains("0xF8"));
    }

    [Fact]
    public void a_malformed_tempo_event_is_skipped_and_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder()
            .MetaWithDeclaredLength(0, 0x51, 4, 0x07, 0xA1, 0x20, 0x00)
            .NoteOn(0, 1, 60, 100)
            .NoteOff(10, 1, 60, 0)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("tempo event"));
    }

    [Fact]
    public void a_malformed_tempo_event_is_rejected_in_strict_mode()
    {
        //Arrange
        var track = new MidiTrackBuilder()
            .MetaWithDeclaredLength(0, 0x51, 4, 0x07, 0xA1, 0x20, 0x00)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var act = () => ReadStrict(bytes);

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void a_chunk_that_is_not_a_track_is_skipped_and_reported()
    {
        //Arrange
        var alien = SyntheticMidi.AlienChunk("XFIR", 1, 2, 3, 4, 5);
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.FileDeclaring(1, SyntheticMidi.Division, 1, alien, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("XFIR"));
        sequence.Length.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void an_unsupported_division_is_replaced_and_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(0, 0, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("division"));
    }

    [Fact]
    public void an_unsupported_division_is_rejected_in_strict_mode()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(0, 0, track);

        //Act
        var act = () => ReadStrict(bytes);

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void an_unsupported_file_format_is_merged_and_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(2, SyntheticMidi.Division, track);
        using var stream = new MemoryStream(bytes);

        //Act
        var sequence = new MidiSequence(stream);

        //Assert
        sequence.Problems.Should().Contain(p => p.Contains("format 2"));
    }

    [Fact]
    public void an_unsupported_file_format_is_rejected_in_strict_mode()
    {
        //Arrange
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(2, SyntheticMidi.Division, track);

        //Act
        var act = () => ReadStrict(bytes);

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void both_readers_agree_on_the_departures_they_found()
    {
        //Arrange
        var bytes = SunoShapedMidi.Bytes();
        using var forFile = new MemoryStream(bytes);
        using var forSequence = new MemoryStream(bytes);

        //Act
        var midiFile = new MidiFile(forFile);
        var sequence = new MidiSequence(forSequence);

        //Assert - each reader notices what it models: the file reader the key signature, both the
        //hanging note. Neither throws, and neither reports a clean file as dirty.
        midiFile.Problems.Should().NotBeEmpty();
        sequence.Problems.Should().NotBeEmpty();
    }

    private static MidiSequence ReadStrict(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new MidiSequence(stream, MidiReadMode.Strict);
    }
}

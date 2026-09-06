using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// Covers <see cref="MidiFile"/> reading content the Standard MIDI File specification does not
/// allow: the shapes a stems export produces, plus the malformed shapes that used to lose the
/// reader its place in a track.
/// </summary>
public class MidiFileLeniency
{
    [Fact]
    public void a_file_with_every_known_departure_loads_by_default()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.Bytes());

        //Act
        var midiFile = new MidiFile(stream);

        //Assert
        midiFile.ReadMode.Should().Be(MidiReadMode.Tolerant);
        midiFile.Tracks.Should().Be(2);
        midiFile.DeltaTicksPerQuarterNote.Should().Be(SyntheticMidi.Division);
    }

    [Fact]
    public void the_same_file_is_rejected_in_strict_mode()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.Bytes());

        //Act
        var act = () => new MidiFile(stream, MidiReadMode.Strict);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void a_clean_file_reports_no_problems()
    {
        //Arrange
        using var stream = new MemoryStream(SunoShapedMidi.CleanBytes());

        //Act
        var midiFile = new MidiFile(stream);

        //Assert
        midiFile.Problems.Should().BeEmpty();
    }

    [Fact]
    public void the_out_of_range_key_signature_is_kept_and_reported()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        var keySignature = midiFile.Events[0].OfType<KeySignatureEvent>().Single();

        //Assert
        keySignature.RawSharpsFlats.Should().Be(SunoShapedMidi.KeySignatureSharpsFlats);
        keySignature.IsWithinSpecification.Should().BeFalse();
        midiFile.Problems.Should().Contain(p => p.Contains("Key signature") && p.Contains("13"));
    }

    [Fact]
    public void a_meta_event_type_the_package_does_not_model_is_kept_verbatim_and_is_not_a_problem()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        var raw = midiFile.Events[1].OfType<RawMetaEvent>()
            .Single(e => (byte)e.MetaEventType == SunoShapedMidi.UnknownMetaType);

        //Assert
        raw.Data.Should().Equal([(byte)0x01, 0x02, 0x03]);
        midiFile.Problems.Should().NotContain(p => p.Contains("0x60"));
    }

    [Fact]
    public void a_note_on_without_a_note_off_is_closed_at_the_end_of_its_track_and_reported()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        var hanging = midiFile.Events[1].OfType<NoteOnEvent>().Single(n => n.NoteNumber == 42);

        //Assert
        hanging.OffEvent.Should().NotBeNull();
        hanging.NoteLength.Should().Be(SyntheticMidi.Division);
        midiFile.Problems.Should().Contain(p => p.Contains("no note off"));
    }

    [Fact]
    public void the_closing_note_off_keeps_the_track_exportable()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        using var exported = new MemoryStream();
        MidiFile.Export(exported, midiFile.Events, leaveOpen: true);
        exported.Position = 0;
        var reread = new MidiFile(exported);

        //Assert
        MidiEvent.IsEndTrack(midiFile.Events[1][midiFile.Events[1].Count - 1]).Should().BeTrue();
        reread.Problems.Should().NotContain(p => p.Contains("no note off"));
    }

    [Fact]
    public void an_out_of_range_key_signature_survives_an_export_round_trip()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        using var exported = new MemoryStream();
        MidiFile.Export(exported, midiFile.Events, leaveOpen: true);
        exported.Position = 0;
        var reread = new MidiFile(exported);

        //Assert
        var keySignature = reread.Events[0].OfType<KeySignatureEvent>().Single();
        keySignature.RawSharpsFlats.Should().Be(SunoShapedMidi.KeySignatureSharpsFlats);
        keySignature.RawMajorMinor.Should().Be(0);
    }

    [Fact]
    public void the_mangled_track_name_is_kept_byte_for_byte()
    {
        //Arrange
        var midiFile = Read(SunoShapedMidi.Bytes());

        //Act
        var name = midiFile.Events[0].OfType<TextEvent>()
            .Single(t => t.MetaEventType == MetaEventType.SequenceTrackName);

        //Assert
        name.Data.Should().Equal(SunoShapedMidi.MangledTrackName());
    }

    [Fact]
    public void a_meta_event_whose_payload_does_not_match_its_type_is_kept_as_raw_data_and_reported()
    {
        //Arrange - a tempo event declaring four bytes instead of three
        var track = new MidiTrackBuilder()
            .MetaWithDeclaredLength(0, 0x51, 4, 0x07, 0xA1, 0x20, 0x00)
            .NoteOn(0, 1, 60, 100)
            .NoteOff(10, 1, 60, 0)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[0].OfType<RawMetaEvent>().Should().ContainSingle();
        midiFile.Events[0].OfType<NoteOnEvent>().Should().ContainSingle();
        midiFile.Problems.Should().Contain(p => p.Contains("0x51"));
    }

    [Fact]
    public void a_malformed_meta_event_is_still_rejected_in_strict_mode()
    {
        //Arrange
        var track = new MidiTrackBuilder()
            .MetaWithDeclaredLength(0, 0x51, 4, 0x07, 0xA1, 0x20, 0x00)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var act = () => Read(bytes, MidiReadMode.Strict);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void an_end_of_track_event_carrying_data_does_not_derail_the_track()
    {
        //Arrange
        var track = new MidiTrackBuilder()
            .NoteOn(0, 1, 60, 100)
            .NoteOff(10, 1, 60, 0)
            .MetaWithDeclaredLength(0, 0x2F, 1, 0x00)
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[0].Count.Should().Be(3);
        MidiEvent.IsEndTrack(midiFile.Events[0][2]).Should().BeTrue();
        midiFile.Problems.Should().Contain(p => p.Contains("0x2F"));
    }

    [Fact]
    public void a_note_off_without_a_note_on_is_kept_and_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder()
            .NoteOff(0, 1, 60, 64)
            .EndOfTrack()
            .ToChunk();
        var bytes = SyntheticMidi.File(0, SyntheticMidi.Division, track);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[0].Count.Should().Be(2);
        midiFile.Problems.Should().Contain(p => p.Contains("no matching note on"));
    }

    [Fact]
    public void a_running_status_byte_with_nothing_to_run_from_abandons_the_track_and_is_reported()
    {
        //Arrange
        var good = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bad = new MidiTrackBuilder().Raw(0, 0x3C, 0x40).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(1, SyntheticMidi.Division, good, bad);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[0].Count.Should().Be(3);
        midiFile.Events[1].Should().BeEmpty();
        midiFile.Problems.Should().Contain(p => p.Contains("reading stopped"));
    }

    [Fact]
    public void a_chunk_that_is_not_a_track_is_skipped_and_reported()
    {
        //Arrange
        var alien = SyntheticMidi.AlienChunk("XFIR", 1, 2, 3, 4, 5);
        var track = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.FileDeclaring(1, SyntheticMidi.Division, 1, alien, track);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[0].Count.Should().Be(3);
        midiFile.Problems.Should().Contain(p => p.Contains("XFIR"));
    }

    [Fact]
    public void a_chunk_that_is_not_a_track_is_still_rejected_in_strict_mode()
    {
        //Arrange
        var alien = SyntheticMidi.AlienChunk("XFIR", 1, 2, 3, 4, 5);
        var track = new MidiTrackBuilder().EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.FileDeclaring(1, SyntheticMidi.Division, 1, alien, track);

        //Act
        var act = () => Read(bytes, MidiReadMode.Strict);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void a_file_that_declares_more_tracks_than_it_holds_is_reported()
    {
        //Arrange
        var track = new MidiTrackBuilder().EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.FileDeclaring(1, SyntheticMidi.Division, 2, track);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Tracks.Should().Be(2);
        midiFile.Events[1].Should().BeEmpty();
        midiFile.Problems.Should().Contain(p => p.Contains("only 1 were found"));
    }

    [Fact]
    public void a_track_chunk_longer_than_its_events_is_reported_and_the_next_track_still_reads()
    {
        //Arrange - a track chunk padded with four unread bytes after the end-of-track event
        var events = new MidiTrackBuilder().NoteOn(0, 1, 60, 100).NoteOff(10, 1, 60, 0).EndOfTrack().ToEventBytes();
        var padded = new System.Collections.Generic.List<byte>();
        padded.AddRange("MTrk"u8.ToArray());
        padded.AddRange(SyntheticMidi.BigEndian32(events.Length + 4));
        padded.AddRange(events);
        padded.AddRange([(byte)0, 0, 0, 0]);
        var second = new MidiTrackBuilder().NoteOn(0, 2, 64, 100).NoteOff(10, 2, 64, 0).EndOfTrack().ToChunk();
        var bytes = SyntheticMidi.File(1, SyntheticMidi.Division, padded.ToArray(), second);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Events[1].Count.Should().Be(3);
        midiFile.Problems.Should().Contain(p => p.Contains("moved to the end of the chunk"));
    }

    [Fact]
    public void the_problem_list_is_capped()
    {
        //Arrange - far more orphan note-offs than the cap, spread over their own tracks
        var chunks = new byte[300][];
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i] = new MidiTrackBuilder().NoteOff(0, 1, 60, 64).EndOfTrack().ToChunk();
        }
        var bytes = SyntheticMidi.File(1, SyntheticMidi.Division, chunks);

        //Act
        var midiFile = Read(bytes);

        //Assert
        midiFile.Problems.Count.Should().Be(201);
        midiFile.Problems[200].Should().Contain("not recorded");
    }

    private static MidiFile Read(byte[] bytes, MidiReadMode readMode = MidiReadMode.Tolerant)
    {
        using var stream = new MemoryStream(bytes);
        return new MidiFile(stream, readMode);
    }
}

using System;
using System.IO;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// Covers <see cref="KeySignatureEvent"/> reading a sharps/flats or major/minor byte that the
/// Standard MIDI File specification does not allow - which every stems export from at least one
/// online music service contains.
/// </summary>
public class KeySignatureEventLeniency
{
    [Theory]
    [InlineData(11, 0)]
    [InlineData(13, 0)]
    [InlineData(13, 1)]
    [InlineData(14, 1)]
    [InlineData(16, 0)]
    [InlineData(0, 7)]
    public void tolerant_read_keeps_the_raw_bytes(int rawSharpsFlats, int rawMajorMinor)
    {
        //Arrange
        var bytes = new byte[] { unchecked((byte)rawSharpsFlats), (byte)rawMajorMinor };

        //Act
        var keySignature = Read(bytes, MidiReadMode.Tolerant);

        //Assert
        keySignature.RawSharpsFlats.Should().Be(rawSharpsFlats);
        keySignature.RawMajorMinor.Should().Be(rawMajorMinor);
        keySignature.IsWithinSpecification.Should().BeFalse();
    }

    [Fact]
    public void strict_read_throws_for_sharps_flats_out_of_range()
    {
        //Arrange
        var bytes = new byte[] { 13, 0 };

        //Act
        var act = () => Read(bytes, MidiReadMode.Strict);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void strict_read_throws_for_major_minor_out_of_range()
    {
        //Arrange
        var bytes = new byte[] { 0, 2 };

        //Act
        var act = () => Read(bytes, MidiReadMode.Strict);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void the_two_argument_constructor_is_still_strict()
    {
        //Arrange
        var bytes = new byte[] { 13, 0 };

        //Act
        var act = () =>
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            return new KeySignatureEvent(reader, 2);
        };

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(MidiReadMode.Tolerant)]
    [InlineData(MidiReadMode.Strict)]
    public void a_wrong_length_is_rejected_in_either_mode(MidiReadMode readMode)
    {
        //Arrange
        var bytes = new byte[] { 0, 0, 0 };

        //Act
        var act = () => Read(bytes, readMode, length: 3);

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void an_in_range_value_reads_the_same_way_in_either_mode()
    {
        //Arrange
        var bytes = new byte[] { 0xF9, 0x01 };

        //Act
        var tolerant = Read(bytes, MidiReadMode.Tolerant);
        var strict = Read(bytes, MidiReadMode.Strict);

        //Assert
        tolerant.SharpsFlats.Should().Be(-7);
        tolerant.MajorMinor.Should().Be(1);
        tolerant.IsWithinSpecification.Should().BeTrue();
        tolerant.KeyName.Should().Be(strict.KeyName);
    }

    [Fact]
    public void key_name_describes_the_raw_bytes_when_they_are_out_of_range()
    {
        //Arrange
        var keySignature = Read([13, 0], MidiReadMode.Tolerant);

        //Act
        var name = keySignature.KeyName;

        //Assert
        name.Should().Be("Unknown key (sf 13, mi 0)");
    }

    [Fact]
    public void an_out_of_range_event_exports_the_bytes_it_was_given()
    {
        //Arrange
        var keySignature = KeySignatureEvent.FromRawValues(13, 0, 0);

        //Act
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        long absoluteTime = 0;
        keySignature.Export(ref absoluteTime, writer);

        //Assert
        stream.ToArray().Should().Equal([(byte)0x00, 0xFF, 0x59, 0x02, 0x0D, 0x00]);
    }

    [Fact]
    public void from_raw_values_builds_an_out_of_specification_event()
    {
        //Arrange
        var keySignature = KeySignatureEvent.FromRawValues(16, 0, 7);

        //Act
        var within = keySignature.IsWithinSpecification;

        //Assert
        within.Should().BeFalse();
        keySignature.RawSharpsFlats.Should().Be(16);
        keySignature.AbsoluteTime.Should().Be(7);
        keySignature.MetaEventType.Should().Be(MetaEventType.KeySignature);
    }

    [Fact]
    public void from_raw_values_accepts_an_ordinary_key_signature()
    {
        //Arrange
        var keySignature = KeySignatureEvent.FromRawValues(-2, 0, 0);

        //Act
        var name = keySignature.KeyName;

        //Assert
        keySignature.IsWithinSpecification.Should().BeTrue();
        keySignature.SharpsFlats.Should().Be(-2);
        name.Should().Be("Bb major");
    }

    [Fact]
    public void from_raw_values_rejects_a_value_that_does_not_fit_in_a_byte() =>
        new Action(() => KeySignatureEvent.FromRawValues(300, 0, 0)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void an_out_of_range_event_survives_a_clone()
    {
        //Arrange
        var keySignature = KeySignatureEvent.FromRawValues(14, 1, 3);

        //Act
        var clone = (KeySignatureEvent)keySignature.Clone();

        //Assert
        clone.RawSharpsFlats.Should().Be(14);
        clone.RawMajorMinor.Should().Be(1);
        clone.IsWithinSpecification.Should().BeFalse();
    }

    [Fact]
    public void an_out_of_range_event_names_itself_without_throwing()
    {
        //Arrange
        var keySignature = KeySignatureEvent.FromRawValues(13, 0, 0);

        //Act
        var text = keySignature.ToString();

        //Assert
        text.Should().Contain("Unknown key");
    }

    private static KeySignatureEvent Read(byte[] data, MidiReadMode readMode, int length = 2)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return new KeySignatureEvent(reader, length, readMode);
    }
}

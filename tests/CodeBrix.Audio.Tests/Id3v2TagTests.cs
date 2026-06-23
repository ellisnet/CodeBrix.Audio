using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="Id3v2Tag"/> creation and reading.
/// </summary>
public class Id3v2TagTests
{
    [Fact]
    public void Created_tag_can_be_read_back_from_its_raw_bytes()
    {
        //Arrange
        var tags = new List<KeyValuePair<string, string>>
        {
            new("TIT2", "Holy Inanna"),
            new("TPE1", "CodeBrix"),
        };
        var created = Id3v2Tag.Create(tags);

        //Act
        using var ms = new MemoryStream(created.RawData);
        var read = Id3v2Tag.ReadTag(ms);

        //Assert
        read.Should().NotBeNull();
        read.RawData.Should().Equal(created.RawData);
    }

    [Fact]
    public void ReadTag_returns_null_when_there_is_no_id3v2_tag()
    {
        //Arrange — a bare stream that does not start with the "ID3" marker
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFB, 0x90, 0xC0 });

        //Act
        var tag = Id3v2Tag.ReadTag(ms);

        //Assert
        tag.Should().BeNull();
    }
}

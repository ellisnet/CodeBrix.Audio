using CodeBrix.Audio.Utils;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class ChunkIdentifierTests
{
    [Theory]
    [InlineData("WAVE", 0x45564157)]
    [InlineData("data", 0x61746164)]
    [InlineData("fmt ", 0x20746D66)]
    [InlineData("RF64", 0x34364652)]
    [InlineData("ds64", 0x34367364)]
    [InlineData("labl", 0x6C62616C)]
    [InlineData("cue ", 0x20657563)]
    public void CanConvertChunkIdentifierToInt(string chunkIdentifier, int expected)
    {
        Assert.Equal(expected, ChunkIdentifier.ChunkIdentifierToInt32(chunkIdentifier));
    }
}

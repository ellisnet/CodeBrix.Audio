using System.IO;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Tests.Utils;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Mp3;
public class Mp3FileReaderBaseTests
{
    [Fact]
    public void DisposesFileOnFailToParse()
    {
        // If File.Delete here fails with a sharing violation, the ctor failed to release
        // the file handle on its parsing-error path (see Mp3FileReaderBase ctor catch block).
        string tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, "Some test content");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new Mp3FileReaderBase(tempFilePath, fmt => new FakeMp3FrameDecompressor(fmt)));
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void CopesWithZeroLengthStream()
    {
        var ms = new MemoryStream(new byte[0]);
        Assert.Throws<InvalidDataException>(() =>
            new Mp3FileReaderBase(ms, fmt => new FakeMp3FrameDecompressor(fmt)));
    }
}

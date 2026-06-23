using System;
using System.Linq;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class SilenceProviderTests
{
    [Fact]
    public void CanReadSilence()
    {
        var sp = new SilenceProvider(new WaveFormat(44100, 2));
        var length = 1000;
        var b = Enumerable.Range(1, length).Select(n => (byte) 1).ToArray();
        var read = sp.Read(b.AsSpan());
        Assert.Equal(length, read);
        Assert.Equal(new byte[length], b);
    }

    [Fact]
    public void ClearsEntireSpan()
    {
        var sp = new SilenceProvider(new WaveFormat(44100, 2));
        var length = 4;
        var b = Enumerable.Range(1, length).Select(n => (byte)1).ToArray();
        var read = sp.Read(b.AsSpan());
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, b);
    }
}

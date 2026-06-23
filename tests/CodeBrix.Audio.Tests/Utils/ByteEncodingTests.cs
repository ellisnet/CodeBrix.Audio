using System;
using System.Linq;
using CodeBrix.Audio.Utils;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Utils;
public class ByteEncodingTests
{
    [Fact]
    public void CanDecodeString()
    {
        var b = new byte[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', };
        Assert.Equal("Hello", ByteEncoding.Instance.GetString(b));
    }

    [Fact]
    public void CanTruncate()
    {
        var b = new byte[] {(byte) 'H', (byte) 'e', (byte) 'l', (byte) 'l', (byte) 'o', 0};
        Assert.Equal("Hello", ByteEncoding.Instance.GetString(b));
    }

    [Fact]
    public void CanTruncateWithThreeParamOverride()
    {
        var b = new byte[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0 };
        Assert.Equal("Hello", ByteEncoding.Instance.GetString(b,0,b.Length));
    }
}

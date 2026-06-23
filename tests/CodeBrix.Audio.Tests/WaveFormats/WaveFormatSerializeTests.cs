using System.IO;
using CodeBrix.Audio.Utils;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveFormats;
public class WaveFormatSerializeTests
{
    private static (int declaredLength, byte[] body) Serialize(WaveFormat format)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            format.Serialize(writer);
        }
        var bytes = ms.ToArray();
        var declaredLength = System.BitConverter.ToInt32(bytes, 0);
        var body = new byte[bytes.Length - 4];
        System.Array.Copy(bytes, 4, body, 0, body.Length);
        return (declaredLength, body);
    }

    [Fact]
    public void PcmSerializesAs16ByteChunkWithoutCbSize()
    {
        var format = new WaveFormat(44100, 16, 2);
        var (declaredLength, body) = Serialize(format);

        Assert.Equal(16, declaredLength);
        Assert.Equal(16, body.Length);
    }

    [Fact]
    public void NonPcmStillSerializesCbSize()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var (declaredLength, body) = Serialize(format);

        Assert.Equal(18, declaredLength);
        Assert.Equal(18, body.Length);
    }

    [Fact]
    public void PcmRoundTripsThroughSerialize()
    {
        var format = new WaveFormat(22050, 24, 1);
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            format.Serialize(writer);
        }
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var roundTripped = new WaveFormat(reader);

        Assert.Equal(format, roundTripped);
        Assert.Equal(0, roundTripped.ExtraSize);
        Assert.Equal(ms.Length, ms.Position);
    }

    [Fact]
    public void PcmRoundTripsThroughWaveFileWriter()
    {
        var format = new WaveFormat(16000, 16, 1);
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), format))
        {
            writer.Write(new byte[64], 0, 64);
        }
        ms.Position = 0;
        using var reader = new WaveFileReader(ms);

        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(64, reader.Length);
    }
}

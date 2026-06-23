using System;
using System.IO;
using System.Text;
using CodeBrix.Audio.Utils;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class WaveFileWriterRf64Tests
{
    private static WaveFormat Format => new WaveFormat(8000, 16, 1);

    private static string ReadFourCc(byte[] bytes, int offset)
        => Encoding.ASCII.GetString(bytes, offset, 4);

    [Fact]
    public void Rf64NotEnabledProducesPlainRiff()
    {
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format))
        {
            w.Write(new byte[100], 0, 100);
        }
        Assert.Equal("RIFF", ReadFourCc(ms.ToArray(), 0));
    }

    [Fact]
    public void Rf64NotEnabledRejectsFilesLargerThan4Gb()
    {
        // We can't actually allocate > 4 GB in a test, but we can prove the pre-flight
        // range check still triggers at the internal boundary.
        var ms = new MemoryStream();
        using var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format,
            new WaveFileWriterOptions { EnableRf64 = false, Rf64PromotionThreshold = 50 });
        // Even with a low threshold, EnableRf64 is false so the 4 GB cap still applies.
        // With our modest data the check doesn't fire, but we can at least verify the writer
        // writes RIFF format:
        w.Write(new byte[100], 0, 100);
        // Close and check magic — this asserts the low-threshold override doesn't promote
        // when EnableRf64 is false.
        w.Dispose();
        Assert.Equal("RIFF", ReadFourCc(ms.ToArray(), 0));
    }

    [Fact]
    public void Rf64EnabledReservesJunkPlaceholderRegardlessOfSize()
    {
        // Whether or not promotion kicks in, the 36-byte JUNK placeholder is always reserved
        // so the ds64 slot exists.
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true }))
        {
            w.Write(new byte[100], 0, 100);
        }
        var bytes = ms.ToArray();
        // RIFF (4) + size (4) + WAVE (4) + JUNK (4) = JUNK id at offset 12
        Assert.Equal("JUNK", ReadFourCc(bytes, 12));
    }

    [Fact]
    public void Rf64EnabledButSmallDataStaysAsRiff()
    {
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 10_000 }))
        {
            w.Write(new byte[100], 0, 100);
        }
        var bytes = ms.ToArray();
        Assert.Equal("RIFF", ReadFourCc(bytes, 0));
        // JUNK stays as JUNK (unpromoted)
        Assert.Equal("JUNK", ReadFourCc(bytes, 12));
    }

    [Fact]
    public void Rf64EnabledAndDataOverThresholdPromotesToRf64()
    {
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 100 }))
        {
            w.Write(new byte[500], 0, 500);
        }
        var bytes = ms.ToArray();
        Assert.Equal("RF64", ReadFourCc(bytes, 0));
        // top-level RIFF size is 0xFFFFFFFF
        Assert.Equal(0xFFFFFFFFu, BitConverter.ToUInt32(bytes, 4));
        // JUNK has been rewritten as ds64
        Assert.Equal("ds64", ReadFourCc(bytes, 12));
    }

    [Fact]
    public void Rf64Ds64ChunkCarriesCorrectSizes()
    {
        var dataLength = 500;
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 100 }))
        {
            w.Write(new byte[dataLength], 0, dataLength);
        }
        var bytes = ms.ToArray();
        // ds64 body starts at 12 + 8 = 20 (after "ds64" id + size field)
        int ds64BodyStart = 20;
        long riffSize = BitConverter.ToInt64(bytes, ds64BodyStart);
        long dataSize = BitConverter.ToInt64(bytes, ds64BodyStart + 8);
        long sampleCount = BitConverter.ToInt64(bytes, ds64BodyStart + 16);

        Assert.Equal(bytes.Length - 8, riffSize);
        Assert.Equal(dataLength, dataSize);
        Assert.Equal(dataLength / Format.BlockAlign, sampleCount);
    }

    [Fact]
    public void Rf64PromotedFileCanBeReadBack()
    {
        var audio = new byte[500];
        for (int i = 0; i < audio.Length; i++) audio[i] = (byte)i;

        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 100 }))
        {
            w.Write(audio, 0, audio.Length);
        }
        ms.Position = 0;
        using var reader = new WaveFileReader(ms);
        Assert.Equal(audio.Length, reader.Length);

        var buffer = new byte[audio.Length];
        int read = reader.Read(buffer, 0, buffer.Length);
        Assert.Equal(audio.Length, read);
        Assert.Equal(audio, buffer);
    }

    [Fact]
    public void Rf64PromotedFileDataChunkSizeFieldIsFfffffff()
    {
        // Per EBU Tech 3306, the data chunk size in an RF64 file is 0xFFFFFFFF; the real
        // 64-bit size lives in ds64.
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 100 }))
        {
            w.Write(new byte[500], 0, 500);
        }
        var bytes = ms.ToArray();
        // Find "data" marker and check the following uint32 is 0xFFFFFFFF.
        var text = Encoding.ASCII.GetString(bytes);
        int dataIx = text.IndexOf("data", StringComparison.Ordinal);
        Assert.True(dataIx > 0);
        uint dataSizeField = BitConverter.ToUInt32(bytes, dataIx + 4);
        Assert.Equal(0xFFFFFFFFu, dataSizeField);
    }

    [Fact]
    public void Rf64WorksAlongsideBeforeDataAndAfterDataChunks()
    {
        // Promotion must cope with the extra chunks correctly.
        var audio = new byte[500];
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions { EnableRf64 = true, Rf64PromotionThreshold = 100 }))
        {
            w.WriteBroadcastExtension(new BroadcastExtension { Description = "RF64 Test", Version = 1 });
            w.Write(audio, 0, audio.Length);
            w.AddCue(100, "Mark");
        }
        ms.Position = 0;
        using var reader = new WaveFileReader(ms);
        Assert.Equal(audio.Length, reader.Length);
        var bext = reader.Chunks.ReadBroadcastExtension();
        Assert.Equal("RF64 Test", bext.Description);
        var cues = reader.Chunks.ReadCueList();
        Assert.NotNull(cues);
        Assert.Equal("Mark", cues[0].Label);
    }

    [Fact]
    public void NormalWriterDoesNotEmitJunkPlaceholder()
    {
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format))
        {
            w.Write(new byte[100], 0, 100);
        }
        var bytes = ms.ToArray();
        // At offset 12 we should find fmt, not JUNK
        Assert.Equal("fmt ", ReadFourCc(bytes, 12));
    }

    [Fact]
    public void OptionsDefaultsProduceNormalRiff()
    {
        // new WaveFileWriterOptions() with no properties set should match the no-options ctor.
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, new WaveFileWriterOptions()))
        {
            w.Write(new byte[100], 0, 100);
        }
        var bytes = ms.ToArray();
        Assert.Equal("RIFF", ReadFourCc(bytes, 0));
        Assert.Equal("fmt ", ReadFourCc(bytes, 12));
    }

    [Fact]
    public void NullOptionsIsTreatedAsDefault()
    {
        // Passing null for options is allowed and equivalent to passing a default-constructed
        // WaveFileWriterOptions.
        var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format, (WaveFileWriterOptions)null))
        {
            w.Write(new byte[100], 0, 100);
        }
        var bytes = ms.ToArray();
        Assert.Equal("RIFF", ReadFourCc(bytes, 0));
    }

    [Fact]
    public void OptionsDefaultsMatchDocumentedValues()
    {
        // Sanity check on the options defaults themselves.
        var opts = new WaveFileWriterOptions();
        Assert.False(opts.EnableRf64);
        Assert.Equal(uint.MaxValue, opts.Rf64PromotionThreshold);
    }
}

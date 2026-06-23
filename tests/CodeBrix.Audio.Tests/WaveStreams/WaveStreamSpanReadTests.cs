using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeBrix.Audio.Utils;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Tests.Shared;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;

/// <summary>
/// Parity and architectural tests for the Span-based Read path on WaveStream subclasses.
/// Ensures: (a) calling Read(Span&lt;byte&gt;) yields the same bytes as Read(byte[], int, int);
/// (b) NAudio's concrete readers all override Read(Span&lt;byte&gt;) directly (no bridge-copy).
/// </summary>
public class WaveStreamSpanReadTests
{
    /// <summary>
    /// Build a 1kHz sine-in-WAV byte array we can feed repeatedly to readers.
    /// </summary>
    private static byte[] Build16BitMonoPcmWav(int sampleCount = 4096, int sampleRate = 44100)
    {
        var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), new WaveFormat(sampleRate, 16, 1)))
        {
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 1000.0 * i / sampleRate) * 16000);
                writer.WriteSample(sample / 32768f);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Read a bounded number of bytes from a WaveStream using the byte[] overload.
    /// Some streams (WaveOffsetStream, WaveChannel32 with PadWithZeroes) never return 0 — the
    /// caller's expected playout length terminates the loop instead.
    /// </summary>
    private static byte[] ReadAllViaByteArray(WaveStream stream, int chunkSize, long? expectedLength = null)
    {
        stream.Position = 0;
        long bound = expectedLength ?? stream.Length;
        var ms = new MemoryStream();
        var buffer = new byte[chunkSize];
        int read;
        while (ms.Length < bound && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, bound - ms.Length))) > 0)
        {
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Read a bounded number of bytes from a WaveStream using the Span overload.
    /// </summary>
    private static byte[] ReadAllViaSpan(WaveStream stream, int chunkSize, long? expectedLength = null)
    {
        stream.Position = 0;
        long bound = expectedLength ?? stream.Length;
        var ms = new MemoryStream();
        var buffer = new byte[chunkSize];
        int read;
        while (ms.Length < bound && (read = stream.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, bound - ms.Length)))) > 0)
        {
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static void AssertReadParity(WaveStream stream, int chunkSize = 1024, long? expectedLength = null)
    {
        var viaArray = ReadAllViaByteArray(stream, chunkSize, expectedLength);
        var viaSpan = ReadAllViaSpan(stream, chunkSize, expectedLength);
        Assert.Equal(viaArray, viaSpan);
        Assert.True(viaArray.Length > 0);
    }

    [Fact]
    public void WaveFileReader_SpanAndByteArrayRead_Agree()
    {
        var wav = Build16BitMonoPcmWav();
        using var reader = new WaveFileReader(new MemoryStream(wav));
        AssertReadParity(reader);
    }

    [Fact]
    public void WaveFileReader_SpanReadRespectsSliceBoundary()
    {
        var wav = Build16BitMonoPcmWav();
        using var reader = new WaveFileReader(new MemoryStream(wav));
        // Allocate a larger buffer and read through a sliced span; outside the slice must stay 0xFF.
        var outer = new byte[4096];
        Array.Fill(outer, (byte)0xFF);
        int got = reader.Read(outer.AsSpan(256, 1024));
        Assert.Equal(1024, got);
        Assert.Equal(0xFF, outer[0]);
        Assert.Equal(0xFF, outer[255]);
        Assert.Equal(0xFF, outer[256 + 1024]);
    }

    [Fact]
    public void RawSourceWaveStream_SpanAndByteArrayRead_Agree()
    {
        var data = new byte[4096];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        var stream = new RawSourceWaveStream(new MemoryStream(data), new WaveFormat(44100, 16, 1));
        AssertReadParity(stream);
    }

    [Fact]
    public void WaveOffsetStream_SpanAndByteArrayRead_Agree()
    {
        var wav = Build16BitMonoPcmWav();
        using var source = new WaveFileReader(new MemoryStream(wav));
        using var offset = new WaveOffsetStream(source, TimeSpan.FromMilliseconds(5), TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        AssertReadParity(offset);
    }

    [Fact]
    public void BlockAlignReductionStream_SpanAndByteArrayRead_Agree()
    {
        var wav = Build16BitMonoPcmWav();
        using var source = new WaveFileReader(new MemoryStream(wav));
        using var reducer = new BlockAlignReductionStream(source);
        AssertReadParity(reducer);
    }

    [Fact]
    public void WaveChannel32_SpanAndByteArrayRead_Agree()
    {
        var wav = Build16BitMonoPcmWav();
        var source = new WaveFileReader(new MemoryStream(wav));
        using var channel = new WaveChannel32(source);
        AssertReadParity(channel, chunkSize: 2048); // multiple of 8 (stereo float)
    }

    [Fact]
    public void Wave32To16Stream_SpanAndByteArrayRead_Agree()
    {
        // Build a 32-bit IEEE float source, then push it through Wave32To16Stream
        var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms),
                   WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        {
            for (int i = 0; i < 4096; i++)
                writer.WriteSample((float)Math.Sin(2 * Math.PI * 1000.0 * i / 44100) * 0.8f);
        }
        var source = new WaveFileReader(new MemoryStream(ms.ToArray()));
        using var converter = new Wave32To16Stream(source);
        AssertReadParity(converter);
    }


    /// <summary>
    /// Build an AIFF byte array of the requested PCM bit depth. Each bit depth exercises a
    /// different endian-swap branch inside AiffFileReader.Read.
    /// </summary>
    private static byte[] BuildMonoAiff(int bitsPerSample, int sampleCount = 1024, int sampleRate = 44100)
    {
        // AiffFileWriter only accepts 32-bit PCM via WaveFormatExtensible; 16/24-bit use the
        // regular PCM WaveFormat.
        WaveFormat format = bitsPerSample == 32
            ? new WaveFormatExtensible(sampleRate, bitsPerSample, 1)
            : new WaveFormat(sampleRate, bitsPerSample, 1);
        var ms = new MemoryStream();
        using (var writer = new AiffFileWriter(new IgnoreDisposeStream(ms), format))
        {
            for (int i = 0; i < sampleCount; i++)
            {
                writer.WriteSample((float)Math.Sin(2 * Math.PI * 1000.0 * i / sampleRate) * 0.8f);
            }
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void AiffFileReader_SpanAndByteArrayRead_Agree(int bitsPerSample)
    {
        var aiff = BuildMonoAiff(bitsPerSample);
        using var reader = new AiffFileReader(new MemoryStream(aiff));
        AssertReadParity(reader, chunkSize: bitsPerSample / 8 * 256);
    }

    /// <summary>
    /// The legacy <c>Read(byte[], int, int)</c> overload had been broken for non-zero offsets
    /// (it would throw because its internal scratch buffer was sized to count, not offset+count).
    /// After the span refactor the offset is honoured — this test pins that behavior down.
    /// </summary>
    [Fact]
    public void AiffFileReader_ReadWithNonZeroOffset_FillsOnlyRequestedSlice()
    {
        var aiff = BuildMonoAiff(16);
        using var reader = new AiffFileReader(new MemoryStream(aiff));

        // Read the whole thing once via offset 0 as the reference.
        var reference = new byte[(int)reader.Length];
        Assert.Equal(reference.Length, reader.Read(reference, 0, reference.Length));

        // Now read again into a larger sentinel-filled buffer at a non-zero offset.
        reader.Position = 0;
        var outer = new byte[reference.Length + 512];
        Array.Fill(outer, (byte)0xCD);
        int got = reader.Read(outer, 256, reference.Length);

        Assert.Equal(reference.Length, got);
        Assert.Equal(0xCD, outer[0]);
        Assert.Equal(0xCD, outer[255]);
        Assert.Equal(0xCD, outer[256 + reference.Length]);
        Assert.Equal(reference, outer.AsSpan(256, reference.Length).ToArray());
    }

    /// <summary>
    /// Third-party subclasses that only override the legacy byte[] overload must still
    /// deliver correct data when the caller invokes Read(Span&lt;byte&gt;) via the base class bridge.
    /// </summary>
    [Fact]
    public void LegacyByteArrayOnlySubclass_StillBridgesToSpanCallers()
    {
        var stream = new NullWaveStream(new WaveFormat(44100, 16, 1), 4096);

        // Fill the buffer with a sentinel so we can see that the bridge actually wrote into it.
        var buffer = new byte[1024];
        Array.Fill(buffer, (byte)0xAA);

        int read = stream.Read(buffer.AsSpan());

        Assert.Equal(1024, read);
        // NullWaveStream writes zeros — every byte should be 0 after the read.
        Assert.True(buffer.All(b => b == 0));
    }

    /// <summary>
    /// Architectural test: every concrete WaveStream subclass in NAudio's own assemblies should
    /// override Read(Span&lt;byte&gt;) directly. Relying on the Stream default bridge for our own
    /// readers means an ArrayPool rent + a copy per read — avoidable for everything we ship.
    /// </summary>
    [Fact]
    public void AllNAudioWaveStreamSubclasses_OverrideSpanRead()
    {
        // Walk every CodeBrix.Audio.* assembly directly referenced by the test project. Adding a new
        // NAudio package doesn't require editing this test, and the cross-platform TFM
        // naturally sees fewer assemblies (no WinMM/Asio/Wasapi/WinForms) than the Windows TFM.
        var assembliesToCheck = typeof(WaveStreamSpanReadTests).Assembly
            .GetReferencedAssemblies()
            .Where(name => name.Name?.StartsWith("NAudio", StringComparison.Ordinal) == true)
            .Select(Assembly.Load)
            .ToArray();

        var spanReadSig = new[] { typeof(Span<byte>) };

        // Classes we know don't need to override (or physically can't — they're abstract-ish bases).
        // Keep this list tight and justified.
        var exempt = new[]
        {
            "CodeBrix.Audio.Wave.WaveStream", // abstract base
        };

        var offenders = new System.Collections.Generic.List<string>();
        foreach (var asm in assembliesToCheck)
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract) continue;
                if (!typeof(WaveStream).IsAssignableFrom(type)) continue;
                if (exempt.Contains(type.FullName)) continue;

                // Does this type (or any non-WaveStream base between it and WaveStream) override Read(Span<byte>)?
                bool overridesSpan = false;
                var t = type;
                while (t != null && t != typeof(WaveStream))
                {
                    var m = t.GetMethod("Read",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                        binder: null, types: spanReadSig, modifiers: null);
                    if (m != null) { overridesSpan = true; break; }
                    t = t.BaseType;
                }

                // And does it override the byte[] overload? If it overrides neither there's something wrong,
                // but that's a different problem — this test is specifically about the span path.
                if (!overridesSpan)
                {
                    offenders.Add(type.FullName);
                }
            }
        }

        Assert.Empty(offenders);
    }
}

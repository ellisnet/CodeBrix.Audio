using System;
using System.Linq;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;

/// <summary>
/// Tests validating correctness of the Span-first IWaveProvider / ISampleProvider transition.
/// Covers PCM round-trips, cross-boundary reads, buffer-size variation, and the SampleChannel pipeline.
/// </summary>
public class SpanTransitionTests
{
    #region PCM format conversion round-trips

    [Fact]
    public void RoundTrip16Bit_PreservesSignal()
    {
        // float -> 16-bit PCM -> float should preserve signal within quantization error
        var signal = CreateKnownSignal(1000, 44100, 1);
        var sampleToWave = new SampleToWaveProvider16(signal);
        var waveToSample = new Pcm16BitToSampleProvider(sampleToWave);

        var output = new float[1000];
        int read = waveToSample.Read(output.AsSpan());

        Assert.Equal(1000, read);
        for (int i = 0; i < read; i++)
        {
            // 16-bit uses asymmetric scale (encode *32767, decode /32768) so tolerance ~2/32768
            Assert.Equal(signal.Samples[i], output[i], 2.0f / 32768f);
        }
    }

    [Fact]
    public void RoundTrip24Bit_PreservesSignal()
    {
        var signal = CreateKnownSignal(1000, 44100, 1);
        var sampleToWave = new SampleToWaveProvider24(signal);
        var waveToSample = new Pcm24BitToSampleProvider(sampleToWave);

        var output = new float[1000];
        int read = waveToSample.Read(output.AsSpan());

        Assert.Equal(1000, read);
        for (int i = 0; i < read; i++)
        {
            // 24-bit uses asymmetric scale (encode *8388607, decode /8388608) so tolerance ~2/8388608
            Assert.Equal(signal.Samples[i], output[i], 2.0f / 8388608f);
        }
    }

    [Fact]
    public void RoundTrip16BitStereo_PreservesChannelLayout()
    {
        // Stereo signal: L=0.5, R=-0.5
        var signal = new ConstChannelSampleProvider(44100, 2, 500,
            new[] { 0.5f, -0.5f });
        var sampleToWave = new SampleToWaveProvider16(signal);
        var waveToSample = new Pcm16BitToSampleProvider(sampleToWave);

        var output = new float[1000]; // 500 stereo sample pairs
        int read = waveToSample.Read(output.AsSpan());

        Assert.Equal(1000, read);
        for (int i = 0; i < read; i += 2)
        {
            Assert.Equal(0.5f, output[i], 1.0f / 32768f);
            Assert.Equal(-0.5f, output[i + 1], 1.0f / 32768f);
        }
    }

    [Fact]
    public void RoundTrip16Bit_ClipsAboveOne()
    {
        var signal = new ConstChannelSampleProvider(44100, 1, 10,
            new[] { 1.5f });
        var sampleToWave = new SampleToWaveProvider16(signal);
        var waveToSample = new Pcm16BitToSampleProvider(sampleToWave);

        var output = new float[10];
        waveToSample.Read(output.AsSpan());

        for (int i = 0; i < 10; i++)
        {
            // Clipped to 1.0 then quantized
            Assert.Equal(1.0f, output[i], 1.0f / 32768f);
        }
    }

    #endregion

    #region ConcatenatingSampleProvider boundary crossing

    [Fact]
    public void Concatenation_SingleReadSpansBoundary()
    {
        // Two providers of 30 samples each, read with buffer of 60
        // This forces the Span slicing logic to read from both in one call
        var p1 = new TestSampleProvider(44100, 1, 30);
        p1.UseConstValue = true; p1.ConstValue = 1;
        var p2 = new TestSampleProvider(44100, 1, 30);
        p2.UseConstValue = true; p2.ConstValue = 2;

        var concat = new ConcatenatingSampleProvider(new[] { p1, p2 });
        var buffer = new float[60];
        int read = concat.Read(buffer.AsSpan());

        Assert.Equal(60, read);
        // First 30 from p1
        for (int i = 0; i < 30; i++)
            Assert.Equal(1f, buffer[i]);
        // Next 30 from p2
        for (int i = 30; i < 60; i++)
            Assert.Equal(2f, buffer[i]);
    }

    [Fact]
    public void Concatenation_SmallReadsAcrossBoundary()
    {
        var p1 = new TestSampleProvider(44100, 1, 10);
        p1.UseConstValue = true; p1.ConstValue = 1;
        var p2 = new TestSampleProvider(44100, 1, 10);
        p2.UseConstValue = true; p2.ConstValue = 2;

        var concat = new ConcatenatingSampleProvider(new[] { p1, p2 });

        // Read 7 at a time across the boundary
        var buffer = new float[7];
        int r1 = concat.Read(buffer.AsSpan()); // reads 7 from p1
        Assert.Equal(7, r1);
        Assert.True(buffer.Take(7).All(v => v == 1f));

        int r2 = concat.Read(buffer.AsSpan()); // reads 3 from p1, 4 from p2
        Assert.Equal(7, r2);
        Assert.Equal(1f, buffer[0]); // last 3 from p1
        Assert.Equal(1f, buffer[1]);
        Assert.Equal(1f, buffer[2]);
        Assert.Equal(2f, buffer[3]); // first 4 from p2
        Assert.Equal(2f, buffer[6]);

        int r3 = concat.Read(buffer.AsSpan()); // reads remaining 6 from p2
        Assert.Equal(6, r3);
    }

    #endregion


    #region SampleChannel pipeline

    [Fact]
    public void SampleChannel_16BitMono_ProducesSamples()
    {
        // Create a 16-bit PCM wave provider with known data
        var format = new WaveFormat(44100, 16, 1);
        var pcmData = new byte[200]; // 100 16-bit samples
        // Write a known 16-bit sample value (16384 = 0.5 * 32768)
        for (int i = 0; i < pcmData.Length; i += 2)
        {
            short val = 16384;
            pcmData[i] = (byte)(val & 0xFF);
            pcmData[i + 1] = (byte)(val >> 8);
        }
        var waveProvider = new BufferedWaveProvider(format);
        waveProvider.ReadFully = false;
        waveProvider.AddSamples(pcmData, 0, pcmData.Length);

        var channel = new SampleChannel(waveProvider);

        var output = new float[100];
        int read = channel.Read(output.AsSpan());

        Assert.Equal(100, read);
        for (int i = 0; i < read; i++)
        {
            // 16384 / 32768 = 0.5
            Assert.Equal(0.5f, output[i], 0.001f);
        }
    }

    [Fact]
    public void SampleChannel_VolumeAffectsOutput()
    {
        var format = new WaveFormat(44100, 16, 1);
        var pcmData = new byte[200];
        for (int i = 0; i < pcmData.Length; i += 2)
        {
            short val = 16384; // 0.5
            pcmData[i] = (byte)(val & 0xFF);
            pcmData[i + 1] = (byte)(val >> 8);
        }
        var waveProvider = new BufferedWaveProvider(format);
        waveProvider.ReadFully = false;
        waveProvider.AddSamples(pcmData, 0, pcmData.Length);

        var channel = new SampleChannel(waveProvider);
        channel.Volume = 0.5f; // half volume

        var output = new float[100];
        int read = channel.Read(output.AsSpan());

        Assert.Equal(100, read);
        for (int i = 0; i < read; i++)
        {
            // 0.5 * 0.5 = 0.25
            Assert.Equal(0.25f, output[i], 0.001f);
        }
    }

    [Fact]
    public void SampleChannel_MonoForcedToStereo()
    {
        var format = new WaveFormat(44100, 16, 1);
        var pcmData = new byte[20]; // 10 mono samples
        for (int i = 0; i < pcmData.Length; i += 2)
        {
            short val = 16384;
            pcmData[i] = (byte)(val & 0xFF);
            pcmData[i + 1] = (byte)(val >> 8);
        }
        var waveProvider = new BufferedWaveProvider(format);
        waveProvider.ReadFully = false;
        waveProvider.AddSamples(pcmData, 0, pcmData.Length);

        var channel = new SampleChannel(waveProvider, forceStereo: true);

        Assert.Equal(2, channel.WaveFormat.Channels);

        var output = new float[20]; // 10 stereo pairs
        int read = channel.Read(output.AsSpan());

        Assert.Equal(20, read);
        for (int i = 0; i < read; i++)
        {
            Assert.Equal(0.5f, output[i], 0.001f);
        }
    }

    #endregion

    #region BufferedWaveProvider circular buffer wrap-around

    [Fact]
    public void BufferedWaveProvider_WrapAround_DataIntegrity()
    {
        // Use a small buffer that will wrap
        var format = new WaveFormat(44100, 16, 2);
        var bwp = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(10));
        bwp.ReadFully = false;
        int bufferSize = bwp.BufferLength;

        // Fill most of the buffer
        int firstWrite = bufferSize - 100;
        var data1 = Enumerable.Range(0, firstWrite).Select(n => (byte)(n % 256)).ToArray();
        bwp.AddSamples(data1, 0, data1.Length);

        // Read it all out (advances the read pointer near the end)
        var readBuf = new byte[firstWrite];
        int read1 = bwp.Read(readBuf.AsSpan());
        Assert.Equal(firstWrite, read1);
        Assert.Equal(data1, readBuf);

        // Now write data that wraps around the circular buffer
        int wrapWrite = 200; // crosses the end of the internal buffer
        var data2 = Enumerable.Range(0, wrapWrite).Select(n => (byte)((n + 50) % 256)).ToArray();
        bwp.AddSamples(data2, 0, data2.Length);

        // Read it back - this exercises the wrap-around read path
        readBuf = new byte[wrapWrite];
        int read2 = bwp.Read(readBuf.AsSpan());
        Assert.Equal(wrapWrite, read2);
        Assert.Equal(data2, readBuf);
    }

    #endregion

    #region Span edge cases

    [Fact]
    public void ZeroLengthSpanRead_ReturnsZero()
    {
        var provider = new TestSampleProvider(44100, 1, 100);
        var buffer = new float[0];
        Assert.Equal(0, provider.Read(buffer.AsSpan()));
    }

    [Fact]
    public void SampleToWaveProvider_ZeroLengthSpan()
    {
        var signal = new TestSampleProvider(44100, 1, 100);
        signal.UseConstValue = true; signal.ConstValue = 1;
        var stwp = new SampleToWaveProvider(signal);
        var buffer = new byte[0];
        Assert.Equal(0, stwp.Read(buffer.AsSpan()));
    }

    [Fact]
    public void PartialSpanSlice_ReadsCorrectSubset()
    {
        var signal = new TestSampleProvider(44100, 1, 100);
        signal.UseConstValue = true; signal.ConstValue = 42;

        // Allocate larger buffer, pass a slice
        var buffer = new float[100];
        int read = signal.Read(buffer.AsSpan(10, 20));
        Assert.Equal(20, read);
        // The slice should be filled
        Assert.Equal(42f, buffer[10]);
        Assert.Equal(42f, buffer[29]);
        // Outside the slice should be untouched
        Assert.Equal(0f, buffer[0]);
        Assert.Equal(0f, buffer[30]);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates an ISampleProvider that produces a known sine-like signal with values in [-1, 1]
    /// </summary>
    private static KnownSignalSampleProvider CreateKnownSignal(int sampleCount, int sampleRate, int channels)
    {
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            // Use a sine wave that stays well within [-1, 1] to avoid clipping
            samples[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.9f;
        }
        return new KnownSignalSampleProvider(sampleRate, channels, samples);
    }

    /// <summary>
    /// Sample provider backed by a known array of samples
    /// </summary>
    private class KnownSignalSampleProvider : ISampleProvider
    {
        public float[] Samples { get; }
        private int position;

        public KnownSignalSampleProvider(int sampleRate, int channels, float[] samples)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            Samples = samples;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            int toCopy = Math.Min(buffer.Length, Samples.Length - position);
            Samples.AsSpan(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return toCopy;
        }
    }

    /// <summary>
    /// Sample provider that repeats a per-channel constant pattern
    /// </summary>
    private class ConstChannelSampleProvider : ISampleProvider
    {
        private readonly float[] channelValues;
        private readonly int totalSamples;
        private int position;

        public ConstChannelSampleProvider(int sampleRate, int channels, int framesCount, float[] channelValues)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            this.channelValues = channelValues;
            this.totalSamples = framesCount * channels;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            int toCopy = Math.Min(buffer.Length, totalSamples - position);
            for (int i = 0; i < toCopy; i++)
            {
                buffer[i] = channelValues[(position + i) % channelValues.Length];
            }
            position += toCopy;
            return toCopy;
        }
    }

    #endregion
}

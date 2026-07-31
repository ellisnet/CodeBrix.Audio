using System;
using System.IO;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Verifies that every WAV sample format the library claims to read actually survives the whole
/// managed path - reader, float conversion, and the sample values themselves.
/// </summary>
/// <remarks>
/// The awkward cases here are 24- and 32-bit, because encoders write those as
/// WAVE_FORMAT_EXTENSIBLE rather than plain PCM. That is a header wrapper around ordinary PCM,
/// but code that switches on the encoding tag alone rejects it - which is exactly what used to
/// happen. Every file in these tests is built byte by byte so no binary assets are needed.
/// </remarks>
public class WavBitDepthTests
{
    private const int SampleRate = 44100;
    private const int Frames = 4410; // 0.1 s
    private const double Amplitude = 0.5;

    /// <summary>
    /// Builds a mono sine WAV. <paramref name="extensible"/> selects WAVE_FORMAT_EXTENSIBLE,
    /// which is how real encoders write 24- and 32-bit files.
    /// </summary>
    private static byte[] BuildWav(int bitsPerSample, bool ieeeFloat, bool extensible)
    {
        var bytesPerSample = bitsPerSample / 8;
        var dataLength = Frames * bytesPerSample;
        var fmtLength = extensible ? 40 : 16;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(4 + (8 + fmtLength) + (8 + dataLength));
        w.Write(new[] { 'W', 'A', 'V', 'E' });

        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(fmtLength);
        w.Write((short)(extensible ? 0xFFFE : ieeeFloat ? 3 : 1)); // 0xFFFE = EXTENSIBLE
        w.Write((short)1);                                          // channels
        w.Write(SampleRate);
        w.Write(SampleRate * bytesPerSample);                       // average bytes per second
        w.Write((short)bytesPerSample);                             // block align
        w.Write((short)bitsPerSample);

        if (extensible)
        {
            w.Write((short)22);                                     // extra size
            w.Write((short)bitsPerSample);                          // valid bits per sample
            w.Write(1);                                             // channel mask: front left
            w.Write(ieeeFloat
                ? new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray()   // IEEE float
                : new Guid("00000001-0000-0010-8000-00AA00389B71").ToByteArray()); // PCM
        }

        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataLength);

        for (var n = 0; n < Frames; n++)
        {
            var value = Amplitude * Math.Sin(2.0 * Math.PI * 440.0 * n / SampleRate);

            if (ieeeFloat)
            {
                if (bitsPerSample == 64) w.Write(value);
                else w.Write((float)value);
                continue;
            }

            switch (bitsPerSample)
            {
                case 8:
                    w.Write((byte)(value * 127 + 128)); // 8-bit PCM in WAV is unsigned
                    break;
                case 16:
                    w.Write((short)(value * short.MaxValue));
                    break;
                case 24:
                    var sample24 = (int)(value * 8388607);
                    w.Write((byte)sample24);
                    w.Write((byte)(sample24 >> 8));
                    w.Write((byte)(sample24 >> 16));
                    break;
                default:
                    w.Write((int)(value * int.MaxValue));
                    break;
            }
        }

        w.Flush();
        return ms.ToArray();
    }

    private static string WriteToTempFile(byte[] wav)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        File.WriteAllBytes(path, wav);
        return path;
    }

    [Theory]
    [InlineData(8, false, false)]    // 8-bit PCM
    [InlineData(16, false, false)]   // 16-bit PCM - the everyday case
    [InlineData(24, false, false)]   // 24-bit PCM, plain header
    [InlineData(32, false, false)]   // 32-bit PCM, plain header
    [InlineData(24, false, true)]    // 24-bit PCM, WAVE_FORMAT_EXTENSIBLE (what encoders write)
    [InlineData(32, false, true)]    // 32-bit PCM, WAVE_FORMAT_EXTENSIBLE
    [InlineData(32, true, false)]    // 32-bit IEEE float
    [InlineData(64, true, false)]    // 64-bit IEEE float
    [InlineData(32, true, true)]     // 32-bit IEEE float, WAVE_FORMAT_EXTENSIBLE
    public void Every_supported_wav_sample_format_reads_back_as_the_audio_it_encoded(
        int bitsPerSample, bool ieeeFloat, bool extensible)
    {
        //Arrange
        var path = WriteToTempFile(BuildWav(bitsPerSample, ieeeFloat, extensible));

        try
        {
            //Act
            using var reader = new AudioFileReader(path);
            var buffer = new float[8192];
            var total = 0;
            var peak = 0f;
            int read;
            while ((read = ((ISampleProvider)reader).Read(buffer)) > 0)
            {
                for (var i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
                total += read;
            }

            //Assert
            reader.WaveFormat.SampleRate.Should().Be(SampleRate);
            reader.WaveFormat.Channels.Should().Be(1);
            total.Should().Be(Frames);

            // The amplitude has to survive the conversion: a wrong scaling factor or a
            // misread container width shows up here as a peak nowhere near 0.5.
            peak.Should().BeApproximately((float)Amplitude, 0.02f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_extensible_wav_is_recognised_rather_than_rejected_for_its_encoding_tag()
    {
        //Arrange
        // Regression guard: this used to throw "Only PCM and IEEE-float WAV files are supported;
        // this file uses Extensible encoding", which locked out most real 24- and 32-bit files.
        var path = WriteToTempFile(BuildWav(24, ieeeFloat: false, extensible: true));

        try
        {
            //Act
            var open = () =>
            {
                using var reader = new AudioFileReader(path);
                return reader.WaveFormat.SampleRate;
            };

            //Assert
            open.Should().NotThrow();
        }
        finally
        {
            File.Delete(path);
        }
    }
}

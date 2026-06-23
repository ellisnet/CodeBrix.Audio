using System;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Shared helpers for generating self-contained test audio: synthesized WAV
/// files and a hand-built silent MP3 byte stream. Keeping generation in code
/// avoids bundling any third-party audio assets into the test project.
/// </summary>
internal static class TestAudio
{
    /// <summary>Default sample rate used by the generated test audio.</summary>
    public const int SampleRate = 44100;

    /// <summary>
    /// Writes a mono 16-bit PCM sine wave to <paramref name="path"/> and returns
    /// the float samples that were written (for round-trip comparison).
    /// </summary>
    public static float[] WriteSineWaveFile(string path, double frequency = 440.0,
        double seconds = 0.25, int sampleRate = SampleRate)
    {
        int sampleCount = (int)(seconds * sampleRate);
        var samples = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            samples[n] = (float)(0.5 * Math.Sin(2.0 * Math.PI * frequency * n / sampleRate));
        }

        using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
        return samples;
    }

    /// <summary>
    /// Builds a valid silent MPEG-1 Layer III stream (44.1 kHz, 128 kbps, mono).
    /// Each frame is a 4-byte header followed by zeroed side-info and main data,
    /// which a conformant decoder renders as silence.
    /// </summary>
    public static byte[] BuildSilentMp3(int frameCount = 25)
    {
        // MPEG-1 Layer III, 128 kbps, 44100 Hz, mono, no CRC.
        // FrameLength = 144 * 128000 / 44100 = 417 bytes (no padding).
        const int frameLength = 417;
        byte[] header = { 0xFF, 0xFB, 0x90, 0xC0 };
        using var ms = new MemoryStream();
        for (int i = 0; i < frameCount; i++)
        {
            ms.Write(header, 0, header.Length);
            ms.Write(new byte[frameLength - header.Length], 0, frameLength - header.Length);
        }
        return ms.ToArray();
    }
}

using System;
using System.IO;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// In-code audio fixtures so the test suite needs no binary assets (matching the
/// CodeBrix.Audio.Tests convention).
/// </summary>
internal static class TestAudio
{
    /// <summary>
    /// Builds a valid in-memory PCM-16 WAV (RIFF/WAVE) containing a sine wave of the
    /// given frequency, laid out by hand so no external file or codec is needed.
    /// </summary>
    public static byte[] BuildSineWavPcm16(int sampleRate, int channels, int frames, double frequency = 440.0)
    {
        const short bitsPerSample = 16;
        var blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var dataLen = frames * blockAlign;

        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms);

        Tag(w, "RIFF");
        w.Write(36 + dataLen);
        Tag(w, "WAVE");

        Tag(w, "fmt ");
        w.Write(16);                     // PCM fmt chunk size
        w.Write((short)1);               // audio format = PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write(bitsPerSample);

        Tag(w, "data");
        w.Write(dataLen);
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * t) * short.MaxValue * 0.5);
            for (var c = 0; c < channels; c++)
                w.Write(sample);
        }

        w.Flush();
        return ms.ToArray();
    }

    private static void Tag(BinaryWriter w, string fourCc)
    {
        foreach (var ch in fourCc)
            w.Write((byte)ch);
    }
}

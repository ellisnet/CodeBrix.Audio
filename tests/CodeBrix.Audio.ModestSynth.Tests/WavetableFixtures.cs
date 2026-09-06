using System;
using System.IO;
using System.Text;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The synthetic wavetable files the wavetable tests are written against, and the minimal RIFF
/// writer that produces them.
/// </summary>
/// <remarks>
/// <para>
/// The WAV writer here is deliberately hand-rolled rather than CodeBrix.Audio's
/// <c>WaveFileWriter</c>: these tests need to place a <c>clm&#160;</c> chunk between <c>fmt&#160;</c>
/// and <c>data</c>, which is exactly what the writer does not offer. Writing the twelve fields by
/// hand also means the test knows byte for byte what the loader is being asked to read.
/// </para>
/// <para>
/// Nothing here comes from any real wavetable library. The four frames are a sine, a triangle, a
/// sawtooth and a square, generated from their formulae, and they are deliberately UNRELATED so
/// that "which frame is playing" is answerable from the spectrum alone.
/// </para>
/// </remarks>
public static class WavetableFixtures
{
    /// <summary>The frame size the fixture tables use - the format's own default.</summary>
    public const int FrameSize = 2048;

    /// <summary>How many frames the standard fixture table holds.</summary>
    public const int FrameCount = 4;

    /// <summary>The frame index holding a sine.</summary>
    public const int SineFrame = 0;

    /// <summary>The frame index holding a triangle.</summary>
    public const int TriangleFrame = 1;

    /// <summary>The frame index holding a sawtooth.</summary>
    public const int SawFrame = 2;

    /// <summary>The frame index holding a square.</summary>
    public const int SquareFrame = 3;

    /// <summary>
    /// The four fixture frames - sine, triangle, saw, square - concatenated.
    /// </summary>
    /// <param name="frameSize">The samples per frame.</param>
    /// <returns>The samples.</returns>
    public static float[] BuildFourFrames(int frameSize = FrameSize)
    {
        float[] samples = new float[FrameCount * frameSize];

        for (int i = 0; i < frameSize; i++)
        {
            double phase = (double)i / frameSize;

            samples[(SineFrame * frameSize) + i] = (float)Math.Sin(2.0 * Math.PI * phase);
            samples[(TriangleFrame * frameSize) + i] = (float)(phase < 0.25
                ? 4.0 * phase
                : phase < 0.75 ? 2.0 - (4.0 * phase) : (4.0 * phase) - 4.0);
            samples[(SawFrame * frameSize) + i] = (float)((2.0 * phase) - 1.0);
            samples[(SquareFrame * frameSize) + i] = phase < 0.5 ? 1f : -1f;
        }

        return samples;
    }

    /// <summary>
    /// Writes a .wav file holding the given samples, optionally with a Serum-style
    /// <c>clm&#160;</c> chunk.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="samples">The mono samples, in [-1, 1].</param>
    /// <param name="sampleRate">The rate to declare.</param>
    /// <param name="bitsPerSample">8, 16, 24 or 32. 32 is written as IEEE float.</param>
    /// <param name="channels">How many channels to write; the samples are copied to each.</param>
    /// <param name="clmText">The clm chunk's text, or null for no chunk.</param>
    public static void WriteWav(string path, float[] samples, int sampleRate, int bitsPerSample,
        int channels, string clmText)
    {
        File.WriteAllBytes(path, BuildWav(samples, sampleRate, bitsPerSample, channels, clmText));
    }

    /// <summary>
    /// Writes a two-channel .wav file whose channels DIFFER, so a test can tell which one survives.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="left">The left channel's samples.</param>
    /// <param name="right">The right channel's samples, the same length as the left's.</param>
    /// <param name="sampleRate">The rate to declare.</param>
    public static void WriteStereoWav(string path, float[] left, float[] right, int sampleRate)
    {
        float[] interleaved = new float[left.Length * 2];

        for (int i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[(i * 2) + 1] = right[i];
        }

        // BuildWav copies each sample to every channel, so the interleaved pairs are written as one
        // mono stream and the header is patched to call it stereo.
        byte[] bytes = BuildWav(interleaved, sampleRate, 32, 1, null);
        BitConverter.TryWriteBytes(bytes.AsSpan(22, 2), (short)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(32, 2), (short)8);
        BitConverter.TryWriteBytes(bytes.AsSpan(28, 4), sampleRate * 8);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// The Serum-style chunk text that declares a frame size.
    /// </summary>
    /// <param name="frameSize">The frame size to declare.</param>
    /// <returns>The chunk text.</returns>
    public static string ClmText(int frameSize) => "<!>" + frameSize + " 00000000 wavetable (test)";

    /// <summary>
    /// Builds the bytes of a .wav file, optionally with a <c>clm&#160;</c> chunk between the
    /// format chunk and the data chunk.
    /// </summary>
    /// <param name="samples">The mono samples, in [-1, 1].</param>
    /// <param name="sampleRate">The rate to declare.</param>
    /// <param name="bitsPerSample">8, 16, 24 or 32. 32 is written as IEEE float.</param>
    /// <param name="channels">How many channels to write; the samples are copied to each.</param>
    /// <param name="clmText">The clm chunk's text, or null for no chunk.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] BuildWav(float[] samples, int sampleRate, int bitsPerSample, int channels,
        string clmText)
    {
        bool isFloat = bitsPerSample == 32;
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = bytesPerSample * channels;

        byte[] data = new byte[samples.Length * blockAlign];
        int offset = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                WriteSample(data, offset, samples[i], bitsPerSample, isFloat);
                offset += bytesPerSample;
            }
        }

        byte[] clm = clmText == null ? null : Encoding.ASCII.GetBytes(clmText);

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);                                    // patched below
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)(isFloat ? 3 : 1));
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write((short)blockAlign);
            writer.Write((short)bitsPerSample);

            if (clm != null)
            {
                writer.Write(Encoding.ASCII.GetBytes("clm "));
                writer.Write(clm.Length);
                writer.Write(clm);
                if ((clm.Length & 1) != 0) { writer.Write((byte)0); }
            }

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length);
            writer.Write(data);

            writer.Flush();
            byte[] bytes = stream.ToArray();
            BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), bytes.Length - 8);
            return bytes;
        }
    }

    private static void WriteSample(byte[] destination, int offset, float value, int bitsPerSample,
        bool isFloat)
    {
        if (isFloat)
        {
            BitConverter.TryWriteBytes(destination.AsSpan(offset, 4), value);
            return;
        }

        switch (bitsPerSample)
        {
            case 8:
                {
                    int quantised = (int)Math.Round((value * 127.0) + 128.0);
                    destination[offset] = (byte)Math.Clamp(quantised, 0, 255);
                    return;
                }

            case 16:
                {
                    int quantised = (int)Math.Round(value * 32767.0);
                    BitConverter.TryWriteBytes(destination.AsSpan(offset, 2),
                        (short)Math.Clamp(quantised, short.MinValue, short.MaxValue));
                    return;
                }

            case 24:
                {
                    int quantised = (int)Math.Round(value * 8388607.0);
                    quantised = Math.Clamp(quantised, -8388608, 8388607);
                    destination[offset] = (byte)(quantised & 0xFF);
                    destination[offset + 1] = (byte)((quantised >> 8) & 0xFF);
                    destination[offset + 2] = (byte)((quantised >> 16) & 0xFF);
                    return;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample,
                    "The fixture writer handles 8, 16 and 24-bit PCM and 32-bit float.");
        }
    }
}

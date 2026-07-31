using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One decoded sample file, held as planar 32-bit float channels, with any loop points the file itself
/// carried (a WAV <c>smpl</c> chunk).
/// </summary>
/// <remarks>
/// SFZ regions reference external sample files (WAV, FLAC, sometimes Ogg), decoded here through the
/// same readers the rest of the library uses. The embedded loop points matter because the SFZ default
/// for <c>loop_mode</c> depends on them: a sample with a loop defaults to <c>loop_continuous</c>, one
/// without to <c>no_loop</c>, and unset <c>loop_start</c>/<c>loop_end</c> opcodes fall back to them.
/// </remarks>
internal sealed class SfzSampleData
{
    private SfzSampleData(float[][] channels, int sampleRate, long frames, long? loopStart, long? loopEnd)
    {
        Channels = channels;
        SampleRate = sampleRate;
        Frames = frames;
        EmbeddedLoopStart = loopStart;
        EmbeddedLoopEnd = loopEnd;
    }

    /// <summary>The decoded audio, one array per channel, all the same length.</summary>
    public float[][] Channels { get; }

    /// <summary>The number of channels (1 for mono, 2 for stereo; more are folded to stereo).</summary>
    public int ChannelCount => Channels.Length;

    /// <summary>The sample rate the file was recorded at.</summary>
    public int SampleRate { get; }

    /// <summary>The number of sample frames per channel.</summary>
    public long Frames { get; }

    /// <summary>The first frame of the file's own loop, when the file defines one.</summary>
    public long? EmbeddedLoopStart { get; }

    /// <summary>The last frame (inclusive) of the file's own loop, when the file defines one.</summary>
    public long? EmbeddedLoopEnd { get; }

    /// <summary>Whether the file carries usable loop points.</summary>
    public bool HasEmbeddedLoop => EmbeddedLoopStart.HasValue && EmbeddedLoopEnd.HasValue;

    /// <summary>
    /// Decodes a sample file into planar float channels.
    /// </summary>
    /// <param name="path">The file to decode. Any format the reader registry can open.</param>
    /// <returns>The decoded sample.</returns>
    public static SfzSampleData Load(string path)
    {
        using (var reader = AudioFileReaderRegistry.OpenFile(path))
        {
            long? loopStart = null;
            long? loopEnd = null;

            if (reader is WaveFileReader waveReader)
            {
                ReadSmplLoop(waveReader, ref loopStart, ref loopEnd);
            }

            var format = reader.WaveFormat;
            var sourceChannels = Math.Max(1, format.Channels);

            var provider = reader.ToSampleProvider();

            var interleaved = new float[8192 * sourceChannels];
            var blocks = new List<float[]>();
            var totalSamples = 0L;

            int read;
            while ((read = provider.Read(interleaved)) > 0)
            {
                var block = new float[read];
                Array.Copy(interleaved, block, read);
                blocks.Add(block);
                totalSamples += read;
            }

            var frames = totalSamples / sourceChannels;

            // Fold anything beyond stereo down to stereo; regions have no use for surround stems.
            var targetChannels = Math.Min(sourceChannels, 2);
            var channels = new float[targetChannels][];
            for (var c = 0; c < targetChannels; c++)
            {
                channels[c] = new float[frames];
            }

            var frameIndex = 0L;
            var carry = 0;
            var carrySamples = new float[sourceChannels];

            foreach (var block in blocks)
            {
                var offset = 0;

                // A block boundary can split a frame; stitch the partial frame back together.
                if (carry > 0)
                {
                    var needed = sourceChannels - carry;
                    var available = Math.Min(needed, block.Length);
                    Array.Copy(block, 0, carrySamples, carry, available);
                    carry += available;
                    offset = available;

                    if (carry == sourceChannels)
                    {
                        WriteFrame(channels, carrySamples, sourceChannels, targetChannels, frameIndex);
                        frameIndex++;
                        carry = 0;
                    }
                }

                var wholeFrames = (block.Length - offset) / sourceChannels;
                for (var f = 0; f < wholeFrames; f++)
                {
                    var basePosition = offset + f * sourceChannels;
                    for (var c = 0; c < targetChannels; c++)
                    {
                        channels[c][frameIndex] = block[basePosition + c];
                    }
                    frameIndex++;
                }

                var used = offset + wholeFrames * sourceChannels;
                var remainder = block.Length - used;
                if (remainder > 0)
                {
                    Array.Copy(block, used, carrySamples, 0, remainder);
                    carry = remainder;
                }
            }

            if (loopEnd.HasValue && loopEnd.Value >= frames)
            {
                loopEnd = frames - 1;
            }
            if (loopStart.HasValue && (loopStart.Value < 0 || (loopEnd.HasValue && loopStart.Value > loopEnd.Value)))
            {
                loopStart = null;
                loopEnd = null;
            }

            return new SfzSampleData(channels, format.SampleRate, frames, loopStart, loopEnd);
        }
    }

    private static void WriteFrame(float[][] channels, float[] frame, int sourceChannels, int targetChannels, long frameIndex)
    {
        for (var c = 0; c < targetChannels && c < sourceChannels; c++)
        {
            channels[c][frameIndex] = frame[c];
        }
    }

    // The smpl chunk: 36 bytes of header, then 24-byte loop records. dwStart/dwEnd are sample frames,
    // end inclusive - the same convention as SFZ loop_end.
    private static void ReadSmplLoop(WaveFileReader reader, ref long? loopStart, ref long? loopEnd)
    {
        var chunk = reader.Chunks.Find("smpl");
        if (chunk == null || chunk.Length < 36 + 24)
        {
            return;
        }

        byte[] data;
        try
        {
            data = reader.Chunks.GetData(chunk);
        }
        catch (Exception)
        {
            // A truncated or unreadable smpl chunk is not fatal; the sample simply has no loop.
            return;
        }

        var loopCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28, 4));
        if (loopCount == 0)
        {
            return;
        }

        var start = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(36 + 8, 4));
        var end = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(36 + 12, 4));
        if (end <= start)
        {
            return;
        }

        loopStart = start;
        loopEnd = end;
    }
}

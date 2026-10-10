using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Flac;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>
/// Takes a .flac file apart the way a demultiplexer would hand it over: the stream header as
/// codec-private data, then one packet per audio frame. Also decodes a file through
/// <see cref="FlacFileReader"/> into the float samples the packet path must reproduce.
/// </summary>
/// <remarks>
/// The frame boundaries come from the library's own <see cref="FlacFrameDecoder"/> - each frame is
/// decoded from the file in turn and the reader's byte position before and after it marks where it
/// starts and ends - so the split is exactly what the frame parser considers one frame.
/// </remarks>
internal static class FlacFramePacketizer
{
    /// <summary>A .flac file split into its stream header and its audio frames.</summary>
    internal sealed class SplitFlac
    {
        /// <summary>The "fLaC" marker and every metadata block - Matroska's A_FLAC codec-private data.</summary>
        public byte[] CodecPrivate { get; init; }

        /// <summary>The 34-byte STREAMINFO body on its own.</summary>
        public byte[] StreamInfoBody { get; init; }

        /// <summary>One entry per audio frame, each exactly the frame's bytes.</summary>
        public List<byte[]> Frames { get; init; }

        /// <summary>The number of samples per channel in each frame, parallel to <see cref="Frames"/>.</summary>
        public List<int> BlockSizes { get; init; }

        /// <summary>The stream's channel count.</summary>
        public int Channels { get; init; }
    }

    /// <summary>Splits the .flac file at <paramref name="path"/> into codec-private data and frames.</summary>
    public static SplitFlac Split(string path)
    {
        var bytes = File.ReadAllBytes(path);

        long firstFrameOffset;
        FlacStreamInfo info;
        using (var probe = new FlacDecoder(new MemoryStream(bytes)))
        {
            firstFrameOffset = probe.FirstFrameOffset;
            info = probe.StreamInfo;
        }

        var codecPrivate = new byte[firstFrameOffset];
        Array.Copy(bytes, codecPrivate, codecPrivate.Length);

        // STREAMINFO is always the first metadata block: marker (4) + block header (4).
        var streamInfoBody = new byte[FlacStreamInfo.BodyLength];
        Array.Copy(bytes, 8, streamInfoBody, 0, streamInfoBody.Length);

        var stream = new MemoryStream(bytes) { Position = firstFrameOffset };
        var reader = new FlacBitReader(stream);
        var frameDecoder = new FlacFrameDecoder(info, reader, allowOversizedBlocks: true);

        var frames = new List<byte[]>();
        var blockSizes = new List<int>();
        var start = reader.BytePosition;

        while (frameDecoder.DecodeFrame())
        {
            var end = reader.BytePosition;
            var frame = new byte[end - start];
            Array.Copy(bytes, start, frame, 0, frame.Length);
            frames.Add(frame);
            blockSizes.Add(frameDecoder.BlockSize);
            start = end;
        }

        return new SplitFlac
        {
            CodecPrivate = codecPrivate,
            StreamInfoBody = streamInfoBody,
            Frames = frames,
            BlockSizes = blockSizes,
            Channels = info.Channels
        };
    }

    /// <summary>
    /// Decodes the whole file through <see cref="FlacFileReader"/> and converts its PCM to float on
    /// the engine's full-scale convention (a sample divided by 2 to the power of bit depth minus one).
    /// </summary>
    public static float[] DecodeWithFileReader(string path)
    {
        using var reader = new FlacFileReader(path);
        var sourceBits = reader.SourceBitsPerSample;
        var containerBytes = reader.WaveFormat.BitsPerSample / 8;
        var shift = reader.WaveFormat.BitsPerSample - sourceBits;
        var scale = 1f / (1L << (sourceBits - 1));

        using var pcm = new MemoryStream();
        var buffer = new byte[8192 * containerBytes];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) pcm.Write(buffer, 0, read);

        var data = pcm.ToArray();
        var samples = new float[data.Length / containerBytes];
        for (var i = 0; i < samples.Length; i++)
        {
            var offset = i * containerBytes;
            int value = containerBytes switch
            {
                2 => (short)(data[offset] | (data[offset + 1] << 8)),
                3 => ((data[offset] << 8) | (data[offset + 1] << 16) | (data[offset + 2] << 24)) >> 8,
                _ => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24)
            };
            samples[i] = (value >> shift) * scale;
        }

        return samples;
    }
}

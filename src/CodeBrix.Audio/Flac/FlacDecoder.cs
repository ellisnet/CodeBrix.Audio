using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.Audio.Flac;

/// <summary>
/// A fully managed FLAC decoder, written from the FLAC format specification.
/// </summary>
/// <remarks>
/// <para>
/// Consumers do not use this directly - <see cref="CodeBrix.Audio.Wave.FlacFileReader"/> is the
/// public face of it, exactly as the NLayer-derived MPEG decoder sits behind
/// <c>Mp3FileReader</c>. It decodes the whole format: constant, verbatim, fixed-predictor and
/// LPC subframes, Rice-coded residuals including escaped partitions, all four stereo
/// decorrelation modes, and wasted bits.
/// </para>
/// <para>
/// This class owns the FILE-level work - the "fLaC" marker, the metadata blocks, buffering and
/// seeking. Each audio frame is decoded by <see cref="FlacFrameDecoder"/>, the same frame decoder
/// the packet seam uses, so there is one FLAC implementation behind both.
/// </para>
/// <para>
/// Because FLAC is lossless, correctness here is not a matter of judgement: the decoder either
/// reproduces the original PCM exactly or it is wrong, and the test suite compares its output
/// sample for sample against the audio each fixture was encoded from.
/// </para>
/// </remarks>
internal sealed class FlacDecoder : IDisposable
{
    private readonly Stream stream;
    private readonly bool ownsStream;
    private readonly FlacBitReader reader;
    private readonly long firstFrameOffset;
    private readonly List<FlacSeekPoint> seekPoints = [];
    private readonly FlacFrameDecoder frameDecoder;

    private int bufferedFrames;
    private int bufferedOffset;
    private bool endOfStream;

    /// <summary>
    /// Opens a FLAC stream and reads its metadata.
    /// </summary>
    /// <param name="input">A readable stream positioned at the start of a FLAC file.</param>
    /// <param name="ownsStream">True to dispose <paramref name="input"/> with this decoder.</param>
    /// <exception cref="InvalidDataException">The stream is not FLAC, or its metadata is unusable.</exception>
    public FlacDecoder(Stream input, bool ownsStream = false)
    {
        stream = input ?? throw new ArgumentNullException(nameof(input));
        this.ownsStream = ownsStream;
        reader = new FlacBitReader(stream);

        StreamInfo = ReadMetadata(out var tags);
        Tags = tags;

        frameDecoder = new FlacFrameDecoder(StreamInfo, reader, allowOversizedBlocks: true);
        firstFrameOffset = reader.BytePosition;
    }

    /// <summary>The stream's STREAMINFO block.</summary>
    public FlacStreamInfo StreamInfo { get; }

    /// <summary>
    /// The byte offset of the first audio frame: everything before it is the "fLaC" marker and the
    /// metadata blocks.
    /// </summary>
    public long FirstFrameOffset => firstFrameOffset;

    /// <summary>Vorbis comments carried by the stream, keyed by uppercase field name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tags { get; }

    /// <summary>Current position, in frames (samples per channel).</summary>
    public long SamplePosition { get; private set; }

    /// <summary>
    /// Reads decoded, interleaved samples. Values are right-aligned signed integers at the
    /// stream's own bit depth.
    /// </summary>
    /// <param name="destination">Buffer to fill; its length should be a multiple of the channel count.</param>
    /// <returns>The number of samples written; 0 at the end of the stream.</returns>
    public int ReadSamples(Span<int> destination)
    {
        var channels = StreamInfo.Channels;
        var framesWanted = destination.Length / channels;
        var framesWritten = 0;

        while (framesWritten < framesWanted)
        {
            if (bufferedOffset >= bufferedFrames)
            {
                if (endOfStream || !DecodeNextFrame()) break;
            }

            var channelBuffers = frameDecoder.ChannelBuffers;
            var take = Math.Min(framesWanted - framesWritten, bufferedFrames - bufferedOffset);
            for (var frame = 0; frame < take; frame++)
            {
                var source = bufferedOffset + frame;
                var target = (framesWritten + frame) * channels;
                for (var channel = 0; channel < channels; channel++)
                    destination[target + channel] = channelBuffers[channel][source];
            }

            bufferedOffset += take;
            framesWritten += take;
            SamplePosition += take;
        }

        return framesWritten * channels;
    }

    /// <summary>
    /// Seeks to a frame index (sample number).
    /// </summary>
    /// <remarks>
    /// A SEEKTABLE, when the encoder wrote one, gives the nearest frame to jump to; from there -
    /// or from the start of the audio when there is no table - the decoder walks forward frame by
    /// frame. Walking forward is what makes the landing point exact: a FLAC frame header states
    /// the sample number it begins at, so there is no estimation involved.
    /// </remarks>
    /// <param name="frameIndex">The frame (sample per channel) to position at.</param>
    public void SeekTo(long frameIndex)
    {
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (!stream.CanSeek) throw new NotSupportedException("The underlying stream does not support seeking.");

        var startOffset = firstFrameOffset;
        long startSample = 0;

        foreach (var point in seekPoints)
        {
            if (point.IsPlaceholder || point.SampleNumber > frameIndex) continue;
            if (point.SampleNumber < startSample) continue;

            startSample = point.SampleNumber;
            startOffset = firstFrameOffset + point.ByteOffset;
        }

        stream.Position = startOffset;
        reader.Reset();
        bufferedFrames = 0;
        bufferedOffset = 0;
        endOfStream = false;
        SamplePosition = startSample;

        // Walk forward to the requested frame, discarding whole frames where possible.
        while (SamplePosition < frameIndex)
        {
            if (bufferedOffset >= bufferedFrames)
            {
                if (!DecodeNextFrame()) return;
            }

            var skip = (int)Math.Min(frameIndex - SamplePosition, bufferedFrames - bufferedOffset);
            bufferedOffset += skip;
            SamplePosition += skip;
        }
    }

    /// <summary>Releases the decoder and, when it owns it, the underlying stream.</summary>
    public void Dispose()
    {
        if (ownsStream) stream.Dispose();
    }

    // ---------------------------------------------------------------------------------------
    // Metadata
    // ---------------------------------------------------------------------------------------

    private FlacStreamInfo ReadMetadata(out IReadOnlyDictionary<string, IReadOnlyList<string>> tags)
    {
        Span<byte> magic = stackalloc byte[4];
        reader.ReadBytes(magic);
        if (magic[0] != (byte)'f' || magic[1] != (byte)'L' || magic[2] != (byte)'a' || magic[3] != (byte)'C')
            throw new InvalidDataException("This stream is not FLAC: the 'fLaC' marker is missing.");

        FlacStreamInfo info = null;
        var collectedTags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var isLast = reader.ReadBits(1) != 0;
            var blockType = (int)reader.ReadBits(7);
            var blockLength = (int)reader.ReadBits(24);

            switch (blockType)
            {
                case 0: // STREAMINFO
                    info = FlacStreamInfo.Read(reader);
                    break;
                case 3: // SEEKTABLE
                    ReadSeekTable(blockLength);
                    break;
                case 4: // VORBIS_COMMENT
                    ReadVorbisComment(blockLength, collectedTags);
                    break;
                default:
                    // PADDING, APPLICATION, CUESHEET, PICTURE and anything reserved: not needed
                    // to decode audio, so step over them.
                    reader.SkipBytes(blockLength);
                    break;
            }

            if (isLast) break;
        }

        if (info == null)
            throw new InvalidDataException("The FLAC stream has no STREAMINFO block.");
        info.Validate();

        tags = collectedTags;
        return info;
    }

    private void ReadSeekTable(int blockLength)
    {
        var count = blockLength / 18;
        for (var i = 0; i < count; i++)
        {
            var sampleNumber = ((long)reader.ReadBits(32) << 32) | reader.ReadBits(32);
            var byteOffset = ((long)reader.ReadBits(32) << 32) | reader.ReadBits(32);
            var frameSamples = (int)reader.ReadBits(16);
            seekPoints.Add(new FlacSeekPoint(sampleNumber, byteOffset, frameSamples));
        }

        reader.SkipBytes(blockLength - count * 18);
    }

    private void ReadVorbisComment(int blockLength, Dictionary<string, IReadOnlyList<string>> tags)
    {
        var block = new byte[blockLength];
        reader.ReadBytes(block);

        try
        {
            var offset = 0;
            var vendorLength = (int)ReadLittleEndianUInt32(block, ref offset);
            offset += vendorLength;

            var commentCount = (int)ReadLittleEndianUInt32(block, ref offset);
            for (var i = 0; i < commentCount; i++)
            {
                var length = (int)ReadLittleEndianUInt32(block, ref offset);
                var comment = Encoding.UTF8.GetString(block, offset, length);
                offset += length;

                var separator = comment.IndexOf('=');
                if (separator <= 0) continue;

                var key = comment.Substring(0, separator).ToUpperInvariant();
                var value = comment.Substring(separator + 1);

                if (tags.TryGetValue(key, out var existing))
                {
                    var list = (List<string>)existing;
                    list.Add(value);
                }
                else
                {
                    tags[key] = new List<string> { value };
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or ArgumentException)
        {
            // Tags are optional decoration; a malformed comment block must not stop playback.
            tags.Clear();
        }
    }

    private static uint ReadLittleEndianUInt32(byte[] data, ref int offset)
    {
        var value = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        offset += 4;
        return value;
    }

    // ---------------------------------------------------------------------------------------
    // Frames
    // ---------------------------------------------------------------------------------------

    private bool DecodeNextFrame()
    {
        if (endOfStream) return false;

        if (!frameDecoder.DecodeFrame())
        {
            endOfStream = true;
            return false;
        }

        var blockSize = frameDecoder.BlockSize;
        bufferedFrames = blockSize;
        bufferedOffset = 0;

        // A frame states where it begins, so this is also how a seek confirms it landed.
        SamplePosition = frameDecoder.IsVariableBlockSize
            ? frameDecoder.FrameOrSampleNumber
            : frameDecoder.FrameOrSampleNumber * (StreamInfo.HasFixedBlockSize ? StreamInfo.MaximumBlockSize : blockSize);

        return true;
    }
}

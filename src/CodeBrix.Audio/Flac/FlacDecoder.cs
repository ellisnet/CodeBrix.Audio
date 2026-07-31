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
/// Because FLAC is lossless, correctness here is not a matter of judgement: the decoder either
/// reproduces the original PCM exactly or it is wrong, and the test suite compares its output
/// sample for sample against the audio each fixture was encoded from.
/// </para>
/// </remarks>
internal sealed class FlacDecoder : IDisposable
{
    private const uint FrameSyncCode = 0x3FFE; // 14 bits: 11111111111110

    private static readonly int[] BlockSizeTable =
        [0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768];

    private static readonly int[] SampleRateTable =
        [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000, 0, 0, 0, -1];

    private static readonly int[] BitsPerSampleTable = [0, 8, 12, -1, 16, 20, 24, 32];

    private readonly Stream stream;
    private readonly bool ownsStream;
    private readonly FlacBitReader reader;
    private readonly long firstFrameOffset;
    private readonly List<FlacSeekPoint> seekPoints = [];

    private int[][] channelBuffers;
    private int[] residualBuffer;
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

        AllocateBuffers();
        firstFrameOffset = reader.BytePosition;
    }

    /// <summary>The stream's STREAMINFO block.</summary>
    public FlacStreamInfo StreamInfo { get; }

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
                    info = ReadStreamInfo();
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
        if (info.Channels is < 1 or > 8)
            throw new InvalidDataException($"Unsupported FLAC channel count: {info.Channels}.");
        if (info.BitsPerSample is < 4 or > 32)
            throw new InvalidDataException($"Unsupported FLAC bit depth: {info.BitsPerSample}.");
        if (info.MaximumBlockSize <= 0)
            throw new InvalidDataException("The FLAC stream declares an invalid maximum block size.");

        tags = collectedTags;
        return info;
    }

    private FlacStreamInfo ReadStreamInfo()
    {
        var minBlockSize = (int)reader.ReadBits(16);
        var maxBlockSize = (int)reader.ReadBits(16);
        var minFrameSize = (int)reader.ReadBits(24);
        var maxFrameSize = (int)reader.ReadBits(24);
        var sampleRate = (int)reader.ReadBits(20);
        var channels = (int)reader.ReadBits(3) + 1;
        var bitsPerSample = (int)reader.ReadBits(5) + 1;
        var totalSamples = ((long)reader.ReadBits(4) << 32) | reader.ReadBits(32);

        var md5 = new byte[16];
        reader.ReadBytes(md5);

        return new FlacStreamInfo
        {
            MinimumBlockSize = minBlockSize,
            MaximumBlockSize = maxBlockSize,
            MinimumFrameSize = minFrameSize,
            MaximumFrameSize = maxFrameSize,
            SampleRate = sampleRate,
            Channels = channels,
            BitsPerSample = bitsPerSample,
            TotalSamples = totalSamples,
            Md5Signature = md5
        };
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

    private void AllocateBuffers()
    {
        channelBuffers = new int[StreamInfo.Channels][];
        for (var i = 0; i < channelBuffers.Length; i++)
            channelBuffers[i] = new int[StreamInfo.MaximumBlockSize];

        residualBuffer = new int[StreamInfo.MaximumBlockSize];
    }

    // ---------------------------------------------------------------------------------------
    // Frames
    // ---------------------------------------------------------------------------------------

    private bool DecodeNextFrame()
    {
        if (endOfStream) return false;

        reader.ResetCrc();

        if (!FindFrameSync())
        {
            endOfStream = true;
            return false;
        }

        var blockingStrategyIsVariable = reader.ReadBits(1) != 0;
        var blockSizeCode = (int)reader.ReadBits(4);
        var sampleRateCode = (int)reader.ReadBits(4);
        var channelAssignment = (int)reader.ReadBits(4);
        var sampleSizeCode = (int)reader.ReadBits(3);

        if (reader.ReadBits(1) != 0)
            throw new InvalidDataException("Reserved bit set in a FLAC frame header; the stream is corrupt.");

        var frameOrSampleNumber = (long)reader.ReadUtf8Number();

        var blockSize = BlockSizeTable[blockSizeCode];
        if (blockSizeCode == 6) blockSize = (int)reader.ReadBits(8) + 1;
        else if (blockSizeCode == 7) blockSize = (int)reader.ReadBits(16) + 1;
        if (blockSize <= 0) throw new InvalidDataException("Invalid block size in a FLAC frame header.");

        if (sampleRateCode == 12) reader.ReadBits(8);
        else if (sampleRateCode is 13 or 14) reader.ReadBits(16);
        else if (SampleRateTable[sampleRateCode] == -1)
            throw new InvalidDataException("Invalid sample rate code in a FLAC frame header.");

        var frameBitsPerSample = sampleSizeCode == 0
            ? StreamInfo.BitsPerSample
            : BitsPerSampleTable[sampleSizeCode];
        if (frameBitsPerSample <= 0)
            throw new InvalidDataException("Invalid sample size code in a FLAC frame header.");

        // The CRC-8 covers the header up to but excluding itself.
        var computedHeaderCrc = reader.Crc8;
        var storedHeaderCrc = reader.ReadByteAligned();
        if (computedHeaderCrc != storedHeaderCrc)
            throw new InvalidDataException("FLAC frame header CRC mismatch; the stream is corrupt.");

        if (blockSize > StreamInfo.MaximumBlockSize) GrowBuffers(blockSize);

        DecodeSubframes(channelAssignment, blockSize, frameBitsPerSample);

        reader.AlignToByte();
        var computedFrameCrc = reader.Crc16;
        var storedFrameCrc = (ushort)reader.ReadBits(16);
        if (computedFrameCrc != storedFrameCrc)
            throw new InvalidDataException("FLAC frame CRC mismatch; the stream is corrupt.");

        bufferedFrames = blockSize;
        bufferedOffset = 0;

        // A frame states where it begins, so this is also how a seek confirms it landed.
        SamplePosition = blockingStrategyIsVariable
            ? frameOrSampleNumber
            : frameOrSampleNumber * (StreamInfo.HasFixedBlockSize ? StreamInfo.MaximumBlockSize : blockSize);

        return true;
    }

    /// <summary>
    /// Finds the next frame sync code, tolerating the end of the stream.
    /// </summary>
    private bool FindFrameSync()
    {
        reader.AlignToByte();

        try
        {
            // The first 14 bits of a frame are the sync code; the 15th is reserved and zero.
            var value = reader.ReadBits(15);
            if ((value >> 1) == FrameSyncCode && (value & 1) == 0) return true;

            throw new InvalidDataException("Lost frame synchronisation in the FLAC stream.");
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private void GrowBuffers(int blockSize)
    {
        for (var i = 0; i < channelBuffers.Length; i++) channelBuffers[i] = new int[blockSize];
        residualBuffer = new int[blockSize];
    }

    private void DecodeSubframes(int channelAssignment, int blockSize, int frameBitsPerSample)
    {
        var channels = StreamInfo.Channels;

        if (channelAssignment < 8)
        {
            if (channelAssignment + 1 != channels)
                throw new InvalidDataException("A FLAC frame declares a different channel count from STREAMINFO.");

            for (var channel = 0; channel < channels; channel++)
                DecodeSubframe(channelBuffers[channel], blockSize, frameBitsPerSample);

            return;
        }

        if (channels != 2)
            throw new InvalidDataException("Stereo decorrelation used on a stream that is not stereo.");

        // In the joint-stereo modes one of the two channels is a difference signal, which needs
        // one extra bit of headroom.
        switch (channelAssignment)
        {
            case 8: // left / side
                DecodeSubframe(channelBuffers[0], blockSize, frameBitsPerSample);
                DecodeSubframe(channelBuffers[1], blockSize, frameBitsPerSample + 1);
                for (var i = 0; i < blockSize; i++)
                    channelBuffers[1][i] = channelBuffers[0][i] - channelBuffers[1][i];
                break;

            case 9: // side / right
                DecodeSubframe(channelBuffers[0], blockSize, frameBitsPerSample + 1);
                DecodeSubframe(channelBuffers[1], blockSize, frameBitsPerSample);
                for (var i = 0; i < blockSize; i++)
                    channelBuffers[0][i] += channelBuffers[1][i];
                break;

            case 10: // mid / side
                DecodeSubframe(channelBuffers[0], blockSize, frameBitsPerSample);
                DecodeSubframe(channelBuffers[1], blockSize, frameBitsPerSample + 1);
                for (var i = 0; i < blockSize; i++)
                {
                    var side = channelBuffers[1][i];
                    // The encoder dropped the low bit of the mid channel; the side channel's
                    // parity is what puts it back.
                    var mid = (channelBuffers[0][i] << 1) | (side & 1);
                    channelBuffers[0][i] = (mid + side) >> 1;
                    channelBuffers[1][i] = (mid - side) >> 1;
                }

                break;

            default:
                throw new InvalidDataException($"Reserved FLAC channel assignment {channelAssignment}.");
        }
    }

    private void DecodeSubframe(int[] output, int blockSize, int bitsPerSample)
    {
        if (reader.ReadBits(1) != 0)
            throw new InvalidDataException("Reserved bit set in a FLAC subframe header.");

        var subframeType = (int)reader.ReadBits(6);
        var wastedBits = 0;

        if (reader.ReadBits(1) != 0)
        {
            // Unary-coded count minus one: encoders use this when every sample in the subframe
            // has the same low zero bits, typically after upsampling or volume scaling.
            wastedBits = reader.ReadUnary() + 1;
            bitsPerSample -= wastedBits;
            if (bitsPerSample <= 0)
                throw new InvalidDataException("A FLAC subframe declares more wasted bits than it has.");
        }

        if (subframeType == 0)
        {
            DecodeConstantSubframe(output, blockSize, bitsPerSample);
        }
        else if (subframeType == 1)
        {
            DecodeVerbatimSubframe(output, blockSize, bitsPerSample);
        }
        else if (subframeType is >= 8 and <= 12)
        {
            DecodeFixedSubframe(output, blockSize, bitsPerSample, subframeType & 0x07);
        }
        else if (subframeType >= 32)
        {
            DecodeLpcSubframe(output, blockSize, bitsPerSample, (subframeType & 0x1F) + 1);
        }
        else
        {
            throw new InvalidDataException($"Reserved FLAC subframe type {subframeType}.");
        }

        if (wastedBits > 0)
        {
            for (var i = 0; i < blockSize; i++) output[i] <<= wastedBits;
        }
    }

    private void DecodeConstantSubframe(int[] output, int blockSize, int bitsPerSample)
    {
        var value = reader.ReadSignedBits(bitsPerSample);
        for (var i = 0; i < blockSize; i++) output[i] = value;
    }

    private void DecodeVerbatimSubframe(int[] output, int blockSize, int bitsPerSample)
    {
        for (var i = 0; i < blockSize; i++) output[i] = reader.ReadSignedBits(bitsPerSample);
    }

    private void DecodeFixedSubframe(int[] output, int blockSize, int bitsPerSample, int order)
    {
        for (var i = 0; i < order; i++) output[i] = reader.ReadSignedBits(bitsPerSample);

        DecodeResidual(blockSize, order);

        for (var i = order; i < blockSize; i++)
        {
            var residual = residualBuffer[i];
            output[i] = order switch
            {
                0 => residual,
                1 => residual + output[i - 1],
                2 => residual + 2 * output[i - 1] - output[i - 2],
                3 => residual + 3 * output[i - 1] - 3 * output[i - 2] + output[i - 3],
                4 => residual + 4 * output[i - 1] - 6 * output[i - 2] + 4 * output[i - 3] - output[i - 4],
                _ => throw new InvalidDataException($"Invalid fixed predictor order {order}.")
            };
        }
    }

    private void DecodeLpcSubframe(int[] output, int blockSize, int bitsPerSample, int order)
    {
        for (var i = 0; i < order; i++) output[i] = reader.ReadSignedBits(bitsPerSample);

        var precision = (int)reader.ReadBits(4) + 1;
        if (precision == 16)
            throw new InvalidDataException("Invalid LPC coefficient precision in a FLAC subframe.");

        var shift = reader.ReadSignedBits(5);
        if (shift < 0)
            throw new InvalidDataException("Negative LPC shift in a FLAC subframe.");

        Span<int> coefficients = order <= 32 ? stackalloc int[order] : new int[order];
        for (var i = 0; i < order; i++) coefficients[i] = reader.ReadSignedBits(precision);

        DecodeResidual(blockSize, order);

        // 64-bit accumulation: with 32-bit samples and 32 coefficients the sum overflows int.
        for (var i = order; i < blockSize; i++)
        {
            long sum = 0;
            for (var j = 0; j < order; j++) sum += (long)coefficients[j] * output[i - 1 - j];
            output[i] = residualBuffer[i] + (int)(sum >> shift);
        }
    }

    private void DecodeResidual(int blockSize, int predictorOrder)
    {
        var method = (int)reader.ReadBits(2);
        if (method > 1)
            throw new InvalidDataException($"Reserved FLAC residual coding method {method}.");

        var parameterBits = method == 0 ? 4 : 5;
        var escapeParameter = method == 0 ? 0x0F : 0x1F;

        var partitionOrder = (int)reader.ReadBits(4);
        var partitionCount = 1 << partitionOrder;

        if (blockSize % partitionCount != 0)
            throw new InvalidDataException("FLAC partition order does not divide the block size.");
        if (blockSize / partitionCount < predictorOrder)
            throw new InvalidDataException("FLAC partition is smaller than the predictor order.");

        var index = predictorOrder;

        for (var partition = 0; partition < partitionCount; partition++)
        {
            var samples = blockSize / partitionCount - (partition == 0 ? predictorOrder : 0);
            var parameter = (int)reader.ReadBits(parameterBits);

            if (parameter == escapeParameter)
            {
                // Escaped partition: the residuals did not compress, so they are stored raw at a
                // fixed width (which may be zero, meaning every residual is zero).
                var rawBits = (int)reader.ReadBits(5);
                for (var i = 0; i < samples; i++)
                    residualBuffer[index++] = rawBits == 0 ? 0 : reader.ReadSignedBits(rawBits);

                continue;
            }

            for (var i = 0; i < samples; i++)
            {
                var quotient = reader.ReadUnary();
                var remainder = parameter == 0 ? 0u : reader.ReadBits(parameter);
                var value = ((uint)quotient << parameter) | remainder;

                // Rice codes are unsigned, so the sign is folded into the low bit (zigzag).
                residualBuffer[index++] = (int)(value >> 1) ^ -(int)(value & 1);
            }
        }
    }
}

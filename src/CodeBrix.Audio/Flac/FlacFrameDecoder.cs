using System;
using System.IO;

namespace CodeBrix.Audio.Flac;

/// <summary>
/// Decodes one FLAC frame at a time - frame header, subframes, stereo decorrelation and both CRC
/// checks - into per-channel integer buffers.
/// </summary>
/// <remarks>
/// <para>
/// This is the single FLAC frame implementation in the library. <see cref="FlacDecoder"/> drives it
/// over a whole file, where frames follow one another in a stream; the packet decoder behind
/// <c>FlacPacketCodecFactory</c> drives it over loose frames lifted out of a media container, one
/// per packet. Both hand it a <see cref="FlacBitReader"/> positioned at a frame's sync code, and
/// both get back exactly the same samples for the same frame.
/// </para>
/// <para>
/// A frame decodes independently of every other frame - FLAC carries no state across frame
/// boundaries - so nothing here needs resetting between frames, or after a seek.
/// </para>
/// </remarks>
internal sealed class FlacFrameDecoder
{
    private const uint FrameSyncCode = 0x3FFE; // 14 bits: 11111111111110

    private static readonly int[] BlockSizeTable =
        [0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768];

    private static readonly int[] SampleRateTable =
        [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000, 0, 0, 0, -1];

    private static readonly int[] BitsPerSampleTable = [0, 8, 12, -1, 16, 20, 24, 32];

    private readonly FlacStreamInfo streamInfo;
    private readonly FlacBitReader reader;
    private readonly bool allowOversizedBlocks;

    private int[][] channelBuffers;
    private int[] residualBuffer;
    private int capacity;

    /// <summary>
    /// Creates a frame decoder for a stream described by <paramref name="streamInfo"/>.
    /// </summary>
    /// <param name="streamInfo">The stream's STREAMINFO block, already validated.</param>
    /// <param name="reader">The reader frames are decoded from.</param>
    /// <param name="allowOversizedBlocks">
    /// True to accept a frame whose block size exceeds the STREAMINFO maximum by growing the
    /// buffers, as the file decoder always has; false to reject such a frame as corrupt, which is
    /// what a caller that sized its output from the maximum needs.
    /// </param>
    public FlacFrameDecoder(FlacStreamInfo streamInfo, FlacBitReader reader, bool allowOversizedBlocks)
    {
        this.streamInfo = streamInfo ?? throw new ArgumentNullException(nameof(streamInfo));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.allowOversizedBlocks = allowOversizedBlocks;

        capacity = streamInfo.MaximumBlockSize;
        channelBuffers = new int[streamInfo.Channels][];
        for (var i = 0; i < channelBuffers.Length; i++)
            channelBuffers[i] = new int[capacity];

        residualBuffer = new int[capacity];
    }

    /// <summary>
    /// The most recently decoded frame's samples, one array per channel, valid for the first
    /// <see cref="BlockSize"/> entries. The arrays may be replaced by a later, larger frame, so
    /// fetch this again after each <see cref="DecodeFrame"/>.
    /// </summary>
    public int[][] ChannelBuffers => channelBuffers;

    /// <summary>Number of samples per channel in the most recently decoded frame.</summary>
    public int BlockSize { get; private set; }

    /// <summary>
    /// The bit depth the most recently decoded frame's samples are at: the STREAMINFO depth, or the
    /// frame header's own when it states one.
    /// </summary>
    public int BitsPerSample { get; private set; }

    /// <summary>True when the most recently decoded frame used the variable-blocksize strategy.</summary>
    public bool IsVariableBlockSize { get; private set; }

    /// <summary>
    /// The most recently decoded frame's coded number: its first sample's number under the
    /// variable-blocksize strategy, or its frame number under the fixed one.
    /// </summary>
    public long FrameOrSampleNumber { get; private set; }

    /// <summary>
    /// Decodes the frame that starts at the reader's next byte boundary.
    /// </summary>
    /// <returns>True when a frame was decoded; false when the data ended before a frame began.</returns>
    /// <exception cref="InvalidDataException">The frame is corrupt, or fails a CRC check.</exception>
    /// <exception cref="EndOfStreamException">The data ended part-way through the frame.</exception>
    public bool DecodeFrame()
    {
        reader.ResetCrc();

        if (!FindFrameSync()) return false;

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
            ? streamInfo.BitsPerSample
            : BitsPerSampleTable[sampleSizeCode];
        if (frameBitsPerSample <= 0)
            throw new InvalidDataException("Invalid sample size code in a FLAC frame header.");

        // The CRC-8 covers the header up to but excluding itself.
        var computedHeaderCrc = reader.Crc8;
        var storedHeaderCrc = reader.ReadByteAligned();
        if (computedHeaderCrc != storedHeaderCrc)
            throw new InvalidDataException("FLAC frame header CRC mismatch; the stream is corrupt.");

        if (blockSize > capacity)
        {
            if (!allowOversizedBlocks)
                throw new InvalidDataException("A FLAC frame is larger than the STREAMINFO maximum block size.");
            GrowBuffers(blockSize);
        }

        DecodeSubframes(channelAssignment, blockSize, frameBitsPerSample);

        reader.AlignToByte();
        var computedFrameCrc = reader.Crc16;
        var storedFrameCrc = (ushort)reader.ReadBits(16);
        if (computedFrameCrc != storedFrameCrc)
            throw new InvalidDataException("FLAC frame CRC mismatch; the stream is corrupt.");

        BlockSize = blockSize;
        BitsPerSample = frameBitsPerSample;
        IsVariableBlockSize = blockingStrategyIsVariable;
        FrameOrSampleNumber = frameOrSampleNumber;
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
        capacity = blockSize;
    }

    private void DecodeSubframes(int channelAssignment, int blockSize, int frameBitsPerSample)
    {
        var channels = streamInfo.Channels;

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

using System;
using System.IO;

namespace CodeBrix.Audio.Flac;

/// <summary>
/// The STREAMINFO metadata block: the fixed description every FLAC stream begins with.
/// </summary>
/// <remarks>
/// This is why a FLAC file needs no scanning to report its duration - the total sample count is
/// stated up front, unlike a VBR MP3 where it has to be estimated or counted.
/// </remarks>
internal sealed class FlacStreamInfo
{
    /// <summary>Smallest block size (in frames) used in the stream.</summary>
    public int MinimumBlockSize { get; init; }

    /// <summary>Largest block size (in frames) used in the stream.</summary>
    public int MaximumBlockSize { get; init; }

    /// <summary>Smallest frame size in bytes, or 0 when the encoder did not record it.</summary>
    public int MinimumFrameSize { get; init; }

    /// <summary>Largest frame size in bytes, or 0 when the encoder did not record it.</summary>
    public int MaximumFrameSize { get; init; }

    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate { get; init; }

    /// <summary>Number of channels, 1 to 8.</summary>
    public int Channels { get; init; }

    /// <summary>Bits per sample, 4 to 32.</summary>
    public int BitsPerSample { get; init; }

    /// <summary>
    /// Total number of frames (samples per channel), or 0 when the encoder did not know it -
    /// which happens when FLAC is produced by a pipe.
    /// </summary>
    public long TotalSamples { get; init; }

    /// <summary>MD5 of the unencoded audio, or all zeroes when not computed.</summary>
    public byte[] Md5Signature { get; init; } = new byte[16];

    /// <summary>True when every block in the stream is the same size.</summary>
    public bool HasFixedBlockSize => MinimumBlockSize == MaximumBlockSize && MinimumBlockSize > 0;

    /// <summary>Duration of the stream, or <see cref="TimeSpan.Zero"/> when the length is unknown.</summary>
    public TimeSpan Duration => SampleRate > 0 && TotalSamples > 0
        ? TimeSpan.FromSeconds((double)TotalSamples / SampleRate)
        : TimeSpan.Zero;

    /// <summary>The size in bytes of a STREAMINFO block's body.</summary>
    public const int BodyLength = 34;

    /// <summary>
    /// Reads a STREAMINFO block's 34-byte body - everything after its metadata block header.
    /// </summary>
    /// <param name="reader">A reader positioned at the start of the body.</param>
    /// <returns>The parsed block. Call <see cref="Validate"/> before decoding with it.</returns>
    public static FlacStreamInfo Read(FlacBitReader reader)
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

    /// <summary>
    /// Rejects a STREAMINFO block that no frame could be decoded against.
    /// </summary>
    /// <exception cref="InvalidDataException">The channel count, bit depth or maximum block size is unusable.</exception>
    public void Validate()
    {
        if (Channels is < 1 or > 8)
            throw new InvalidDataException($"Unsupported FLAC channel count: {Channels}.");
        if (BitsPerSample is < 4 or > 32)
            throw new InvalidDataException($"Unsupported FLAC bit depth: {BitsPerSample}.");
        if (MaximumBlockSize <= 0)
            throw new InvalidDataException("The FLAC stream declares an invalid maximum block size.");
    }
}

/// <summary>
/// One entry of a SEEKTABLE metadata block: a sample number and where its frame starts.
/// </summary>
internal readonly struct FlacSeekPoint
{
    /// <summary>Creates a seek point.</summary>
    public FlacSeekPoint(long sampleNumber, long byteOffset, int frameSamples)
    {
        SampleNumber = sampleNumber;
        ByteOffset = byteOffset;
        FrameSamples = frameSamples;
    }

    /// <summary>First sample (frame index) of the target frame.</summary>
    public long SampleNumber { get; }

    /// <summary>Offset of the target frame, counted from the first audio frame.</summary>
    public long ByteOffset { get; }

    /// <summary>Number of samples in the target frame.</summary>
    public int FrameSamples { get; }

    /// <summary>
    /// True for a placeholder point, which carries no position and must be ignored.
    /// </summary>
    public bool IsPlaceholder => SampleNumber == unchecked((long)0xFFFFFFFFFFFFFFFF);
}

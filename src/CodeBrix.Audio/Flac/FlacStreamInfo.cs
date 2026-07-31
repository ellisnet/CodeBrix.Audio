using System;

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

using System;
using System.IO;

namespace CodeBrix.Audio.Flac;

/// <summary>
/// Reads a FLAC stream a bit at a time, most significant bit first, and keeps the frame CRCs
/// up to date as bytes are consumed.
/// </summary>
/// <remarks>
/// FLAC is a bit-packed format: subframe headers, Rice-coded residuals and LPC coefficients all
/// sit at arbitrary bit offsets, so nearly everything is read through here. The CRC registers
/// live in this class because CRCs cover whole bytes, and this is the only place that knows when
/// a byte has actually been pulled from the stream.
/// </remarks>
internal sealed class FlacBitReader
{
    private readonly Stream stream;
    private readonly byte[] buffer;

    private int bufferLength;
    private int bufferPosition;

    private ulong bitCache;
    private int bitCacheCount;

    private byte crc8;
    private ushort crc16;

    /// <summary>Creates a reader over the given stream.</summary>
    /// <param name="source">The stream to read from; it is not disposed by this class.</param>
    /// <param name="bufferSize">Size of the internal read buffer, in bytes.</param>
    public FlacBitReader(Stream source, int bufferSize = 16384)
    {
        stream = source ?? throw new ArgumentNullException(nameof(source));
        buffer = new byte[bufferSize];
    }

    /// <summary>True once the underlying stream has no more data and the cache is empty.</summary>
    public bool EndOfStream { get; private set; }

    /// <summary>The byte offset in the source stream of the next unread bit's byte.</summary>
    public long BytePosition => stream.Position - bufferLength + bufferPosition - bitCacheCount / 8;

    /// <summary>Resets both CRC registers - call at the start of a frame.</summary>
    public void ResetCrc()
    {
        crc8 = 0;
        crc16 = 0;
    }

    /// <summary>The CRC-8 of every byte consumed since <see cref="ResetCrc"/>.</summary>
    public byte Crc8 => crc8;

    /// <summary>The CRC-16 of every byte consumed since <see cref="ResetCrc"/>.</summary>
    public ushort Crc16 => crc16;

    /// <summary>Discards buffered state and re-reads from the stream's current position.</summary>
    public void Reset()
    {
        bufferLength = 0;
        bufferPosition = 0;
        bitCache = 0;
        bitCacheCount = 0;
        EndOfStream = false;
        ResetCrc();
    }

    /// <summary>Drops any partially consumed byte so the next read starts on a byte boundary.</summary>
    public void AlignToByte() => bitCacheCount -= bitCacheCount % 8;

    /// <summary>True when the next bit to be read starts a byte.</summary>
    public bool IsByteAligned => bitCacheCount % 8 == 0;

    /// <summary>
    /// Reads up to 32 bits as an unsigned value.
    /// </summary>
    /// <param name="count">How many bits to read (0 to 32).</param>
    /// <returns>The value, right-aligned.</returns>
    /// <exception cref="EndOfStreamException">The stream ran out mid-value.</exception>
    public uint ReadBits(int count)
    {
        if (count == 0) return 0;
        if (count is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(count));

        while (bitCacheCount < count)
        {
            if (!FillByte()) throw new EndOfStreamException("The FLAC stream ended mid-value.");
        }

        bitCacheCount -= count;
        var value = (uint)(bitCache >> bitCacheCount);
        bitCache &= (1UL << bitCacheCount) - 1;
        return count == 32 ? value : value & ((1u << count) - 1);
    }

    /// <summary>
    /// Reads up to 32 bits as a two's-complement signed value.
    /// </summary>
    /// <param name="count">How many bits to read.</param>
    public int ReadSignedBits(int count)
    {
        if (count == 0) return 0;
        var raw = ReadBits(count);
        if (count == 32) return unchecked((int)raw);

        // Sign-extend from the value's own width.
        var signBit = 1u << (count - 1);
        return (raw & signBit) != 0 ? unchecked((int)(raw | ~((1u << count) - 1))) : (int)raw;
    }

    /// <summary>
    /// Reads a unary-coded value: the number of zero bits before the next one bit.
    /// </summary>
    /// <remarks>This is the quotient half of every Rice-coded residual sample.</remarks>
    public int ReadUnary()
    {
        var count = 0;
        while (true)
        {
            if (bitCacheCount == 0 && !FillByte())
                throw new EndOfStreamException("The FLAC stream ended inside a unary value.");

            // Look at the highest cached bit.
            bitCacheCount--;
            var bit = (bitCache >> bitCacheCount) & 1;
            bitCache &= (1UL << bitCacheCount) - 1;

            if (bit != 0) return count;
            count++;

            if (count > 1 << 20)
                throw new InvalidDataException("Implausible unary run in the FLAC stream; the data is corrupt.");
        }
    }

    /// <summary>
    /// Reads one whole byte, which must be byte-aligned. Used for the CRC fields.
    /// </summary>
    public byte ReadByteAligned()
    {
        if (!IsByteAligned) throw new InvalidOperationException("The reader is not byte aligned.");
        return (byte)ReadBits(8);
    }

    /// <summary>
    /// Reads the UTF-8-style variable-length number that identifies a frame or its first sample.
    /// </summary>
    /// <remarks>
    /// FLAC borrows UTF-8's encoding shape for this field, extended to 7 bytes so a 36-bit sample
    /// number fits.
    /// </remarks>
    public ulong ReadUtf8Number()
    {
        var first = ReadBits(8);

        if ((first & 0x80) == 0) return first;

        int extraBytes;
        ulong value;

        if ((first & 0xE0) == 0xC0) { extraBytes = 1; value = first & 0x1Fu; }
        else if ((first & 0xF0) == 0xE0) { extraBytes = 2; value = first & 0x0Fu; }
        else if ((first & 0xF8) == 0xF0) { extraBytes = 3; value = first & 0x07u; }
        else if ((first & 0xFC) == 0xF8) { extraBytes = 4; value = first & 0x03u; }
        else if ((first & 0xFE) == 0xFC) { extraBytes = 5; value = first & 0x01u; }
        else if (first == 0xFE) { extraBytes = 6; value = 0; }
        else throw new InvalidDataException("Invalid UTF-8 coded number in a FLAC frame header.");

        for (var i = 0; i < extraBytes; i++)
        {
            var next = ReadBits(8);
            if ((next & 0xC0) != 0x80)
                throw new InvalidDataException("Invalid UTF-8 coded number in a FLAC frame header.");
            value = (value << 6) | (next & 0x3Fu);
        }

        return value;
    }

    /// <summary>
    /// Skips whole bytes, byte-aligned. Used to step over metadata blocks we do not interpret.
    /// </summary>
    public void SkipBytes(long count)
    {
        AlignToByte();

        while (count > 0)
        {
            if (bitCacheCount >= 8)
            {
                ReadBits(8);
                count--;
                continue;
            }

            var available = bufferLength - bufferPosition;
            if (available <= 0)
            {
                if (!FillBuffer()) throw new EndOfStreamException("The FLAC stream ended inside a metadata block.");
                continue;
            }

            var take = (int)Math.Min(available, count);
            for (var i = 0; i < take; i++) UpdateCrc(buffer[bufferPosition + i]);
            bufferPosition += take;
            count -= take;
        }
    }

    /// <summary>
    /// Reads whole bytes into a buffer, byte-aligned.
    /// </summary>
    public void ReadBytes(Span<byte> destination)
    {
        AlignToByte();
        for (var i = 0; i < destination.Length; i++) destination[i] = (byte)ReadBits(8);
    }

    private bool FillByte()
    {
        if (bufferPosition >= bufferLength && !FillBuffer())
        {
            EndOfStream = true;
            return false;
        }

        var value = buffer[bufferPosition++];
        UpdateCrc(value);

        bitCache = (bitCache << 8) | value;
        bitCacheCount += 8;
        return true;
    }

    private bool FillBuffer()
    {
        bufferLength = stream.Read(buffer, 0, buffer.Length);
        bufferPosition = 0;
        return bufferLength > 0;
    }

    private void UpdateCrc(byte value)
    {
        crc8 = FlacCrc.Update8(crc8, value);
        crc16 = FlacCrc.Update16(crc16, value);
    }
}

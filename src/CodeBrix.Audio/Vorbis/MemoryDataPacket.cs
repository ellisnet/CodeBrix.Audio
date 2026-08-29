using System;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// A packet whose bytes are held in memory rather than gathered from Ogg pages: what a media
/// container hands over when IT does the framing.
/// </summary>
/// <remarks>
/// One instance is reused for every packet of a stream (see <see cref="SetData"/>), so the
/// packet-level decode path allocates nothing per packet.
/// </remarks>
internal sealed class MemoryDataPacket : DataPacket
{
    private ReadOnlyMemory<byte> data;
    private int index;

    /// <summary>Creates a packet over the given bytes.</summary>
    /// <param name="data">The complete packet, without container framing.</param>
    public MemoryDataPacket(ReadOnlyMemory<byte> data)
    {
        this.data = data;
    }

    /// <summary>Points this packet at a different set of bytes and rewinds it.</summary>
    /// <param name="value">The next complete packet.</param>
    public void SetData(ReadOnlyMemory<byte> value)
    {
        data = value;
        Reset();
    }

    /// <inheritdoc />
    protected override int TotalBits => data.Length * 8;

    /// <inheritdoc />
    protected override int ReadNextByte()
    {
        if (index >= data.Length) return -1;
        return data.Span[index++];
    }

    /// <inheritdoc />
    public override void Reset()
    {
        index = 0;
        base.Reset();
    }
}

using System;
using CodeBrix.Audio.Vorbis.Contracts;

namespace CodeBrix.Audio.Vorbis;

/// <summary>
/// Feeds <see cref="StreamDecoder"/> a fixed, already-known set of packets - the three Vorbis setup
/// headers a media container carries in its codec-private data - and nothing after them.
/// </summary>
/// <remarks>
/// <para>
/// The decoder's constructor is the only header parser in this library, and it reads its three
/// headers from a packet provider. Handing it these three through the same door means the
/// packet-level path shares that parser rather than growing a second one.
/// </para>
/// <para>
/// Nothing pulls from this provider afterwards: audio packets arrive on the packet-level entry
/// point (<see cref="StreamDecoder.DecodeSinglePacket"/>) instead of being pulled, which is exactly
/// why that entry point exists - a pulling decoder treats one null packet as a permanent end of
/// stream. <see cref="CanSeek"/> is false, so the decoder never asks this provider to seek either.
/// </para>
/// </remarks>
internal sealed class HeaderPacketProvider : IPacketProvider
{
    private readonly IPacket[] packets;
    private int index;

    /// <summary>Creates a provider over the given packets, delivered in order.</summary>
    /// <param name="packets">The packets to hand out, in stream order.</param>
    public HeaderPacketProvider(IPacket[] packets)
    {
        if (packets == null) throw new ArgumentNullException(nameof(packets));
        this.packets = packets;
    }

    /// <inheritdoc />
    public bool CanSeek => false;

    /// <inheritdoc />
    public int StreamSerial => 0;

    /// <inheritdoc />
    public IPacket GetNextPacket() => index < packets.Length ? packets[index++] : null;

    /// <inheritdoc />
    public IPacket PeekNextPacket() => index < packets.Length ? packets[index] : null;

    /// <inheritdoc />
    public long SeekTo(long granulePos, int preRoll, GetPacketGranuleCount getPacketGranuleCount) =>
        throw new NotSupportedException("Packet-level decoding seeks in the container, not in the codec.");

    /// <inheritdoc />
    public long GetGranuleCount() => 0;
}

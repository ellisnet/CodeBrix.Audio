using System;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// One compressed audio packet on its way from a media container to a decoder: the bytes, and
/// optionally where in the timeline they belong.
/// </summary>
/// <remarks>
/// A struct, and a small one, because packets arrive on the audio thread - typically several hundred
/// per second - and nothing there should allocate. <see cref="Data"/> is a view, not a copy: whoever
/// produced the packet keeps ownership of the memory behind it and must leave it intact until the
/// packet has been handed to the player.
/// </remarks>
public readonly struct AudioPacket
{
    /// <summary>Creates a packet with no timestamp.</summary>
    /// <param name="data">The complete compressed packet, exactly as the container carried it.</param>
    public AudioPacket(ReadOnlyMemory<byte> data)
        : this(data, null)
    {
    }

    /// <summary>Creates a packet with the timestamp the container gave it.</summary>
    /// <param name="data">The complete compressed packet, exactly as the container carried it.</param>
    /// <param name="timestamp">The packet's position in the timeline, or null when it has none.</param>
    public AudioPacket(ReadOnlyMemory<byte> data, TimeSpan? timestamp)
    {
        Data = data;
        Timestamp = timestamp;
    }

    /// <summary>The compressed packet, without container framing.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Where this packet sits in the timeline, when the container says so.
    /// </summary>
    /// <remarks>
    /// The player does not need it - its clock counts decoded samples from the position a
    /// repositioning last established - so a source with nothing meaningful to report may leave it
    /// null. It is carried because a source that HAS timestamps usually wants them travelling with
    /// the packets rather than in a second structure alongside.
    /// </remarks>
    public TimeSpan? Timestamp { get; }

    /// <summary>Whether this packet carries no bytes.</summary>
    public bool IsEmpty => Data.IsEmpty;
}

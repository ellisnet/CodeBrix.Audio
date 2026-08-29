using System;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// One compressed audio packet on its way from a media container to a decoder: the bytes, and
/// optionally where in the timeline they belong, how much of the end of the track they pad, or - for
/// a packet that carries no bytes at all - how much audio went missing.
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
        : this(data, timestamp, TimeSpan.Zero)
    {
    }

    /// <summary>
    /// Creates a packet the container marked as padding the end of the track.
    /// </summary>
    /// <param name="data">The complete compressed packet, exactly as the container carried it.</param>
    /// <param name="timestamp">The packet's position in the timeline, or null when it has none.</param>
    /// <param name="discardPadding">
    /// How much audio at the END of the track this packet's block is padding - the value a container
    /// records per block (Matroska calls it DiscardPadding). Zero, and negative values, mean none.
    /// </param>
    public AudioPacket(ReadOnlyMemory<byte> data, TimeSpan? timestamp, TimeSpan discardPadding)
    {
        Data = data;
        Timestamp = timestamp;
        DiscardPadding = discardPadding > TimeSpan.Zero ? discardPadding : TimeSpan.Zero;
        IsLoss = false;
        LossDuration = TimeSpan.Zero;
        LossFrames = 0;
    }

    // The loss constructor. Private because the two Loss factories read far better at a call site
    // than a constructor whose emptiness is the whole point.
    private AudioPacket(TimeSpan lossDuration, int lossFrames, TimeSpan? timestamp)
    {
        Data = ReadOnlyMemory<byte>.Empty;
        Timestamp = timestamp;
        DiscardPadding = TimeSpan.Zero;
        IsLoss = true;
        LossDuration = lossDuration > TimeSpan.Zero ? lossDuration : TimeSpan.Zero;
        LossFrames = Math.Max(0, lossFrames);
    }

    /// <summary>
    /// Creates a packet that reports a GAP of a known duration: audio the demultiplexer knows is
    /// missing rather than audio it is delivering.
    /// </summary>
    /// <param name="duration">How much audio was lost. Zero, and negative values, mean none.</param>
    /// <param name="timestamp">Where the gap starts in the timeline, or null when that is not known.</param>
    /// <returns>A packet carrying no bytes, marked <see cref="IsLoss"/>.</returns>
    /// <remarks>
    /// <para>
    /// A demultiplexer emits one of these when it can see that packets are missing - a jump in the
    /// timestamps, a container-level loss marker, a network reader that gave up on a retransmission.
    /// <see cref="PacketAudioPlayer"/> asks the decoder to conceal exactly that much
    /// (<c>IPacketSoundDecoder.ConcealLoss</c>) and fills whatever the decoder cannot with silence,
    /// so the audio that follows the gap stays where it belongs in the timeline instead of sliding
    /// earlier by the length of what was lost.
    /// </para>
    /// <para>
    /// Do not use it for a moment when the reader has simply not kept up: that is an underrun, and
    /// the way to report it is to return false from
    /// <see cref="IAudioPacketSource.TryReadPacket"/>, which costs nothing and does not consume any
    /// of the timeline.
    /// </para>
    /// </remarks>
    public static AudioPacket Loss(TimeSpan duration, TimeSpan? timestamp = null) =>
        new AudioPacket(duration, 0, timestamp);

    /// <summary>
    /// Creates a packet that reports a GAP of a known length in frames - the exact form of
    /// <see cref="Loss(TimeSpan, TimeSpan?)"/>, for a container that counts samples rather than time.
    /// </summary>
    /// <param name="frames">
    /// How much audio was lost, counted in FRAMES PER CHANNEL at the decoder's own sample rate.
    /// Zero, and negative values, mean none.
    /// </param>
    /// <param name="timestamp">Where the gap starts in the timeline, or null when that is not known.</param>
    /// <returns>A packet carrying no bytes, marked <see cref="IsLoss"/>.</returns>
    /// <remarks>
    /// Frames avoid the rounding a duration goes through, so a container that knows the gap to the
    /// sample should say so this way. See <see cref="Loss(TimeSpan, TimeSpan?)"/> for what the player
    /// then does with it.
    /// </remarks>
    public static AudioPacket Loss(int frames, TimeSpan? timestamp = null) =>
        new AudioPacket(TimeSpan.Zero, frames, timestamp);

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

    /// <summary>
    /// How much audio at the END of the track this packet's block is encoder padding, as the
    /// container recorded it per block - Matroska's DiscardPadding, and the same idea by other names
    /// elsewhere. <see cref="TimeSpan.Zero"/> means none, which is what almost every packet says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It matters on the LAST packet of a track, where it says how much of the encoder's tail must
    /// never be heard. <see cref="PacketAudioPlayer"/> holds back whichever is larger, this or the
    /// track-level <see cref="PacketAudioPlayer.SetTrailingTrim(TimeSpan)"/>, so a container may pass
    /// its per-block value straight through and not think about it again.
    /// </para>
    /// <para>
    /// A value on a packet that is NOT the last one raises the hold-back for as long as that packet
    /// is the most recent - the audio is delayed, never dropped - and the next packet without one
    /// lets it fall back to the track-level trim.
    /// </para>
    /// </remarks>
    public TimeSpan DiscardPadding { get; }

    /// <summary>
    /// Whether this packet reports a GAP - audio the demultiplexer knows is missing - rather than
    /// audio it is delivering. Created with <see cref="Loss(TimeSpan, TimeSpan?)"/> or
    /// <see cref="Loss(int, TimeSpan?)"/>.
    /// </summary>
    /// <remarks>
    /// A loss packet carries no bytes, so <see cref="IsEmpty"/> is true for it as well; check this
    /// first when the difference matters.
    /// </remarks>
    public bool IsLoss { get; }

    /// <summary>
    /// How long the gap is, when <see cref="IsLoss"/> is true and the length was given as a duration;
    /// otherwise <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan LossDuration { get; }

    /// <summary>
    /// How long the gap is in FRAMES PER CHANNEL, when <see cref="IsLoss"/> is true and the length was
    /// given in frames; otherwise 0.
    /// </summary>
    public int LossFrames { get; }

    /// <summary>Whether this packet carries no bytes.</summary>
    /// <remarks>
    /// True for a gap reported with <see cref="Loss(TimeSpan, TimeSpan?)"/>, and true for the older,
    /// lengthless way of saying the same thing: a packet with empty <see cref="Data"/> and
    /// <see cref="IsLoss"/> false means one packet was lost without saying how long it was, and the
    /// player passes that on to the decoder as an empty packet.
    /// </remarks>
    public bool IsEmpty => Data.IsEmpty;
}

using CodeBrix.Audio.Engine.Enums;

namespace CodeBrix.Audio.Engine.Interfaces;  //was previously: SoundFlow.Interfaces

/// <summary>
/// Decodes one compressed audio packet at a time, as a media container delivers them.
/// </summary>
/// <remarks>
/// <para>
/// This is the packet-level counterpart of <see cref="ISoundDecoder"/>. <see cref="ISoundDecoder"/>
/// reads a <see cref="Stream"/> - a whole file, self-framing, with its own container inside it. A
/// demultiplexer instead hands out the raw codec packets it lifted out of a container, with no
/// framing of their own and no stream to seek in, which is what this interface consumes. Both seams
/// coexist: an implementation may supply either, or both.
/// </para>
/// <para>
/// The decoder carries the codec state between packets, so packets must be fed in stream order.
/// After repositioning the source, call <see cref="Reset"/> before feeding the packet at the new
/// position.
/// </para>
/// </remarks>
public interface IPacketSoundDecoder : IDisposable
{
    /// <summary>
    /// Gets the number of channels the decoder produces.
    /// </summary>
    int Channels { get; }

    /// <summary>
    /// Gets the sample rate, in Hz, of the samples the decoder produces.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Gets the format of the samples the decoder writes, which is always
    /// <see cref="Enums.SampleFormat.F32"/> - the engine's mixing format.
    /// </summary>
    SampleFormat SampleFormat { get; }

    /// <summary>
    /// Gets the largest number of samples a single <see cref="DecodePacket"/> call can produce, so a
    /// caller can size the output buffer once and reuse it.
    /// </summary>
    /// <remarks>
    /// The value counts INTERLEAVED samples, so it already includes <see cref="Channels"/>. It is a
    /// worst case derived from the codec's own limits - for Vorbis from the block sizes in the setup
    /// header, for Opus 5760 samples per channel (a 120 ms packet at 48 kHz) - not the size of any
    /// particular packet.
    /// </remarks>
    int MaxSamplesPerPacket { get; }

    /// <summary>
    /// Gets the number of samples PER CHANNEL that a player should decode and discard at the start of
    /// the stream, before the first sample it keeps.
    /// </summary>
    /// <remarks>
    /// This is the codec's own priming - Opus reports its pre-skip here (the encoder delay recorded in
    /// its identification header); Vorbis has none and reports 0. It is not the container's start
    /// trim, which the caller applies on top of it.
    /// </remarks>
    int PreSkipSamples { get; }

    /// <summary>
    /// Decodes one packet, writing the samples that became final into <paramref name="output"/>.
    /// </summary>
    /// <param name="packet">One complete compressed packet, exactly as the container carried it.</param>
    /// <param name="output">
    /// The buffer to write interleaved samples into. Size it to <see cref="MaxSamplesPerPacket"/>.
    /// </param>
    /// <returns>
    /// The number of interleaved samples written, which MAY BE ZERO - see the remarks. It is never
    /// more than <see cref="MaxSamplesPerPacket"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Zero is a normal, successful result, not an end of stream: a lapped-transform codec finalises
    /// packet N's samples only once packet N+1 has been decoded and overlapped onto it, so the first
    /// packet fed after construction or <see cref="Reset"/> legitimately yields nothing. Keep feeding
    /// packets; the caller decides when the stream has ended, because only the container knows.
    /// </para>
    /// <para>
    /// The decoder does not trim the tail of the stream. Where a container states the exact playable
    /// length (a total-sample count, or an end-trim / discard-padding field), the caller applies it to
    /// the samples this method returns.
    /// </para>
    /// <para>
    /// AN EMPTY PACKET MEANS ONE PACKET WAS LOST. It is the long-standing convention for telling a
    /// decoder to conceal a gap it was given no bytes for, and a decoder with concealment of its own
    /// answers it with a packet's worth of concealment audio. A decoder without any returns zero. A
    /// caller that knows HOW LONG the gap was should use <see cref="ConcealLoss"/> instead, which
    /// says so rather than leaving the decoder to assume one packet.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="output"/> is too small for the samples this packet produces. Sizing it to
    /// <see cref="MaxSamplesPerPacket"/> guarantees this cannot happen.
    /// </exception>
    int DecodePacket(ReadOnlySpan<byte> packet, Span<float> output);

    /// <summary>
    /// Discards the decoder's inter-packet state, so the next packet fed may come from anywhere in the
    /// stream. Call this after repositioning the source.
    /// </summary>
    /// <remarks>
    /// A codec that carries state across packets cannot produce correct audio for the first packet
    /// after a jump, so a caller seeking exactly starts a little before its target - one packet for
    /// Vorbis, about 80 ms for Opus - and discards what comes back until the target is reached.
    /// </remarks>
    void Reset();

    /// <summary>
    /// Gets whether <see cref="ConcealLoss"/> produces real concealment audio for a gap, rather than
    /// leaving the caller to fill it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False by default, which is the honest answer for a codec with no packet-loss concealment of
    /// its own (Vorbis, for one): a gap in such a stream becomes silence of the right length. A
    /// decoder whose codec CAN synthesise plausible audio across a gap - Opus, whose concealment is
    /// part of the specification - overrides this and returns true, so an application can say which
    /// of the two it is getting without calling <see cref="ConcealLoss"/> to find out.
    /// </para>
    /// <para>
    /// A player does not need to consult it: <see cref="ConcealLoss"/> is safe to call either way,
    /// and a caller that gets nothing back fills the gap itself.
    /// </para>
    /// </remarks>
    bool SupportsLossConcealment => false;

    /// <summary>
    /// Produces concealment audio for a gap of <paramref name="lostFrames"/> frames per channel -
    /// audio the demultiplexer knows is missing, because packets were lost or a container recorded a
    /// discontinuity.
    /// </summary>
    /// <param name="lostFrames">
    /// How long the gap is, counted in FRAMES PER CHANNEL at <see cref="SampleRate"/> - the same unit
    /// as <see cref="PreSkipSamples"/>. Must not be negative.
    /// </param>
    /// <param name="output">
    /// The buffer to write interleaved samples into. Size it to <see cref="MaxSamplesPerPacket"/>, as
    /// for <see cref="DecodePacket"/>.
    /// </param>
    /// <returns>
    /// The number of interleaved samples written, which may be FEWER than
    /// <paramref name="lostFrames"/> multiplied by <see cref="Channels"/> - a codec that conceals in
    /// fixed-size steps covers a long gap over several calls - and may be zero when the decoder has
    /// no concealment to offer. Never more than <see cref="MaxSamplesPerPacket"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE CALLER LOOPS. Call this until the gap is covered, feeding the frames still missing each
    /// time, and fill whatever is left over with silence when a call comes back with zero. That
    /// keeps the timeline intact whatever the decoder does: the audio after the gap stays where it
    /// belongs, rather than sliding earlier by the length of what was lost.
    /// </para>
    /// <para>
    /// The default implementation forwards to <c>DecodePacket</c> with an empty packet, which is the
    /// long-standing way of telling a decoder "one packet was lost, do what you can". A decoder that
    /// already conceals that way keeps working with no change; one with real concealment overrides
    /// this so it can conceal the length the container actually lost instead of guessing at one
    /// packet.
    /// </para>
    /// <para>
    /// Like <see cref="DecodePacket"/>, this advances the decoder's state: the gap becomes part of
    /// the stream, and the packet fed afterwards is decoded as the one that follows it.
    /// </para>
    /// </remarks>
    int ConcealLoss(int lostFrames, Span<float> output) => DecodePacket(ReadOnlySpan<byte>.Empty, output);
}

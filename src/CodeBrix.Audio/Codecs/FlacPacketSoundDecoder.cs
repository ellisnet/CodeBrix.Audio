using System;
using System.IO;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Flac;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Decodes FLAC audio one container packet at a time, using the fully managed frame decoder in
/// <c>CodeBrix.Audio.Flac</c>.
/// </summary>
/// <remarks>
/// <para>
/// The stream header arrives in the container's codec-private data rather than at the front of a
/// .flac file, so its STREAMINFO is read here; from then on each packet the demultiplexer produces
/// is one complete FLAC frame, decoded on its own by the same <see cref="FlacFrameDecoder"/> the
/// file reader uses.
/// </para>
/// <para>
/// FLAC frames carry no state from one to the next, so every packet's samples are final as soon as
/// it is decoded - the first packet after construction or <see cref="Reset"/> produces its full
/// block - and the samples are bit-exact: a packet decodes to precisely what the same frame decodes
/// to inside a .flac file.
/// </para>
/// </remarks>
internal sealed class FlacPacketSoundDecoder : IPacketSoundDecoder
{
    private const int MetadataBlockHeaderLength = 4;
    private const int StreamInfoBlockType = 0;
    private const int InvalidBlockType = 127;

    private readonly object syncLock = new object();
    private readonly FlacStreamInfo streamInfo;
    private readonly float streamScale;

    private FlacBitReader reader;
    private FlacFrameDecoder frameDecoder;
    private bool disposed;

    /// <summary>
    /// Creates a decoder for the stream a STREAMINFO block describes.
    /// </summary>
    /// <param name="streamInfo">
    /// The stream's STREAMINFO, as <see cref="ParseCodecPrivate"/> returns it from a container's
    /// codec-private data.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamInfo"/> is null.</exception>
    /// <exception cref="InvalidDataException">The STREAMINFO describes a stream no frame could be decoded against.</exception>
    public FlacPacketSoundDecoder(FlacStreamInfo streamInfo)
    {
        this.streamInfo = streamInfo ?? throw new ArgumentNullException(nameof(streamInfo));
        streamInfo.Validate();

        // FLAC samples are signed integers at the stream's own bit depth; the engine mixes in
        // float, so they are scaled by the depth's full-scale value - the same scale the stream
        // decoder uses, so the two paths agree to the last bit.
        streamScale = ScaleFor(streamInfo.BitsPerSample);

        reader = new FlacBitReader();
        frameDecoder = new FlacFrameDecoder(streamInfo, reader, allowOversizedBlocks: false);
    }

    /// <inheritdoc />
    public int Channels => streamInfo.Channels;

    /// <inheritdoc />
    public int SampleRate => streamInfo.SampleRate;

    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc />
    /// <remarks>
    /// The STREAMINFO maximum block size times the channel count. A frame larger than the stated
    /// maximum is corrupt by definition, and is rejected rather than allowed to overrun the buffer.
    /// </remarks>
    public int MaxSamplesPerPacket => streamInfo.MaximumBlockSize * streamInfo.Channels;

    /// <inheritdoc />
    /// <remarks>FLAC has no codec-level priming, so this is always 0.</remarks>
    public int PreSkipSamples => 0;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <paramref name="packetData"/> must be exactly one complete FLAC frame, from its sync code
    /// through its CRC-16. Every sample in it is final, so a successful call returns the frame's
    /// whole block - never zero, except for an empty packet.
    /// </para>
    /// <para>
    /// Samples are scaled by the bit depth the frame was coded at: the STREAMINFO depth, or the
    /// frame header's own where it states one.
    /// </para>
    /// <para>
    /// A packet that is not one intact frame - truncated, damaged (both CRCs are checked), carrying
    /// trailing bytes, or with a block larger than the STREAMINFO maximum - throws
    /// <see cref="InvalidDataException"/>, as the managed Vorbis decoder does for corrupt data.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">The packet is not one intact FLAC frame of this stream.</exception>
    public int DecodePacket(ReadOnlySpan<byte> packetData, Span<float> output)
    {
        lock (syncLock)
        {
            if (disposed) return 0;
            if (packetData.IsEmpty) return 0;

            reader.LoadPacket(packetData);

            bool decoded;
            try
            {
                decoded = frameDecoder.DecodeFrame();
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("The FLAC packet ends part-way through its frame.", ex);
            }

            if (!decoded)
                throw new InvalidDataException("The FLAC packet is too short to hold a frame.");
            if (reader.BytePosition != packetData.Length)
                throw new InvalidDataException("The FLAC packet carries bytes beyond the end of its frame.");

            var channels = streamInfo.Channels;
            var blockSize = frameDecoder.BlockSize;
            var samples = blockSize * channels;

            if (samples > output.Length)
            {
                throw new ArgumentException(
                    $"The output buffer holds {output.Length} samples, but this packet produces " +
                    $"{samples}. Size the buffer to MaxSamplesPerPacket ({MaxSamplesPerPacket} " +
                    "samples), which no packet can exceed.",
                    nameof(output));
            }

            var frameBitsPerSample = frameDecoder.BitsPerSample;
            var scale = frameBitsPerSample == streamInfo.BitsPerSample ? streamScale : ScaleFor(frameBitsPerSample);
            var channelBuffers = frameDecoder.ChannelBuffers;

            for (var channel = 0; channel < channels; channel++)
            {
                var source = channelBuffers[channel];
                for (var i = 0; i < blockSize; i++)
                    output[i * channels + channel] = source[i] * scale;
            }

            return samples;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// False: FLAC has no packet-loss concealment of its own, so <see cref="ConcealLoss"/> fills a
    /// gap with silence rather than with synthesised audio.
    /// </remarks>
    public bool SupportsLossConcealment => false;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// FLAC cannot invent audio across a gap, so what a lost stretch becomes here is SILENCE OF
    /// EXACTLY THE LENGTH THAT WAS LOST. That is the point of implementing it at all: the timeline
    /// stays intact, and every sample after the gap keeps the position it had before the loss
    /// instead of sliding earlier by the length of what went missing.
    /// </para>
    /// <para>
    /// A gap longer than <see cref="MaxSamplesPerPacket"/> is covered over several calls, as the
    /// interface allows - this returns as much as the buffer holds and waits to be asked again.
    /// </para>
    /// <para>
    /// <see cref="DecodePacket"/> with an EMPTY packet still returns 0, because that convention says
    /// a packet was lost without saying how long it was, and a decoder with no concealment has
    /// nothing useful to do with it.
    /// </para>
    /// </remarks>
    public int ConcealLoss(int lostFrames, Span<float> output)
    {
        lock (syncLock)
        {
            if (disposed || lostFrames <= 0) return 0;

            var channels = streamInfo.Channels;
            var frames = Math.Min(lostFrames, output.Length / channels);
            frames = Math.Min(frames, MaxSamplesPerPacket / channels);
            if (frames <= 0) return 0;

            var samples = frames * channels;
            output.Slice(0, samples).Clear();
            return samples;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// FLAC carries nothing from one frame to the next, so there is no codec state to discard; this
    /// only drops the last packet's bytes. The packet fed next decodes exactly, wherever it is from.
    /// </remarks>
    public void Reset()
    {
        lock (syncLock)
        {
            if (disposed) return;
            reader.Reset();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (syncLock)
        {
            if (disposed) return;
            disposed = true;
            frameDecoder = null;
            reader = null;
        }
    }

    /// <summary>
    /// Reads the STREAMINFO out of a container's FLAC codec-private data, or returns null when the
    /// bytes are not a shape this decoder recognises or describe a stream it cannot decode.
    /// </summary>
    /// <param name="codecPrivate">
    /// Either the bytes a .flac file starts with - the "fLaC" marker followed by the metadata blocks,
    /// STREAMINFO first and the last block flagged as last (Matroska's A_FLAC convention) - or a bare
    /// 34-byte STREAMINFO body.
    /// </param>
    /// <returns>The validated STREAMINFO, or null.</returns>
    internal static FlacStreamInfo ParseCodecPrivate(ReadOnlySpan<byte> codecPrivate)
    {
        ReadOnlySpan<byte> body;

        if (codecPrivate.Length >= 4 &&
            codecPrivate[0] == (byte)'f' && codecPrivate[1] == (byte)'L' &&
            codecPrivate[2] == (byte)'a' && codecPrivate[3] == (byte)'C')
        {
            body = FindStreamInfoBody(codecPrivate);
            if (body.IsEmpty) return null;
        }
        else if (codecPrivate.Length == FlacStreamInfo.BodyLength)
        {
            body = codecPrivate;
        }
        else
        {
            return null;
        }

        try
        {
            var bodyReader = new FlacBitReader();
            bodyReader.LoadPacket(body);
            var info = FlacStreamInfo.Read(bodyReader);
            info.Validate();

            // A file decoder can live with an unstated rate; a decoder that reports SampleRate to a
            // player cannot.
            return info.SampleRate > 0 ? info : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the metadata blocks after the "fLaC" marker and returns the STREAMINFO body, or an
    /// empty span when the blocks are malformed: STREAMINFO not first or not 34 bytes, a block that
    /// runs past the end of the data, or no block flagged as last.
    /// </summary>
    private static ReadOnlySpan<byte> FindStreamInfoBody(ReadOnlySpan<byte> codecPrivate)
    {
        var offset = 4;
        var streamInfoOffset = -1;

        while (true)
        {
            if (codecPrivate.Length - offset < MetadataBlockHeaderLength) return ReadOnlySpan<byte>.Empty;

            var isLast = (codecPrivate[offset] & 0x80) != 0;
            var blockType = codecPrivate[offset] & 0x7F;
            var length = (codecPrivate[offset + 1] << 16) | (codecPrivate[offset + 2] << 8) | codecPrivate[offset + 3];
            var blockStart = offset + MetadataBlockHeaderLength;

            if (blockType == InvalidBlockType) return ReadOnlySpan<byte>.Empty;
            if (codecPrivate.Length - blockStart < length) return ReadOnlySpan<byte>.Empty;

            if (streamInfoOffset < 0)
            {
                // STREAMINFO is mandatory and always the first block.
                if (blockType != StreamInfoBlockType || length != FlacStreamInfo.BodyLength)
                    return ReadOnlySpan<byte>.Empty;
                streamInfoOffset = blockStart;
            }

            offset = blockStart + length;
            if (isLast) break;
        }

        return codecPrivate.Slice(streamInfoOffset, FlacStreamInfo.BodyLength);
    }

    private static float ScaleFor(int bitsPerSample) => 1f / (1L << (bitsPerSample - 1));
}

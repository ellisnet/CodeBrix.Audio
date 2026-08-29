using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Vorbis;
using CodeBrix.Audio.Vorbis.Contracts;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Decodes Vorbis audio one container packet at a time, using the fully managed decoder in
/// <c>CodeBrix.Audio.Vorbis</c>.
/// </summary>
/// <remarks>
/// The setup headers arrive in the container's codec-private data rather than as Ogg pages, so they
/// are un-laced here and fed through the decoder's own header path; from then on each packet the
/// demultiplexer produces is decoded on its own.
/// </remarks>
internal sealed class VorbisPacketSoundDecoder : IPacketSoundDecoder
{
    private readonly object syncLock = new object();

    private StreamDecoder decoder;
    private MemoryDataPacket packet;
    private byte[] packetBuffer;
    private bool disposed;

    /// <summary>
    /// Creates a decoder from a container's Vorbis codec-private data.
    /// </summary>
    /// <param name="codecPrivate">
    /// The three Vorbis setup headers in the Xiph-laced form a media container carries: a count byte
    /// (number of packets minus one, so 2), the lengths of the first two headers as 255-continuation
    /// bytes, then the identification, comment and setup headers back to back.
    /// </param>
    /// <exception cref="ArgumentException">The codec-private data is not three Xiph-laced Vorbis headers.</exception>
    public VorbisPacketSoundDecoder(ReadOnlyMemory<byte> codecPrivate)
    {
        var headers = UnlaceHeaders(codecPrivate);
        if (headers == null)
        {
            throw new ArgumentException(
                "The Vorbis codec-private data is not three Xiph-laced setup headers.", nameof(codecPrivate));
        }

        var provider = new HeaderPacketProvider(headers);
        decoder = new StreamDecoder(provider);
        packet = new MemoryDataPacket(ReadOnlyMemory<byte>.Empty);
        packetBuffer = new byte[0];
    }

    /// <inheritdoc />
    public int Channels => decoder.Channels;

    /// <inheritdoc />
    public int SampleRate => decoder.SampleRate;

    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc />
    /// <remarks>
    /// Worked out from the block sizes in this stream's setup header: the most any packet can make
    /// final is a long block whose left neighbour is long and whose right neighbour is short.
    /// </remarks>
    public int MaxSamplesPerPacket => decoder.MaxPacketSampleCount * decoder.Channels;

    /// <inheritdoc />
    /// <remarks>Vorbis has no codec-level priming, so this is always 0.</remarks>
    public int PreSkipSamples => 0;

    /// <inheritdoc />
    public int DecodePacket(ReadOnlySpan<byte> packetData, Span<float> output)
    {
        lock (syncLock)
        {
            if (disposed) return 0;
            if (packetData.IsEmpty) return 0;

            if (packetBuffer.Length < packetData.Length)
            {
                packetBuffer = new byte[packetData.Length];
            }
            packetData.CopyTo(packetBuffer);
            packet.SetData(new ReadOnlyMemory<byte>(packetBuffer, 0, packetData.Length));

            return decoder.DecodeSinglePacket(packet, output);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (syncLock)
        {
            if (disposed) return;
            decoder.ResetOverlapState();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (syncLock)
        {
            if (disposed) return;
            disposed = true;
            decoder.Dispose();
            decoder = null;
            packet = null;
            packetBuffer = null;
        }
    }

    /// <summary>
    /// Splits Xiph-laced codec-private data into its three header packets, or returns null when the
    /// bytes are not that shape.
    /// </summary>
    private static IPacket[] UnlaceHeaders(ReadOnlyMemory<byte> codecPrivate)
    {
        var span = codecPrivate.Span;
        if (span.Length < 3) return null;

        // Byte 0 is the packet count minus one; Vorbis always carries exactly three headers.
        if (span[0] != 2) return null;

        var offset = 1;
        var lengths = new int[3];

        for (var i = 0; i < 2; i++)
        {
            var length = 0;
            while (true)
            {
                if (offset >= span.Length) return null;
                var value = span[offset++];
                length += value;
                if (value != 255) break;
            }
            lengths[i] = length;
        }

        var remaining = span.Length - offset;
        if (remaining < lengths[0] + lengths[1]) return null;
        lengths[2] = remaining - lengths[0] - lengths[1];
        if (lengths[0] == 0 || lengths[1] == 0 || lengths[2] == 0) return null;

        var headers = new List<IPacket>(3);
        foreach (var length in lengths)
        {
            headers.Add(new MemoryDataPacket(codecPrivate.Slice(offset, length)));
            offset += length;
        }

        return headers.ToArray();
    }
}

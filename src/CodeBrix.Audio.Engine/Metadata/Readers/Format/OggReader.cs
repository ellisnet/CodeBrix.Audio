using System.Text;
using CodeBrix.Audio.Engine.Metadata.Abstracts;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Engine.Metadata.Readers.Tags;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Metadata.Readers.Format;  //was previously: SoundFlow.Metadata.Readers.Format

internal class OggReader : BaseSoundFormatReader
{
    public override async Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options)
    {
        var info = new SoundFormatInfo
        {
            FormatName = "Ogg", 
            FormatIdentifier = "ogg", 
            IsLossless = false, 
            BitrateMode = BitrateMode.VBR
        };

        try
        {
            // The first page must contain an identification header for the codec.
            var page = await ReadNextPageAsync(stream).ConfigureAwait(false);
            if (page == null || page.Packets.Count == 0)
                return new HeaderNotFoundError("Ogg Page");

            var idPacket = page.Packets[0];

            // Encoder priming samples an Opus decoder discards; 0 for every other codec.
            var opusPreSkip = 0;

            // Determine the codec from the identification packet.
            if (idPacket is [0x01, _, _, _, _, _, _, ..] && Encoding.ASCII.GetString(idPacket, 1, 6) == "vorbis")
                ParseVorbisIdentificationHeader(idPacket, info);
            else if (idPacket.Length >= 19 && Encoding.ASCII.GetString(idPacket, 0, 8) == "OpusHead")
                opusPreSkip = ParseOpusIdentificationHeader(idPacket, info);
            else
                return new UnsupportedFormatError("Ogg stream is not a supported Vorbis or Opus stream.");

            // The second page should contain the comment header (tags).
            page = await ReadNextPageAsync(stream).ConfigureAwait(false);
            if (page is { Packets.Count: > 0 } && options.ReadTags)
            {
                var commentPacket = page.Packets[0];
                switch (info.CodecName)
                {
                    case "Vorbis" when commentPacket.Length > 7 && commentPacket[0] == 0x03:
                    {
                        using var memStream = new MemoryStream(commentPacket);
                        memStream.Position = 7; // Skip packet type and "vorbis"
                        var vorbisResult = new VorbisCommentReader().Read(memStream, memStream.Length - 7, options);
                        if(vorbisResult.IsFailure) return Result<SoundFormatInfo>.Fail(vorbisResult.Error!);
                        info.Tags = vorbisResult.Value;
                        break;
                    }
                    case "Opus" when commentPacket.Length > 8 &&
                                     Encoding.ASCII.GetString(commentPacket, 0, 8) == "OpusTags":
                    {
                        using var memStream = new MemoryStream(commentPacket);
                        memStream.Position = 8; // Skip "OpusTags"
                        var vorbisResult = new VorbisCommentReader().Read(memStream, memStream.Length - 8, options);
                        if(vorbisResult.IsFailure) return Result<SoundFormatInfo>.Fail(vorbisResult.Error!);
                        info.Tags = vorbisResult.Value;
                        break;
                    }
                }
            }

            // Duration Calculation
            //
            // This runs for BOTH accuracy settings, unlike the frame-based formats. An Ogg
            // stream carries no usable first-frame estimate, so honouring FastEstimate here
            // would mean reporting a duration of zero - which breaks any media transport bound
            // to it. Reading the granule position of the last page is a single 64 KB tail read,
            // so it satisfies "fast" as well as "accurate". It does need a seekable stream.
            if (stream.CanSeek)
            {
                var lastGranulePosition = await FindLastPageGranuleAsync(stream).ConfigureAwait(false);
                if (lastGranulePosition > 0)
                {
                    if (info.CodecName == "Opus")
                    {
                        // An Opus granule position is always on a 48 kHz clock, and it COUNTS THE
                        // PRE-SKIP - the priming samples the encoder needed and the decoder throws
                        // away. Subtract them or every file reports longer than it plays (a few
                        // milliseconds, which is enough to leave a transport hanging past the end).
                        var frames = lastGranulePosition - opusPreSkip;
                        if (frames > 0)
                            info.Duration = TimeSpan.FromSeconds(frames / 48000.0);
                    }
                    else if (info.SampleRate > 0)
                    {
                        // For Vorbis the granule position is the PCM sample number.
                        info.Duration = TimeSpan.FromSeconds(lastGranulePosition / (double)info.SampleRate);
                    }
                }
            }

            if (info.Duration.TotalSeconds > 0) info.Bitrate = (int)(stream.Length * 8 / info.Duration.TotalSeconds);
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("Ogg Page", "File is truncated or a page segment is incorrect.", ex);
        }
        
        return info;
    }

    private void ParseVorbisIdentificationHeader(byte[] packet, SoundFormatInfo info)
    {
        using var reader = new BinaryReader(new MemoryStream(packet));
        reader.BaseStream.Position = 7; // Skip packet type and "vorbis"
        reader.ReadUInt32(); // Version
        info.ChannelCount = reader.ReadByte();
        info.SampleRate = reader.ReadInt32();
        reader.ReadInt32(); // Max Bitrate
        var nominalBitrate = reader.ReadInt32();
        reader.ReadInt32(); // Min Bitrate
        info.Bitrate = nominalBitrate > 0 ? nominalBitrate : info.Bitrate;
        info.CodecName = "Vorbis";
    }

    /// <summary>
    /// Reads an OpusHead identification packet (RFC 7845 section 5.1) into <paramref name="info"/>.
    /// </summary>
    /// <returns>The pre-skip, in 48 kHz samples, for the duration calculation.</returns>
    private int ParseOpusIdentificationHeader(byte[] packet, SoundFormatInfo info)
    {
        // Packet starts with "OpusHead" (8 bytes), then version (1) and channel count (1).
        info.CodecName = "Opus";
        info.ChannelCount = packet[9];

        // An Opus stream ALWAYS decodes at 48 kHz, whatever it was encoded from. The 32-bit value
        // at offset 12 is the rate of the audio the ENCODER was handed - 16000 for a typical voice
        // note, and permitted to be 0 when unknown - which RFC 7845 marks informational and tells
        // implementations not to use for playback. Reporting it here would hand a decoder a rate
        // its own output does not have: the data providers build the decoder's target format from
        // this value, so a 16 kHz voice note would be resampled as though 48 kHz audio were 16 kHz.
        // Report the decode rate instead, which is also what ffprobe shows for an Opus stream.
        info.SampleRate = 48000;

        // Pre-skip: unsigned 16-bit little-endian at offset 10, on that same 48 kHz clock.
        return BitConverter.ToUInt16(packet, 10);
    }

    private async Task<OggPage?> ReadNextPageAsync(Stream stream)
    {
        var page = new OggPage();

        var fourByteBuffer = new byte[4];
        while (await stream.ReadAsync(fourByteBuffer.AsMemory(0, 1)).ConfigureAwait(false) > 0)
        {
            if (fourByteBuffer[0] == 'O')
                if (await stream.ReadAsync(fourByteBuffer.AsMemory(1, 3)).ConfigureAwait(false) == 3 &&
                    fourByteBuffer[1] == 'g' && fourByteBuffer[2] == 'g' && fourByteBuffer[3] == 'S')
                {
                    stream.Position -= 4;
                    break;
                }

            if (stream.Position >= stream.Length) return null;
        }

        var headerBytes = new byte[27];
        if (await stream.ReadAsync(headerBytes.AsMemory(0, 27)).ConfigureAwait(false) < 27) return null;

        page.GranulePosition = BitConverter.ToInt64(headerBytes, 6);
        int pageSegments = headerBytes[26];
        var segmentTable = new byte[pageSegments];
        await stream.ReadExactlyAsync(segmentTable, 0, pageSegments).ConfigureAwait(false);

        foreach (var segmentLength in segmentTable)
        {
            var packetBytes = new byte[segmentLength];
            await stream.ReadExactlyAsync(packetBytes, 0, segmentLength).ConfigureAwait(false);
            page.Packets.Add(packetBytes);
        }

        return page;
    }

    private async Task<long> FindLastPageGranuleAsync(Stream stream)
    {
        const int bufferSize = 65536;
        if (stream.Length < bufferSize)
            stream.Seek(0, SeekOrigin.Begin);
        else
            stream.Seek(-bufferSize, SeekOrigin.End);

        var buffer = new byte[bufferSize];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize)).ConfigureAwait(false);

        for (var i = bytesRead - 27; i >= 0; i--)
            if (buffer[i] == 'O' && buffer[i + 1] == 'g' && buffer[i + 2] == 'g' && buffer[i + 3] == 'S')
                if ((buffer[i + 5] & 0x04) != 0) // End of stream flag
                    return BitConverter.ToInt64(buffer, i + 6);

        return -1;
    }

    private class OggPage
    {
        public long GranulePosition { get; set; }
        public List<byte[]> Packets { get; } = [];
    }
}
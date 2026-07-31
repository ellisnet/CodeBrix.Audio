using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Vorbis;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Reads Ogg Vorbis (.ogg) audio from a file or stream and presents it as a repositionable
/// <see cref="WaveStream"/> of 32-bit IEEE float PCM. Decoding is performed entirely in managed
/// code, so no platform codec is required and behaviour is identical on Windows, macOS, and Linux.
/// </summary>
/// <remarks>
/// <para>
/// This is the Ogg Vorbis peer of <see cref="WaveFileReader"/> and <see cref="Mp3FileReader"/>:
/// same shape, same base class, and usable anywhere a <see cref="WaveStream"/> is. Unlike MP3,
/// a Vorbis stream records its exact length, so <see cref="WaveStream.TotalTime"/> is accurate
/// from the moment the file opens and seeking is sample-accurate rather than estimated.
/// </para>
/// <para>
/// Output is always 32-bit float, which is Vorbis's native sample format - no conversion or
/// requantisation happens on the way out.
/// </para>
/// <para>
/// One behaviour worth knowing when seeking: <see cref="Position"/> is exact, but seeking into
/// the middle of a Vorbis packet leaves the decoder without the preceding packet's overlap
/// history, so up to one block of audio (on the order of 2048 frames, or roughly 40 ms) after
/// the seek can differ from what a sequential read of the same region produces, before the two
/// converge exactly. For scrubbing a media transport this is not noticeable; for a game loop
/// point that must splice seamlessly, prefer the engine playback path, whose native decoder
/// reconstructs the overlap and matches immediately.
/// </para>
/// </remarks>
public class OggVorbisFileReader : WaveStream
{
    private readonly WaveFormat waveFormat;
    private readonly long length;
    private readonly bool ownInputStream;
    private readonly object lockObject = new object();

    private VorbisReader reader;
    private Stream sourceStream;
    private float[] conversionBuffer;

    /// <summary>
    /// Opens an Ogg Vorbis file for reading.
    /// </summary>
    /// <param name="fileName">The .ogg file to open.</param>
    public OggVorbisFileReader(string fileName)
        : this(File.OpenRead(fileName), true)
    {
    }

    /// <summary>
    /// Opens an Ogg Vorbis stream for reading. The caller keeps ownership of the stream.
    /// </summary>
    /// <param name="inputStream">A readable, seekable stream positioned at the start of an Ogg file.</param>
    public OggVorbisFileReader(Stream inputStream)
        : this(inputStream, false)
    {
    }

    private OggVorbisFileReader(Stream inputStream, bool ownInputStream)
    {
        if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));

        this.ownInputStream = ownInputStream;
        sourceStream = inputStream;

        try
        {
            // Ogg carries more than Vorbis. Identify the codec first so a .opus file (or Ogg FLAC)
            // fails saying what it actually is, rather than surfacing the decoder's internal
            // complaint about an unexpected bitstream.
            var codec = OggCodecSniffer.Identify(inputStream);
            if (codec != OggCodec.Vorbis && codec != OggCodec.NotOgg)
            {
                throw OggCodecSniffer.DescribeUndecodable(codec);
            }

            // closeOnDispose: false - this class decides the stream's fate, in line with the
            // other readers here, so that passing a stream in never takes it away from you.
            reader = new VorbisReader(inputStream, false);
        }
        catch (Exception)
        {
            if (ownInputStream) inputStream.Dispose();
            throw;
        }

        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        length = reader.TotalSamples * waveFormat.BlockAlign;
    }

    /// <summary>
    /// Gets the wave format of this stream: 32-bit IEEE float at the file's own sample rate and
    /// channel count.
    /// </summary>
    public override WaveFormat WaveFormat => waveFormat;

    /// <summary>
    /// Gets the length of the decoded audio in bytes. Exact, because a Vorbis stream records its
    /// total sample count.
    /// </summary>
    public override long Length => length;

    /// <summary>
    /// Gets or sets the current position within the decoded audio, in bytes. Setting it seeks the
    /// underlying decoder to the corresponding sample.
    /// </summary>
    public override long Position
    {
        get
        {
            lock (lockObject)
            {
                return reader.SamplePosition * waveFormat.BlockAlign;
            }
        }
        set
        {
            lock (lockObject)
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));

                var samplePosition = value / waveFormat.BlockAlign;
                if (samplePosition > reader.TotalSamples) samplePosition = reader.TotalSamples;

                reader.SeekTo(samplePosition);
            }
        }
    }

    /// <summary>
    /// Gets the vendor string the encoder wrote into the stream (for example "Xiph.Org libVorbis").
    /// </summary>
    public string EncoderVendor => reader.Tags.EncoderVendor;

    /// <summary>
    /// Gets the Vorbis comments (tags) carried by the stream, keyed by the uppercase field name -
    /// TITLE, ARTIST, ALBUM, and so on. A field may legitimately appear more than once, which is
    /// why each maps to a list.
    /// </summary>
    /// <remarks>
    /// This is the Ogg Vorbis counterpart to reading an <see cref="Id3v2Tag"/> from an MP3.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tags => reader.Tags.All;

    /// <summary>
    /// Reads decoded PCM into the supplied buffer.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">The byte offset in <paramref name="buffer"/> to start writing at.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read; 0 at the end of the stream.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        return Read(new Span<byte>(buffer, offset, count));
    }

    /// <summary>
    /// Reads decoded PCM into the supplied buffer.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>The number of bytes read; 0 at the end of the stream.</returns>
    public override int Read(Span<byte> buffer)
    {
        lock (lockObject)
        {
            // Read whole samples only: a caller asking for a byte count that is not a multiple of
            // four would otherwise leave a partial float behind and desynchronise every later read.
            var samplesWanted = buffer.Length / 4;
            if (samplesWanted == 0) return 0;

            if (conversionBuffer == null || conversionBuffer.Length < samplesWanted)
                conversionBuffer = new float[samplesWanted];

            var samplesRead = reader.ReadSamples(new Span<float>(conversionBuffer, 0, samplesWanted));
            if (samplesRead <= 0) return 0;

            MemoryMarshal.AsBytes(new ReadOnlySpan<float>(conversionBuffer, 0, samplesRead))
                .CopyTo(buffer);

            return samplesRead * 4;
        }
    }

    /// <summary>
    /// Releases the decoder and, when this reader opened the file itself, the underlying stream.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="IDisposable.Dispose"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (lockObject)
            {
                if (reader != null)
                {
                    reader.Dispose();
                    reader = null;
                }

                if (ownInputStream && sourceStream != null)
                {
                    sourceStream.Dispose();
                }

                sourceStream = null;
                conversionBuffer = null;
            }
        }

        base.Dispose(disposing);
    }
}

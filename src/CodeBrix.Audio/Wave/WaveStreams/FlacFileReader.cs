using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Flac;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Reads FLAC (.flac) audio from a file or stream and presents it as a repositionable
/// <see cref="WaveStream"/> of PCM. Decoding is performed entirely in managed code, so no
/// platform codec is required and behaviour is identical on Windows, macOS, and Linux.
/// </summary>
/// <remarks>
/// <para>
/// This is the FLAC peer of <see cref="WaveFileReader"/>, <see cref="Mp3FileReader"/> and
/// <see cref="OggVorbisFileReader"/>: same shape, same base class, and usable anywhere a
/// <see cref="WaveStream"/> is.
/// </para>
/// <para>
/// FLAC is lossless, so the PCM this produces is bit-for-bit what was encoded. Samples are
/// delivered in the smallest standard container that holds the stream's bit depth: 16-bit for
/// depths up to 16, 24-bit for 17 to 24, and 32-bit above that. Depths that are not a whole
/// number of bytes (FLAC permits 12 and 20, among others) are left-shifted into that container,
/// which is what other FLAC-to-WAV converters do.
/// </para>
/// <para>
/// Both duration and seeking are exact. The total sample count is stated in the stream's
/// STREAMINFO block, and every frame header records the sample it starts at, so no estimation is
/// involved in either.
/// </para>
/// </remarks>
public class FlacFileReader : WaveStream
{
    private readonly WaveFormat waveFormat;
    private readonly long length;
    private readonly int sourceBitsPerSample;
    private readonly int containerBitsPerSample;
    private readonly int sampleShift;
    private readonly bool ownInputStream;
    private readonly object lockObject = new object();

    private FlacDecoder decoder;
    private Stream sourceStream;
    private int[] sampleBuffer;

    /// <summary>
    /// Opens a FLAC file for reading.
    /// </summary>
    /// <param name="fileName">The .flac file to open.</param>
    public FlacFileReader(string fileName)
        : this(File.OpenRead(fileName), true)
    {
    }

    /// <summary>
    /// Opens a FLAC stream for reading. The caller keeps ownership of the stream.
    /// </summary>
    /// <param name="inputStream">A readable, seekable stream positioned at the start of a FLAC file.</param>
    public FlacFileReader(Stream inputStream)
        : this(inputStream, false)
    {
    }

    private FlacFileReader(Stream inputStream, bool ownInputStream)
    {
        if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));

        this.ownInputStream = ownInputStream;
        sourceStream = inputStream;

        try
        {
            decoder = new FlacDecoder(inputStream);
        }
        catch (Exception)
        {
            if (ownInputStream) inputStream.Dispose();
            throw;
        }

        var info = decoder.StreamInfo;
        sourceBitsPerSample = info.BitsPerSample;
        containerBitsPerSample = sourceBitsPerSample <= 16 ? 16 : sourceBitsPerSample <= 24 ? 24 : 32;
        sampleShift = containerBitsPerSample - sourceBitsPerSample;

        waveFormat = new WaveFormat(info.SampleRate, containerBitsPerSample, info.Channels);
        length = info.TotalSamples * waveFormat.BlockAlign;
    }

    /// <summary>
    /// Gets the wave format of this stream: PCM at the file's sample rate and channel count, in
    /// the smallest standard container for its bit depth.
    /// </summary>
    public override WaveFormat WaveFormat => waveFormat;

    /// <summary>
    /// Gets the length of the decoded audio in bytes, or 0 when the encoder did not record a
    /// total sample count (which happens when FLAC is produced by a pipe).
    /// </summary>
    public override long Length => length;

    /// <summary>
    /// Gets or sets the current position within the decoded audio, in bytes.
    /// </summary>
    public override long Position
    {
        get
        {
            lock (lockObject)
            {
                return decoder.SamplePosition * waveFormat.BlockAlign;
            }
        }
        set
        {
            lock (lockObject)
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));

                var frame = value / waveFormat.BlockAlign;
                var total = decoder.StreamInfo.TotalSamples;
                if (total > 0 && frame > total) frame = total;

                decoder.SeekTo(frame);
            }
        }
    }

    /// <summary>Gets the bit depth the audio was encoded at, before container widening.</summary>
    public int SourceBitsPerSample => sourceBitsPerSample;

    /// <summary>
    /// Gets the Vorbis comments (tags) carried by the stream, keyed by the uppercase field name -
    /// TITLE, ARTIST, ALBUM, and so on. A field may legitimately appear more than once, which is
    /// why each maps to a list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tags => decoder.Tags;

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
            var bytesPerSample = containerBitsPerSample / 8;
            var samplesWanted = buffer.Length / bytesPerSample;

            // Whole frames only: handing back a partial frame would desynchronise the channels
            // for every later read.
            samplesWanted -= samplesWanted % waveFormat.Channels;
            if (samplesWanted == 0) return 0;

            if (sampleBuffer == null || sampleBuffer.Length < samplesWanted)
                sampleBuffer = new int[samplesWanted];

            var samplesRead = decoder.ReadSamples(new Span<int>(sampleBuffer, 0, samplesWanted));
            if (samplesRead <= 0) return 0;

            var position = 0;
            for (var i = 0; i < samplesRead; i++)
            {
                var value = sampleBuffer[i] << sampleShift;

                switch (bytesPerSample)
                {
                    case 2:
                        buffer[position++] = (byte)value;
                        buffer[position++] = (byte)(value >> 8);
                        break;
                    case 3:
                        buffer[position++] = (byte)value;
                        buffer[position++] = (byte)(value >> 8);
                        buffer[position++] = (byte)(value >> 16);
                        break;
                    default:
                        buffer[position++] = (byte)value;
                        buffer[position++] = (byte)(value >> 8);
                        buffer[position++] = (byte)(value >> 16);
                        buffer[position++] = (byte)(value >> 24);
                        break;
                }
            }

            return position;
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
                if (decoder != null)
                {
                    decoder.Dispose();
                    decoder = null;
                }

                if (ownInputStream && sourceStream != null)
                {
                    sourceStream.Dispose();
                }

                sourceStream = null;
                sampleBuffer = null;
            }
        }

        base.Dispose(disposing);
    }
}

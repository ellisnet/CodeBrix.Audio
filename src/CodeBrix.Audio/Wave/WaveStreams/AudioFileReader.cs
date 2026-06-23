using System;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Wave.SampleProviders;

namespace CodeBrix.Audio.Wave; //was previously: NAudio.Wave;

/// <summary>
/// Simplifies opening a WAV or MP3 audio file: pass in the file name and the
/// reader opens it and sets up a conversion path that yields 32-bit IEEE float
/// PCM samples. It exposes a volume property and implements both
/// <see cref="WaveStream"/> and <see cref="ISampleProvider"/>.
/// </summary>
/// <remarks>
/// Only WAV (PCM or IEEE float) and MP3 sources are supported. WAV files using
/// other encodings (for example A-law or mu-law) are not converted, because
/// CodeBrix.Audio performs no platform-specific codec conversion.
/// </remarks>
public class AudioFileReader : WaveStream, ISampleProvider
{
    private WaveStream readerStream; // the waveStream which we will use for all positioning
    private readonly SampleChannel sampleChannel; // sample provider that gives us most stuff we need
    private readonly int destBytesPerSample;
    private readonly int sourceBytesPerSample;
    private readonly long length;
    private readonly object lockObject;

    /// <summary>
    /// Initializes a new instance of <see cref="AudioFileReader"/>.
    /// </summary>
    /// <param name="fileName">The WAV or MP3 file to open.</param>
    public AudioFileReader(string fileName)
    {
        lockObject = new object();
        FileName = fileName;
        CreateReaderStream(fileName);
        sourceBytesPerSample = (readerStream.WaveFormat.BitsPerSample / 8) * readerStream.WaveFormat.Channels;
        sampleChannel = new SampleChannel(readerStream, false);
        destBytesPerSample = 4 * sampleChannel.WaveFormat.Channels;
        length = SourceToDest(readerStream.Length);
    }

    /// <summary>
    /// Creates the reader stream, supporting WAV (PCM / IEEE float) and MP3, and
    /// ensuring the resulting stream is in a PCM-compatible format.
    /// </summary>
    /// <param name="fileName">The file to open.</param>
    private void CreateReaderStream(string fileName)
    {
        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new WaveFileReader(fileName);
            if (readerStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm && readerStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                throw new InvalidOperationException(
                    "Only PCM and IEEE-float WAV files are supported; this file uses " +
                    $"{readerStream.WaveFormat.Encoding} encoding, which CodeBrix.Audio does not convert.");
            }
        }
        else if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new Mp3FileReader(fileName);
        }
        else
        {
            throw new InvalidOperationException(
                "Unsupported file format. AudioFileReader supports only .wav and .mp3 files.");
        }
    }

    /// <summary>
    /// The name of the file opened by this reader.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the wave format of this stream (32-bit IEEE float PCM).
    /// </summary>
    public override WaveFormat WaveFormat => sampleChannel.WaveFormat;

    /// <summary>
    /// Gets the length of this stream in bytes.
    /// </summary>
    public override long Length => length;

    /// <summary>
    /// Gets or sets the position of this stream in bytes.
    /// </summary>
    public override long Position
    {
        get { return SourceToDest(readerStream.Position); }
        set { lock (lockObject) { readerStream.Position = DestToSource(value); } }
    }

    /// <summary>
    /// Reads from this wave stream into a span of bytes.
    /// </summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(Span<byte> buffer)
    {
        var floatSpan = MemoryMarshal.Cast<byte, float>(buffer);
        int samplesRead = Read(floatSpan);
        return samplesRead * 4;
    }

    /// <summary>
    /// Reads from this wave stream into a byte array.
    /// </summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="offset">The offset into the buffer at which to begin writing.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <summary>
    /// Reads audio samples from this file reader into a span of floats.
    /// </summary>
    /// <param name="buffer">The buffer to read samples into.</param>
    /// <returns>The number of samples read.</returns>
    public int Read(Span<float> buffer)
    {
        lock (lockObject)
        {
            return sampleChannel.Read(buffer);
        }
    }

    /// <summary>
    /// Gets or sets the volume of this reader. 1.0f is full (unaltered) volume.
    /// </summary>
    public float Volume
    {
        get { return sampleChannel.Volume; }
        set { sampleChannel.Volume = value; }
    }

    /// <summary>
    /// Helper to convert source bytes to destination bytes.
    /// </summary>
    private long SourceToDest(long sourceBytes)
    {
        return destBytesPerSample * (sourceBytes / sourceBytesPerSample);
    }

    /// <summary>
    /// Helper to convert destination bytes to source bytes.
    /// </summary>
    private long DestToSource(long destBytes)
    {
        return sourceBytesPerSample * (destBytes / destBytesPerSample);
    }

    /// <summary>
    /// Releases the resources used by this reader.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="System.IDisposable.Dispose"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (readerStream != null)
            {
                readerStream.Dispose();
                readerStream = null;
            }
        }
        base.Dispose(disposing);
    }
}

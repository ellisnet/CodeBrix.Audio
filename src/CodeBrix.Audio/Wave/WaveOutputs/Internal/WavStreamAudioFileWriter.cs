using System;
using System.IO;
using CodeBrix.Audio.Utils;

namespace CodeBrix.Audio.Wave.Internal;

/// <summary>
/// The <see cref="IAudioFileWriter"/> seam over <see cref="WaveFileWriter"/>, which is a
/// <see cref="Stream"/> and predates the seam by a long way.
/// </summary>
/// <remarks>
/// <see cref="WaveFileWriter"/> always disposes the stream it was handed and patches its header on
/// the way out, so the stream is wrapped in an <see cref="IgnoreDisposeStream"/> here: finishing
/// the WAV file must not close a stream the caller still owns.
/// </remarks>
internal sealed class WavStreamAudioFileWriter : IAudioFileWriter
{
    private readonly WaveFormat format;
    private WaveFileWriter writer;
    private long samplesWritten;
    private bool finished;

    internal WavStreamAudioFileWriter(Stream stream, WaveFormat format)
    {
        this.format = format;
        writer = new WaveFileWriter(new IgnoreDisposeStream(stream), format);
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat => format;

    /// <inheritdoc />
    public long SamplesWritten => samplesWritten;

    /// <inheritdoc />
    public void Write(float[] samples, int offset, int count)
    {
        AudioFileWriterGuards.CheckBuffer(samples, offset, count);
        CheckOpen();

        writer.WriteSamples(samples, offset, count);
        samplesWritten += count;
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<float> samples)
    {
        CheckOpen();

        foreach (var sample in samples)
        {
            writer.WriteSample(sample);
        }

        samplesWritten += samples.Length;
    }

    /// <inheritdoc />
    public void Finish()
    {
        if (finished)
        {
            return;
        }

        finished = true;

        // Disposing the WaveFileWriter is what patches the RIFF and data chunk sizes. The stream
        // underneath survives it.
        writer.Dispose();
        writer = null;
    }

    /// <summary>Finishes the file if it is not finished already.</summary>
    public void Dispose() => Finish();

    private void CheckOpen()
    {
        if (finished)
        {
            throw new InvalidOperationException(
                "This WAV file has been finished; nothing more can be written to it.");
        }
    }
}

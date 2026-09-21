using System;
using System.IO;
using CodeBrix.Audio.Utils;

namespace CodeBrix.Audio.Wave.Internal;

/// <summary>
/// The <see cref="IAudioFileWriter"/> seam over <see cref="AiffFileWriter"/>, which is a
/// <see cref="Stream"/> and predates the seam by a long way.
/// </summary>
/// <remarks>
/// <see cref="AiffFileWriter"/> disposes the stream it was handed once it has patched the FORM,
/// COMM and SSND chunk sizes, so the stream is wrapped in an <see cref="IgnoreDisposeStream"/>
/// here: finishing the AIFF file must not close a stream the caller still owns.
/// </remarks>
internal sealed class AiffStreamAudioFileWriter : IAudioFileWriter
{
    private readonly WaveFormat format;
    private AiffFileWriter writer;
    private long samplesWritten;
    private bool finished;

    internal AiffStreamAudioFileWriter(Stream stream, WaveFormat format)
    {
        this.format = format;
        writer = new AiffFileWriter(new IgnoreDisposeStream(stream), format);
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

        // Disposing the AiffFileWriter is what patches the chunk sizes. The stream underneath
        // survives it.
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
                "This AIFF file has been finished; nothing more can be written to it.");
        }
    }
}

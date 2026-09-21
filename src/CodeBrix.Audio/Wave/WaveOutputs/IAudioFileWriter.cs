using System;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Writes interleaved 32-bit float audio into a file format - the write-side mirror of the reading
/// that <see cref="AudioFileReaderRegistry"/> dispatches.
/// </summary>
/// <remarks>
/// <para>
/// Samples go in as floats, interleaved by frame (left, right, left, right, ... for stereo),
/// nominally between -1.0 and 1.0. What the format stores them AS - 32-bit float, 16-bit PCM,
/// a compressed payload - is the writer's business and is settled by the
/// <see cref="WaveFormat"/> it was created with.
/// </para>
/// <para>
/// THE WRITER DOES NOT OWN THE STREAM. It never closes the stream it was handed; the caller opened
/// it and the caller closes it. What <see cref="Finish"/> and <see cref="IDisposable.Dispose"/> do
/// is finish the FILE - flush the last samples and, for a format that needs it, go back and patch
/// the header with the length. A file whose writer was never finished is very likely unreadable,
/// so dispose the writer before the stream.
/// </para>
/// <para>
/// Implementations are not thread-safe: one writer, one producing thread.
/// </para>
/// </remarks>
public interface IAudioFileWriter : IDisposable
{
    /// <summary>The sample rate, channel count and stored sample format of the file being written.</summary>
    WaveFormat WaveFormat { get; }

    /// <summary>How many sample values have been written so far, counting every channel.</summary>
    long SamplesWritten { get; }

    /// <summary>Writes interleaved float samples.</summary>
    /// <param name="samples">The buffer holding the samples.</param>
    /// <param name="offset">The index of the first sample to write.</param>
    /// <param name="count">How many samples to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="offset"/> or <paramref name="count"/> falls outside the buffer.
    /// </exception>
    /// <exception cref="InvalidOperationException">The file has already been finished.</exception>
    void Write(float[] samples, int offset, int count);

    /// <summary>Writes interleaved float samples.</summary>
    /// <param name="samples">The samples to write.</param>
    /// <exception cref="InvalidOperationException">The file has already been finished.</exception>
    void Write(ReadOnlySpan<float> samples);

    /// <summary>
    /// Finishes the file: flushes everything written and patches whatever the format records only
    /// once the length is known.
    /// </summary>
    /// <remarks>
    /// Calling this twice is a no-op, and disposing a writer that was never finished finishes it -
    /// so the ordinary <c>using</c> shape is correct on its own. Writing after this throws.
    /// </remarks>
    void Finish();
}

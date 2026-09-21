using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// An audio writer for a format that is written strictly forwards, so it needs no seeking: the
/// shape a future Ogg or Opus writer has, and the proof that the seam takes a third format from
/// outside this library.
/// </summary>
/// <remarks>
/// It writes the samples out as raw little-endian floats with no header at all. Nothing reads them
/// back; what is being tested is the seam, not an encoder.
/// </remarks>
internal sealed class ForwardOnlyWriterFactory : IAudioFileWriterFactory
{
    private readonly string[] extensions;

    /// <summary>Creates the factory under the ".fake" extension.</summary>
    public ForwardOnlyWriterFactory() : this([".fake"])
    {
    }

    /// <summary>Creates the factory under the extensions given, for the registry's own checks.</summary>
    /// <param name="extensions">The extensions to declare.</param>
    public ForwardOnlyWriterFactory(string[] extensions) => this.extensions = extensions;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions => extensions;

    /// <inheritdoc />
    public bool RequiresSeekableStream => false;

    /// <inheritdoc />
    public WaveFormat DefaultFormat(int sampleRate, int channels) =>
        WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

    /// <inheritdoc />
    public IAudioFileWriter Create(Stream stream, WaveFormat format) =>
        new ForwardOnlyWriter(stream, format);

    private sealed class ForwardOnlyWriter : IAudioFileWriter
    {
        private readonly Stream stream;
        private bool finished;

        internal ForwardOnlyWriter(Stream stream, WaveFormat format)
        {
            this.stream = stream;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public long SamplesWritten { get; private set; }

        public void Write(float[] samples, int offset, int count) =>
            Write(samples.AsSpan(offset, count));

        public void Write(ReadOnlySpan<float> samples)
        {
            if (finished)
            {
                throw new InvalidOperationException("This file has been finished.");
            }

            var buffer = new byte[4];

            foreach (var sample in samples)
            {
                BitConverter.TryWriteBytes(buffer, sample);
                stream.Write(buffer, 0, buffer.Length);
            }

            SamplesWritten += samples.Length;
        }

        public void Finish()
        {
            if (finished) return;

            finished = true;
            stream.Flush();
        }

        public void Dispose() => Finish();
    }
}

/// <summary>A writable stream that cannot seek, for the tests about formats that need to.</summary>
internal sealed class ForwardOnlyStream : Stream
{
    private long bytesWritten;

    /// <summary>How many bytes have been written to this stream.</summary>
    public long BytesWritten => bytesWritten;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => bytesWritten;

    /// <inheritdoc />
    public override long Position
    {
        get => bytesWritten;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => bytesWritten += count;

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => bytesWritten += buffer.Length;
}

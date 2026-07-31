using System;
using System.IO;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// A reader paired with the file stream it was built over, so that disposing the pair closes the
/// file. Returned by <see cref="AudioFileReaderRegistry.OpenFile"/>.
/// </summary>
/// <remarks>
/// The registry's factory contract (<see cref="AudioFileReaderRegistry.Register"/>) hands a reader a
/// stream positioned at the start of the file; readers built that way do not own the stream, which
/// is right for callers who supplied their own. When the registry opened the file itself, ownership
/// has to live somewhere, and this is it. Use <see cref="Reader"/> to reach the underlying reader
/// when its concrete type matters (for example a <see cref="WaveFileReader"/> and its
/// <see cref="WaveFileReader.Chunks"/>).
/// </remarks>
public sealed class FileOwningWaveStream : WaveStream
{
    private readonly Stream fileStream;

    /// <summary>
    /// Initializes a new instance of <see cref="FileOwningWaveStream"/>.
    /// </summary>
    /// <param name="reader">The reader built over <paramref name="fileStream"/>.</param>
    /// <param name="fileStream">The file stream to close when this instance is disposed.</param>
    internal FileOwningWaveStream(WaveStream reader, Stream fileStream)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.fileStream = fileStream ?? throw new ArgumentNullException(nameof(fileStream));
    }

    /// <summary>
    /// The reader this instance wraps.
    /// </summary>
    public WaveStream Reader { get; }

    /// <inheritdoc/>
    public override WaveFormat WaveFormat => Reader.WaveFormat;

    /// <inheritdoc/>
    public override long Length => Reader.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => Reader.Position;
        set => Reader.Position = value;
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => Reader.Read(buffer);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int BlockAlign => Reader.BlockAlign;

    /// <inheritdoc/>
    public override TimeSpan CurrentTime
    {
        get => Reader.CurrentTime;
        set => Reader.CurrentTime = value;
    }

    /// <inheritdoc/>
    public override TimeSpan TotalTime => Reader.TotalTime;

    /// <inheritdoc/>
    public override bool HasData(int count) => Reader.HasData(count);

    /// <inheritdoc/>
    public override bool CanSeek => Reader.CanSeek;

    /// <summary>
    /// Disposes the reader and then the file stream underneath it.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="IDisposable.Dispose"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The reader goes first: it may still read from the file while shutting down, and a
            // reader that does happen to own the stream must not find it already closed.
            Reader.Dispose();
            fileStream.Dispose();
        }

        base.Dispose(disposing);
    }
}

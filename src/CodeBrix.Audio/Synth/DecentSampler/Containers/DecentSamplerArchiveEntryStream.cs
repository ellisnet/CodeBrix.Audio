using System;
using System.IO;
using System.IO.Compression;

namespace CodeBrix.Audio.Synth.DecentSampler.Containers;

// A seekable view over one zip entry that never holds the whole entry in memory.
//
// A deflated entry is forward-only, so the streaming source cannot simply seek it. This stream keeps
// the illusion: reading forward is a plain read, seeking forward skips through the decompressor, and
// seeking BACKWARDS re-opens the entry from the beginning and skips to the wanted offset. That is what
// makes archive streaming possible at all - the alternative is decompressing the entry into RAM, which
// is what DecentSamplerArchiveContainer.OpenFile does and what streaming exists to avoid.
//
// It opens the archive FILE for itself rather than sharing the container's ZipArchive, because a zip
// entry stream and its archive are not safe to read from two threads at once and a streaming source is
// read by a background reader while the rest of the library is still loading.
//
// The cost model to know: a backward seek is O(offset) decompression work. Streaming playback reads
// forward almost always; the one backward move is a loop wrap, and the preload head usually covers the
// loop start so even that costs nothing. A library shipped as a folder streams strictly better than the
// same library shipped as a .dslibrary.
internal sealed class DecentSamplerArchiveEntryStream : Stream
{
    private const int SkipBufferSize = 64 * 1024;

    private readonly string _archivePath;
    private readonly string _entryName;
    private readonly long _length;

    private FileStream _file;
    private ZipArchive _archive;
    private Stream _inner;
    private long _position;
    private byte[] _skipBuffer;
    private bool _disposed;

    public DecentSamplerArchiveEntryStream(string archivePath, string entryName)
    {
        if (archivePath == null)
        {
            throw new ArgumentNullException(nameof(archivePath));
        }

        if (entryName == null)
        {
            throw new ArgumentNullException(nameof(entryName));
        }

        _archivePath = archivePath;
        _entryName = entryName;

        _file = File.OpenRead(archivePath);

        try
        {
            _archive = new ZipArchive(_file, ZipArchiveMode.Read, leaveOpen: true);
            var entry = _archive.GetEntry(entryName) ??
                        throw new FileNotFoundException("The archive has no such entry.", entryName);

            _length = entry.Length;
            _inner = entry.Open();
        }
        catch (Exception)
        {
            _archive?.Dispose();
            _file.Dispose();
            _archive = null;
            _file = null;
            throw;
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var total = 0;

        while (total < buffer.Length)
        {
            var read = _inner.Read(buffer.Slice(total));
            if (read <= 0)
            {
                break;
            }

            total += read;
            _position += read;
        }

        return total;
    }

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 1 ? one[0] : -1;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the entry.");
        }

        if (target < _position)
        {
            // Backwards: the decompressor cannot rewind, so start it again.
            ReopenEntry();
        }

        SkipForwardTo(target);
        return _position;
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("An archive entry is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("An archive entry is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            _inner?.Dispose();
            _archive?.Dispose();
            _file?.Dispose();

            _inner = null;
            _archive = null;
            _file = null;
        }

        base.Dispose(disposing);
    }

    private void ReopenEntry()
    {
        _inner.Dispose();

        var entry = _archive.GetEntry(_entryName) ??
                    throw new FileNotFoundException("The archive has no such entry.", _entryName);

        _inner = entry.Open();
        _position = 0;
    }

    private void SkipForwardTo(long target)
    {
        if (target <= _position)
        {
            return;
        }

        _skipBuffer ??= new byte[SkipBufferSize];

        while (_position < target)
        {
            var want = (int)Math.Min(_skipBuffer.Length, target - _position);
            var read = _inner.Read(_skipBuffer, 0, want);

            if (read <= 0)
            {
                // Past the end: report the position the caller asked for, as a file stream would.
                _position = target;
                return;
            }

            _position += read;
        }
    }

    // The archive this entry lives in, for problem messages.
    public string ArchivePath => _archivePath;
}

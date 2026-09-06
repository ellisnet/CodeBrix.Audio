using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// A store over a "&lt;Title&gt; Stems.zip" read in place. Entries are decompressed on demand -
/// never at load - either into a cache folder or into memory, because the audio readers need a
/// seekable stream and a compressed entry is not one.
/// </summary>
/// <remarks>
/// The archive is opened for the duration of one read and closed again, so no file handle is held
/// between calls and two threads can materialise different entries at the same time. Extraction to
/// the cache folder writes to a temporary name and moves it into place, so a half-written file is
/// never mistaken for a finished one - including by another process using the same cache.
/// </remarks>
internal sealed class SunoZipStore : SunoContentStore
{
    private readonly object _gate = new object();
    private readonly string _zipPath;
    private readonly string[] _keys;
    private readonly Dictionary<string, long> _lengths;
    private readonly Dictionary<string, byte[]> _inMemory;
    private readonly SunoZipExtraction _extraction;
    private readonly string _cacheFolder;

    internal SunoZipStore(string zipPath, IReadOnlyList<ZipArchiveEntry> entries, SunoZipExtraction extraction,
        string cacheFolder)
    {
        _zipPath = zipPath;
        _extraction = extraction;
        _cacheFolder = extraction == SunoZipExtraction.CacheFolder ? cacheFolder : null;
        _keys = new string[entries.Count];
        _lengths = new Dictionary<string, long>(entries.Count, StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            _keys[i] = entries[i].FullName;
            _lengths[entries[i].FullName] = entries[i].Length;
        }

        _inMemory = extraction == SunoZipExtraction.Memory
            ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
            : null;
    }

    internal override SunoSourceKind Kind => SunoSourceKind.ZipArchive;

    internal override string CacheFolder => _cacheFolder;

    internal override bool CanProvideFilePath => _extraction == SunoZipExtraction.CacheFolder;

    internal override string[] Keys => _keys;

    internal override long GetLength(string key) => _lengths.TryGetValue(key, out var length) ? length : 0;

    internal override Stream OpenSequential(string key)
    {
        if (_inMemory != null)
        {
            return new MemoryStream(Materialise(key), false);
        }

        // A ZipArchive owns nothing but the FileStream underneath it, and disposing the entry
        // stream alone would leave both open - so the wrapper below closes all three together.
        var file = File.OpenRead(_zipPath);
        try
        {
            var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            try
            {
                var entry = archive.GetEntry(key);
                if (entry == null)
                {
                    throw new FileNotFoundException($"The archive no longer holds an entry named '{key}'.", _zipPath);
                }

                return new EntryStream(entry.Open(), archive, file);
            }
            catch (Exception)
            {
                archive.Dispose();
                throw;
            }
        }
        catch (Exception)
        {
            file.Dispose();
            throw;
        }
    }

    internal override Stream OpenSeekable(string key)
    {
        if (_inMemory != null)
        {
            return new MemoryStream(Materialise(key), false);
        }

        return File.OpenRead(GetFilePath(key));
    }

    internal override string GetFilePath(string key)
    {
        if (_extraction != SunoZipExtraction.CacheFolder)
        {
            throw new InvalidOperationException(
                "This song was loaded with SunoZipExtraction.Memory, so its entries have no file path. " +
                "Open the stem's stream instead, or load with SunoZipExtraction.CacheFolder.");
        }

        var expected = GetLength(key);
        var target = Path.Combine(_cacheFolder, CacheFileName(key));

        lock (_gate)
        {
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == expected)
            {
                return target;
            }

            Directory.CreateDirectory(_cacheFolder);
            var temporary = target + ".part-" + Guid.NewGuid().ToString("N");

            using (var source = OpenSequential(key))
            using (var destination = File.Create(temporary))
            {
                source.CopyTo(destination);
            }

            try
            {
                File.Move(temporary, target, true);
            }
            catch (IOException)
            {
                // Another process finished the same entry first; its copy is as good as this one.
                File.Delete(temporary);
            }

            return target;
        }
    }

    internal override byte[] ReadAllBytes(string key) => _inMemory != null ? Materialise(key) : base.ReadAllBytes(key);

    internal override void ClearCache()
    {
        lock (_gate)
        {
            _inMemory?.Clear();

            if (_cacheFolder == null || !Directory.Exists(_cacheFolder))
            {
                return;
            }

            // Only this archive's own entries are deleted: the cache folder may have been named by
            // the caller, and deleting it wholesale would take whatever else is in it.
            foreach (var key in Keys)
            {
                var target = Path.Combine(_cacheFolder, CacheFileName(key));
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }

            try
            {
                Directory.Delete(_cacheFolder);
            }
            catch (IOException)
            {
                // Something else is in the folder; leaving it is the right answer.
            }
        }
    }

    /// <summary>
    /// The cache folder for a zip, keyed by its path, size and last-write time, so that the same
    /// download reuses its extracted files and an edited or replaced one does not.
    /// </summary>
    internal static string CacheFolderFor(string zipPath, string root, string title)
    {
        var info = new FileInfo(zipPath);
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        return Path.Combine(root, $"{SanitiseFolderName(title)}-{hash}");
    }

    private byte[] Materialise(string key)
    {
        lock (_gate)
        {
            if (_inMemory.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        byte[] bytes;
        using (var file = File.OpenRead(_zipPath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
        {
            var entry = archive.GetEntry(key);
            if (entry == null)
            {
                throw new FileNotFoundException($"The archive no longer holds an entry named '{key}'.", _zipPath);
            }

            var length = entry.Length;
            using var source = entry.Open();
            using var buffer = length > 0 && length <= int.MaxValue
                ? new MemoryStream((int)length)
                : new MemoryStream();
            source.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        lock (_gate)
        {
            if (_inMemory.TryGetValue(key, out var cached))
            {
                return cached;
            }

            _inMemory[key] = bytes;
            return bytes;
        }
    }

    private static string CacheFileName(string entryName)
    {
        var builder = new StringBuilder(entryName.Length);
        foreach (var c in entryName)
        {
            builder.Append(c == '/' || c == '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }

    private static string SanitiseFolderName(string title)
    {
        var builder = new StringBuilder(title == null ? 0 : title.Length);
        if (title != null)
        {
            foreach (var c in title)
            {
                if (char.IsLetterOrDigit(c) && c < 0x80)
                {
                    builder.Append(c);
                }
                else if (c == ' ' || c == '-' || c == '_')
                {
                    builder.Append('-');
                }
            }
        }

        var name = builder.ToString().Trim('-');
        if (name.Length > 48)
        {
            name = name.Substring(0, 48).TrimEnd('-');
        }

        return name.Length == 0 ? "Song" : name;
    }

    /// <summary>
    /// One entry's decompressed bytes, with the archive and the file underneath it tied to its
    /// lifetime so that disposing the stream closes everything.
    /// </summary>
    private sealed class EntryStream : Stream
    {
        private readonly Stream _inner;
        private readonly ZipArchive _archive;
        private readonly Stream _file;
        private bool _disposed;

        internal EntryStream(Stream inner, ZipArchive archive, Stream file)
        {
            _inner = inner;
            _archive = archive;
            _file = file;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _inner.Dispose();
                _archive.Dispose();
                _file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

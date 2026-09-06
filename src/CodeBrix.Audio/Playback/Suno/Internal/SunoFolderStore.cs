using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// A store over a folder of already-extracted files. Nothing is materialised; a key is the file's
/// full path.
/// </summary>
internal sealed class SunoFolderStore : SunoContentStore
{
    private readonly string[] _keys;
    private readonly Dictionary<string, long> _lengths;

    internal SunoFolderStore(string folder, IReadOnlyList<string> filePaths)
    {
        Folder = folder;
        _keys = new string[filePaths.Count];
        _lengths = new Dictionary<string, long>(filePaths.Count, StringComparer.Ordinal);
        for (var i = 0; i < filePaths.Count; i++)
        {
            _keys[i] = filePaths[i];
            _lengths[filePaths[i]] = new FileInfo(filePaths[i]).Length;
        }
    }

    internal string Folder { get; }

    internal override SunoSourceKind Kind => SunoSourceKind.Folder;

    internal override string CacheFolder => null;

    internal override bool CanProvideFilePath => true;

    internal override string[] Keys => _keys;

    internal override long GetLength(string key) => _lengths.TryGetValue(key, out var length) ? length : 0;

    internal override Stream OpenSequential(string key) => File.OpenRead(key);

    internal override Stream OpenSeekable(string key) => File.OpenRead(key);

    internal override string GetFilePath(string key) => key;

    internal override byte[] ReadAllBytes(string key) => File.ReadAllBytes(key);
}

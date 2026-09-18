using System;
using System.IO;

namespace CodeBrix.Audio.Tests.Abc;

/// <summary>
/// Locates the abc fixtures copied next to the test assembly.
/// </summary>
/// <remarks>
/// The files were written for this suite and are described in
/// <c>tests/Assets/abc/ABC-FIXTURES.txt</c>; no third-party transcription is bundled. They exist
/// for the path and stream entry points, which cannot be exercised against an inline string.
/// </remarks>
internal static class AbcTestAssets
{
    internal const string SingleTune = "single-tune.abc";
    internal const string TwoTuneBook = "two-tune-book.abc";
    internal const string TwoVoice = "two-voice.abc";

    /// <summary>Full path to a fixture beside the test assembly.</summary>
    internal static string Path(string fileName)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "abc", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The abc fixture '{fileName}' was not copied to the test output.", path);
        }

        return path;
    }

    /// <summary>Opens a fixture as a seekable in-memory stream.</summary>
    internal static MemoryStream Open(string fileName) => new MemoryStream(File.ReadAllBytes(Path(fileName)));
}

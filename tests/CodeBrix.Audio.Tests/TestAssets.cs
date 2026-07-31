using System;
using System.IO;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Locates the generated audio fixtures that are copied next to the test assembly, and reads the
/// reference PCM that the lossless FLAC comparisons are measured against.
/// </summary>
/// <remarks>
/// The files come from tools/make_test_fixtures/make_fixtures.sh and are described in
/// tests/Assets/audio/AUDIO-FIXTURES.txt. They are synthesized locally, not third-party audio.
/// Every .flac fixture ships with the .wav it was encoded from; because FLAC is lossless, a
/// correct decoder reproduces that PCM exactly.
/// </remarks>
internal static class TestAssets
{
    public const string VorbisToneStereo = "vorbis-tone-stereo-44100.ogg";
    public const string VorbisToneMono = "vorbis-tone-mono-22050.ogg";
    public const string VorbisSweep = "vorbis-sweep-stereo-48000.ogg";
    public const string VorbisTruncated = "vorbis-truncated.ogg";

    /// <summary>Every FLAC fixture, each paired with the .wav it was encoded from.</summary>
    public static readonly string[] AllFlacFixtures =
    [
        "flac-tone-mono-16bit-22050.flac",
        "flac-tone-stereo-16bit-44100-midside.flac",
        "flac-tone-stereo-16bit-44100-leftside.flac",
        "flac-tone-stereo-16bit-44100-rightside.flac",
        "flac-noise-stereo-16bit-44100.flac",
        "flac-silence-stereo-16bit-44100.flac",
        "flac-tone-stereo-24bit-48000.flac",
        "flac-tone-stereo-16bit-44100-oddlength.flac"
    ];

    public const string FlacToneStereo = "flac-tone-stereo-16bit-44100-midside.flac";
    public const string FlacTruncated = "flac-truncated.flac";

    /// <summary>Full path to a fixture beside the test assembly.</summary>
    public static string Path(string fileName)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "audio", fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Audio fixture '{fileName}' was not copied to the test output. Regenerate the " +
                "fixtures with tools/make_test_fixtures/make_fixtures.sh.", path);
        }

        return path;
    }

    /// <summary>The .wav a .flac fixture was encoded from - the ground truth for its decode.</summary>
    public static string ReferenceWavFor(string flacFileName) =>
        Path(System.IO.Path.ChangeExtension(flacFileName, ".wav"));

    /// <summary>Opens a fixture as a seekable in-memory stream.</summary>
    public static MemoryStream Open(string fileName) => new(File.ReadAllBytes(Path(fileName)));
}

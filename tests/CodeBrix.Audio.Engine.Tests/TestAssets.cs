using System;
using System.IO;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Locates the generated audio fixtures that are copied next to the test assembly.
/// </summary>
/// <remarks>
/// The files come from tools/make_test_fixtures/make_fixtures.sh and are described in
/// tests/Assets/audio/AUDIO-FIXTURES.txt. They are synthesized locally, not third-party audio.
/// </remarks>
internal static class TestAssets
{
    /// <summary>0.25 s stereo 44.1 kHz Ogg Vorbis: two tones, one per channel.</summary>
    public const string VorbisToneStereo = "vorbis-tone-stereo-44100.ogg";

    /// <summary>0.25 s mono 22.05 kHz Ogg Vorbis - the low-rate game-effect shape.</summary>
    public const string VorbisToneMono = "vorbis-tone-mono-22050.ogg";

    /// <summary>2 s stereo 48 kHz Ogg Vorbis sweep, 200 Hz to 2 kHz.</summary>
    public const string VorbisSweep = "vorbis-sweep-stereo-48000.ogg";

    /// <summary>An Ogg stream cut off mid-page.</summary>
    public const string VorbisTruncated = "vorbis-truncated.ogg";

    /// <summary>0.25 s stereo 44.1 kHz 16-bit FLAC, mid/side stereo, full LPC search.</summary>
    public const string FlacToneStereo = "flac-tone-stereo-16bit-44100-midside.flac";

    /// <summary>A FLAC stream cut off mid-frame.</summary>
    public const string FlacTruncated = "flac-truncated.flac";

    /// <summary>Expected sample rate of <see cref="VorbisToneStereo" />.</summary>
    public const int VorbisToneStereoSampleRate = 44100;

    /// <summary>Expected frame count of <see cref="VorbisToneStereo" /> (0.25 s at 44.1 kHz).</summary>
    public const int VorbisToneStereoFrames = 11025;

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

    /// <summary>Opens a fixture as a seekable in-memory stream.</summary>
    public static MemoryStream Open(string fileName) => new(File.ReadAllBytes(Path(fileName)));
}

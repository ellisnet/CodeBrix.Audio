using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Locates the fixtures used by the <see cref="CodeBrix.Audio.Synth"/> tests: the synthetic
/// SoundFont, and the reference vectors the DSP comparisons are measured against.
/// </summary>
/// <remarks>
/// <para>
/// The SoundFont comes from <c>tools/make_test_fixtures/make_soundfont.py</c> and is built from
/// sine tones - it is not third-party sample content. No real GM SoundFont is committed here:
/// they run to tens of megabytes and are variously licensed, and this package is MIT.
/// </para>
/// <para>
/// The reference vectors under <c>tests/Assets/synth</c> are Freeverb (public domain) and
/// TinySoundFont (MIT). The upstream MeltySynth suite also compared against parameter dumps of
/// the GPL-2 TimGM6mb SoundFont; those tests and their data stayed behind with Doom.Brix, which
/// is GPL-2 and can host them legitimately.
/// </para>
/// </remarks>
//was previously: MeltySynthTest.TestSettings
public static class SynthTestAssets
{
    /// <summary>The synthetic SoundFont fixture, without its file extension.</summary>
    public const string TestSoundFontName = "codebrix-test";

    /// <summary>
    /// xUnit <c>[MemberData]</c> source naming the SoundFonts under test. Each test loads the
    /// SoundFont itself via <see cref="LoadSoundFont"/> rather than receiving a
    /// (non-serializable) <see cref="SoundFont"/> instance through <c>MemberData</c>.
    /// </summary>
    /// <returns>One row per SoundFont fixture.</returns>
    public static IEnumerable<object[]> SoundFontNames()
    {
        yield return [TestSoundFontName];
    }

    /// <summary>Loads a committed SoundFont fixture by name.</summary>
    /// <param name="name">The fixture name, without the <c>.sf2</c> extension.</param>
    /// <returns>The parsed SoundFont.</returns>
    public static SoundFont LoadSoundFont(string name) => new SoundFont(SoundFontPath(name));

    /// <summary>Full path to a SoundFont fixture beside the test assembly.</summary>
    /// <param name="name">The fixture name, without the <c>.sf2</c> extension.</param>
    /// <returns>The absolute path to the <c>.sf2</c> file.</returns>
    public static string SoundFontPath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "soundfont", name + ".sf2");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The SoundFont fixture '{name}.sf2' was not found at '{path}'. Regenerate the "
                + "fixtures with tools/make_test_fixtures/make_soundfont.py.",
                path);
        }

        return path;
    }

    /// <summary>Root of the reference test vectors copied beside the test assembly.</summary>
    public static string ReferenceDataDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "synth");
}

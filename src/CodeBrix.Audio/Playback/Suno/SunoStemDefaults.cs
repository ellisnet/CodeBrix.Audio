using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// The stem names a Suno stems export is known to use, and the General MIDI defaults each one
/// carries. The vocabulary is OPEN: a name that is not listed here still loads, still plays, and is
/// reported once in <see cref="SunoSong.Problems"/> so that a consumer knows no defaults applied.
/// </summary>
/// <remarks>
/// <para>
/// Suno's "Auto split" mode advertises twelve instruments. Ten of them have been seen in real
/// exports - Vocals, Backing Vocals, Bass, Drums, Percussion, Guitar, Synth, FX, Brass and
/// Keyboard - and their programs and channels below are the ones measured in those files. Piano and
/// Strings are listed too, with sensible General MIDI choices, so that an export carrying them is
/// not reported as unrecognised; those two programs are inferred, not measured.
/// </para>
/// <para>
/// Names are matched case-insensitively and with surrounding whitespace ignored, because the name
/// comes off a file name.
/// </para>
/// </remarks>
public static class SunoStemDefaults
{
    // The order of this array is the order stems appear in a loaded song, so that the same export
    // produces the same model whether it was read from a zip or from a folder.
    private static readonly SunoStemDefault[] Known =
    [
        new SunoStemDefault("Vocals", 54, 1, false),          // Synth Voice, measured
        new SunoStemDefault("Backing Vocals", 53, 1, false),  // Voice Oohs, measured
        new SunoStemDefault("Drums", 118, 10, true),          // kit number on channel 10, measured
        new SunoStemDefault("Percussion", 9, 10, true),       // kit number on channel 10, measured
        new SunoStemDefault("Bass", 32, 1, false),            // Acoustic Bass, measured
        new SunoStemDefault("Guitar", 24, 1, false),          // Nylon Guitar, measured
        new SunoStemDefault("Keyboard", 0, 1, false),         // Acoustic Grand Piano, inferred
        new SunoStemDefault("Piano", 0, 1, false),            // Acoustic Grand Piano, inferred
        new SunoStemDefault("Synth", 80, 1, false),           // Lead 1 (square), measured
        new SunoStemDefault("Strings", 48, 1, false),         // String Ensemble 1, inferred
        new SunoStemDefault("Brass", 61, 1, false),           // Brass Section, inferred
        new SunoStemDefault("FX", 96, 1, false),              // FX 1 (rain), measured
    ];

    private static readonly Dictionary<string, int> IndexByName = BuildIndex();

    /// <summary>
    /// Every stem name this package knows about, in the order stems are listed in a loaded song.
    /// </summary>
    public static IReadOnlyList<string> KnownStemNames { get; } = Known.Select(stem => stem.Name).ToArray();

    /// <summary>
    /// Every known stem's defaults, in the order stems are listed in a loaded song.
    /// </summary>
    public static IReadOnlyList<SunoStemDefault> All { get; } = Array.AsReadOnly(Known);

    /// <summary>
    /// Whether the name is one of the stem names this package knows about.
    /// </summary>
    /// <param name="stemName">The stem name, as it appeared on the file name.</param>
    /// <returns><see langword="true"/> when defaults exist for the name.</returns>
    public static bool IsKnown(string stemName) => stemName != null && IndexByName.ContainsKey(stemName.Trim());

    /// <summary>
    /// Looks up the defaults for a stem name.
    /// </summary>
    /// <param name="stemName">The stem name, as it appeared on the file name.</param>
    /// <param name="defaults">The defaults for that name when the method returns true.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryGet(string stemName, out SunoStemDefault defaults)
    {
        if (stemName != null && IndexByName.TryGetValue(stemName.Trim(), out var index))
        {
            defaults = Known[index];
            return true;
        }

        defaults = default;
        return false;
    }

    /// <summary>
    /// The defaults for a stem name, or a fallback of program 0 on channel 1 for a name that is not
    /// in the vocabulary. Never throws and never reports; use <see cref="TryGet"/> when the caller
    /// needs to know whether the name was recognised.
    /// </summary>
    /// <param name="stemName">The stem name, as it appeared on the file name.</param>
    /// <returns>The defaults to apply to that stem.</returns>
    public static SunoStemDefault GetOrFallback(string stemName) =>
        TryGet(stemName, out var known) ? known : new SunoStemDefault(stemName ?? string.Empty, 0, 1, false);

    /// <summary>
    /// Where a stem name sorts among the known names, or a value past the end of the vocabulary for
    /// a name that is not in it. Used to give a loaded song a stable stem order.
    /// </summary>
    /// <param name="stemName">The stem name, as it appeared on the file name.</param>
    /// <returns>The sort position.</returns>
    public static int SortIndex(string stemName) =>
        stemName != null && IndexByName.TryGetValue(stemName.Trim(), out var index) ? index : Known.Length;

    private static Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Known.Length; i++)
        {
            index[Known[i].Name] = i;
        }

        return index;
    }
}

using System;
using System.Globalization;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One <c>name=value</c> pair from an SFZ file, with the raw text preserved.
/// </summary>
/// <remarks>
/// <para>
/// SFZ values are untyped in the file, so the raw string is kept and the typed accessors convert on
/// demand. That matters for tooling as much as for playback: a survey that wants to know which opcodes
/// a library uses should not fail because one of them holds something it did not expect.
/// </para>
/// <para>
/// Opcode names carry structure. <c>volume_oncc74</c> is the opcode <c>volume</c> modulated by MIDI CC
/// 74; <c>locc64</c> is a range test on CC 64; <c>amp_velcurve_82</c> is indexed by velocity 82.
/// <see cref="BaseName"/> and <see cref="Index"/> expose that split so callers can group by the opcode
/// rather than by every numbered variant of it.
/// </para>
/// </remarks>
public sealed class SfzOpcode
{
    /// <summary>Creates an opcode from its name and raw value.</summary>
    /// <param name="name">The opcode name exactly as written in the file, lower-cased.</param>
    /// <param name="value">The raw value text, with surrounding whitespace removed.</param>
    /// <param name="lineNumber">The 1-based line the opcode was read from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is null.</exception>
    public SfzOpcode(string name, string value, int lineNumber)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        LineNumber = lineNumber;

        (BaseName, Index, Modulation) = Decompose(name);
    }

    /// <summary>The opcode name as written, lower-cased (for example <c>volume_oncc74</c>).</summary>
    public string Name { get; }

    /// <summary>The raw value text (for example <c>-6.0</c> or <c>samples/piano_c4.wav</c>).</summary>
    public string Value { get; }

    /// <summary>The 1-based line number the opcode was read from.</summary>
    public int LineNumber { get; }

    /// <summary>
    /// The opcode with any trailing number and modulation suffix removed - the name to group by.
    /// <c>volume_oncc74</c> gives <c>volume</c>; <c>locc64</c> gives <c>locc</c>;
    /// <c>amp_velcurve_82</c> gives <c>amp_velcurve</c>.
    /// </summary>
    public string BaseName { get; }

    /// <summary>
    /// The number embedded in the name (the CC number, the velocity point, the effect index), or
    /// <see langword="null"/> when the name carries no number.
    /// </summary>
    public int? Index { get; }

    /// <summary>
    /// The modulation suffix when the name has one - <c>oncc</c>, <c>cc</c>, <c>curvecc</c>,
    /// <c>smoothcc</c>, <c>stepcc</c> - otherwise <see langword="null"/>.
    /// </summary>
    public string Modulation { get; }

    /// <summary>Reads the value as an integer.</summary>
    /// <param name="fallback">Returned when the value is not an integer.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    public int AsInt(int fallback = 0) =>
        int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    /// <summary>Reads the value as a float.</summary>
    /// <param name="fallback">Returned when the value is not a number.</param>
    /// <returns>The parsed value, or <paramref name="fallback"/>.</returns>
    public float AsFloat(float fallback = 0f) =>
        float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    /// <summary>
    /// Reads the value as a MIDI note number, accepting both numeric form (<c>60</c>) and note-name
    /// form (<c>c4</c>, <c>f#3</c>, <c>Bb-1</c>), which SFZ allows interchangeably.
    /// </summary>
    /// <param name="fallback">Returned when the value is neither form.</param>
    /// <returns>The note number 0-127, or <paramref name="fallback"/>.</returns>
    public int AsNoteNumber(int fallback = -1) => ParseNoteNumber(Value, fallback);

    /// <inheritdoc/>
    public override string ToString() => Name + "=" + Value;

    /// <summary>
    /// Parses a MIDI note in either numeric or note-name form. <c>c4</c> is 60, matching the SFZ
    /// convention where middle C is C4.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="fallback">Returned when the text is neither form.</param>
    /// <returns>The note number, or <paramref name="fallback"/>.</returns>
    public static int ParseNoteNumber(string text, int fallback = -1)
    {
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        var i = 0;
        int semitone;
        switch (char.ToLowerInvariant(text[i]))
        {
            case 'c': semitone = 0; break;
            case 'd': semitone = 2; break;
            case 'e': semitone = 4; break;
            case 'f': semitone = 5; break;
            case 'g': semitone = 7; break;
            case 'a': semitone = 9; break;
            case 'b': semitone = 11; break;
            default: return fallback;
        }
        i++;

        while (i < text.Length && (text[i] == '#' || text[i] == 'b' || text[i] == 'B'))
        {
            semitone += text[i] == '#' ? 1 : -1;
            i++;
        }

        if (i >= text.Length ||
            !int.TryParse(text.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave))
        {
            return fallback;
        }

        // SFZ places middle C (note 60) at C4.
        return (octave + 1) * 12 + semitone;
    }

    // Splits volume_oncc74 -> (volume, 74, oncc); locc64 -> (locc, 64, cc); amp_velcurve_82 -> (amp_velcurve, 82, null).
    private static (string BaseName, int? Index, string Modulation) Decompose(string name)
    {
        // Trailing digits first: everything numbered ends with them.
        var end = name.Length;
        while (end > 0 && char.IsDigit(name[end - 1]))
        {
            end--;
        }

        if (end == name.Length)
        {
            return (name, null, null);
        }

        if (!int.TryParse(name.Substring(end), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return (name, null, null);
        }

        var stem = name.Substring(0, end);

        // locc/hicc and their prefixed forms (start_locc, sw_hicc, ...) are RANGE TESTS on a
        // controller, not modulation of some other opcode. The whole stem is the opcode name -
        // stripping "cc" here would turn locc64 into "lo", which names nothing.
        if (stem.EndsWith("locc", StringComparison.Ordinal) || stem.EndsWith("hicc", StringComparison.Ordinal))
        {
            return (stem, index, "cc");
        }

        // The modulation suffixes, longest first so oncc is not mistaken for cc.
        string[] suffixes = ["curvecc", "smoothcc", "stepcc", "oncc", "cc"];
        foreach (var suffix in suffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.Ordinal))
            {
                var baseName = stem.Substring(0, stem.Length - suffix.Length).TrimEnd('_');

                // Nothing left once the suffix is removed: the stem IS the opcode.
                if (baseName.Length == 0)
                {
                    return (stem, index, suffix);
                }

                return (baseName, index, suffix);
            }
        }

        return (stem.TrimEnd('_'), index, null);
    }
}

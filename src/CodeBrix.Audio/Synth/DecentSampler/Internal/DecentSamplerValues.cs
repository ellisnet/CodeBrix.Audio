using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Internal;

/// <summary>
/// Turns the text of a <c>.dspreset</c> attribute into a typed value, following the conventions the
/// Decent Sampler developer guide uses throughout the format.
/// </summary>
/// <remarks>
/// <para>
/// Every method is a Try pattern: a value that cannot be read is reported by the caller as a problem
/// and the documented default is kept, because a preset never fails to load over one bad attribute.
/// </para>
/// <para>
/// All numbers are read with <see cref="CultureInfo.InvariantCulture"/>. Presets are authored on many
/// machines and a comma decimal separator would silently change a gain.
/// </para>
/// </remarks>
internal static class DecentSamplerValues
{
    /// <summary>
    /// The octave number middle C carries in a note name. Decent Sampler uses the Yamaha convention,
    /// so middle C (MIDI 60) is C3, C4 is 72, A4 is 81, C0 is 24 and C-1 is 12; in general
    /// <c>midi = (octave + 2) * 12 + pitchClass</c>.
    /// </summary>
    /// <remarks>
    /// MEASURED against the reference player: a preset gave one zone per spelling of <c>rootNote</c>
    /// and the played pitch identified the root each name resolved to. This is NOT the convention the
    /// SFZ engine uses (C4 = 60 there), which is why the two note-name parsers stay separate. This
    /// single constant is the only place the choice is made - change it here and every note name moves
    /// together.
    /// </remarks>
    public const int MiddleCOctave = 3;

    private static readonly string[] NoteLetters = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Reads a floating-point number.</summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">The parsed number.</param>
    /// <returns><see langword="true"/> when the text is a number.</returns>
    public static bool TryDouble(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a whole number, accepting a floating-point spelling of one.</summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">The parsed number.</param>
    /// <returns><see langword="true"/> when the text is a number.</returns>
    public static bool TryInt(string text, out int value)
    {
        value = 0;
        if (!TryDouble(text, out var number))
        {
            return false;
        }

        if (number < int.MinValue || number > int.MaxValue)
        {
            return false;
        }

        value = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Reads a frame position, accepting a floating-point spelling of one.</summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">The parsed number.</param>
    /// <returns><see langword="true"/> when the text is a number.</returns>
    public static bool TryLong(string text, out long value)
    {
        value = 0;
        if (!TryDouble(text, out var number))
        {
            return false;
        }

        if (number < long.MinValue || number > long.MaxValue)
        {
            return false;
        }

        value = (long)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Reads a boolean, accepting <c>true</c>, <c>false</c>, <c>1</c> and <c>0</c>.</summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the text is a boolean.</returns>
    public static bool TryBool(string text, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
                value = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The largest linear volume the format honours, which is +24 dB.</summary>
    public const double MaximumVolume = 16.0;

    /// <summary>
    /// Reads a volume, which the format writes either as a linear multiplier (<c>0.5</c>) or in
    /// decibels with a <c>dB</c> suffix (<c>3dB</c>, <c>-6 dB</c>).
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="linear">
    /// The volume as a linear multiplier, clamped to 0 to <see cref="MaximumVolume"/>. A zero or
    /// negative number is silence.
    /// </param>
    /// <param name="wasDecibels">Whether the text carried a <c>dB</c> suffix.</param>
    /// <returns><see langword="true"/> when the text is a volume.</returns>
    /// <remarks>
    /// MEASURED against the reference player: the suffix is the literal string <c>dB</c> in that exact
    /// case, so <c>-6db</c> and <c>-6DB</c> are read as the bare number -6 and therefore as silence. A
    /// bare number is a LINEAR multiplier, so <c>volume="3"</c> is a gain of three, not +3 dB.
    /// </remarks>
    public static bool TryVolume(string text, out double linear, out bool wasDecibels)
    {
        linear = 1.0;
        wasDecibels = false;

        if (!TryLeadingVolumeNumber(text, out var number, out wasDecibels))
        {
            return false;
        }

        var gain = wasDecibels ? DecibelsToLinear(number) : number;
        linear = gain <= 0.0 || double.IsNaN(gain) ? 0.0 : gain > MaximumVolume ? MaximumVolume : gain;
        return true;
    }

    /// <summary>
    /// Reads a <c>&lt;tag&gt;</c> volume, which uses a DIFFERENT parser from a <c>&lt;sample&gt;</c>'s
    /// or a <c>&lt;group&gt;</c>'s: the absolute value of the leading number, always linear.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="linear">The volume as a linear multiplier.</param>
    /// <returns><see langword="true"/> when the text starts with a number.</returns>
    /// <remarks>
    /// MEASURED against the reference player (round 2, item 24), seven cases at a level 20 dB below
    /// the player's limiter: <c>"2"</c> and <c>"4"</c> give gains of 2 and 4, so the documented 0-to-1
    /// range is NOT enforced and neither is the [0, 16] clamp a sample volume gets; <c>"0.5xyz"</c>
    /// gives 0.5, so trailing junk is ignored; and <c>"-6"</c> and <c>"-6dB"</c> BOTH give a gain of
    /// six, so the <c>dB</c> suffix is not recognised at all and a negative number is made positive
    /// rather than turned into silence. Only <c>"0"</c> silences a tag.
    /// </remarks>
    public static bool TryTagVolume(string text, out double linear)
    {
        linear = 1.0;

        if (!TryLeadingVolumeNumber(text, out var number, out _))
        {
            return false;
        }

        linear = double.IsNaN(number) ? 1.0 : Math.Abs(number);
        return true;
    }

    /// <summary>
    /// Reads a release-trigger decay rate, which the format writes either in decibels per second with
    /// a <c>dB</c> suffix or as a linear gain factor per second.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">
    /// The decay rate. In decibel form this is always negative, because the guide says a positive
    /// decibel value is converted to the decay direction.
    /// </param>
    /// <param name="inDecibels">Whether the value is decibels per second rather than a linear factor.</param>
    /// <returns><see langword="true"/> when the text is a decay rate.</returns>
    public static bool TryDecayRate(string text, out double value, out bool inDecibels)
    {
        value = 0;
        if (!TryDecibelNumber(text, out var number, out inDecibels))
        {
            return false;
        }

        value = inDecibels ? -Math.Abs(number) : number;
        return true;
    }

    /// <summary>Converts decibels to a linear multiplier.</summary>
    /// <param name="decibels">The level in decibels, where 0 is unity.</param>
    /// <returns>The linear multiplier.</returns>
    public static double DecibelsToLinear(double decibels) => Math.Pow(10.0, decibels / 20.0);

    /// <summary>Converts a linear multiplier to decibels.</summary>
    /// <param name="linear">The linear multiplier, where 1 is unity.</param>
    /// <returns>The level in decibels; negative infinity for zero or less.</returns>
    public static double LinearToDecibels(double linear) =>
        linear <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(linear);

    /// <summary>
    /// Reads a percentage, accepting both <c>50</c> and <c>50%</c>.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="fraction">The value divided by one hundred, so 50 gives 0.5.</param>
    /// <returns><see langword="true"/> when the text is a percentage.</returns>
    public static bool TryPercent(string text, out double fraction)
    {
        fraction = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        if (!TryDouble(trimmed, out var number))
        {
            return false;
        }

        fraction = number / 100.0;
        return true;
    }

    /// <summary>
    /// Reads a MIDI note, accepting both a number (<c>60</c>) and a note name (<c>C4</c>,
    /// <c>c#3</c>, <c>Db-1</c>).
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="note">The MIDI note number, 0 to 127.</param>
    /// <returns><see langword="true"/> when the text names a note in range.</returns>
    public static bool TryNote(string text, out int note)
    {
        note = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (TryInt(trimmed, out var number))
        {
            if (number < 0 || number > 127)
            {
                return false;
            }

            note = number;
            return true;
        }

        var index = 0;
        var pitchClass = char.ToUpperInvariant(trimmed[index]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => -1
        };

        if (pitchClass < 0)
        {
            return false;
        }

        index++;

        while (index < trimmed.Length)
        {
            var accidental = trimmed[index];
            if (accidental == '#' || accidental == 's' || accidental == 'S')
            {
                pitchClass++;
                index++;
            }
            else if (accidental == 'b' || accidental == 'B')
            {
                // 'b' is only a flat when something follows it; a trailing 'b' would be the note B.
                if (index + 1 >= trimmed.Length)
                {
                    break;
                }

                pitchClass--;
                index++;
            }
            else
            {
                break;
            }
        }

        if (index >= trimmed.Length || !int.TryParse(
                trimmed.Substring(index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var octave))
        {
            return false;
        }

        var value = (octave + (5 - MiddleCOctave)) * 12 + pitchClass;
        if (value < 0 || value > 127)
        {
            return false;
        }

        note = value;
        return true;
    }

    /// <summary>The note name for a MIDI note number, using this engine's octave convention.</summary>
    /// <param name="note">The MIDI note number.</param>
    /// <returns>A name such as <c>C4</c>.</returns>
    public static string NoteName(int note)
    {
        var octave = note / 12 - (5 - MiddleCOctave);
        return NoteLetters[((note % 12) + 12) % 12] + octave.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a comma-separated list of tags or names, dropping blanks and trimming each entry.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <returns>The names. Never null; empty when the text holds none.</returns>
    public static string[] TagList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [] : parts;
    }

    /// <summary>
    /// Reads a comma-separated list of MIDI notes, as <c>previousNotes</c> uses.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="notes">The notes, in the order written.</param>
    /// <returns><see langword="true"/> when every entry is a note.</returns>
    public static bool TryNoteList(string text, out int[] notes)
    {
        notes = [];
        var parts = TagList(text);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsed = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryNote(parts[i], out parsed[i]))
            {
                return false;
            }
        }

        notes = parsed;
        return true;
    }

    /// <summary>
    /// Reads a translation table, written as <c>in,out;in,out</c> with at least two points.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="points">The points, in the order written.</param>
    /// <returns><see langword="true"/> when the text holds two or more well-formed points.</returns>
    public static bool TryTranslationTable(string text, out DecentSamplerTranslationPoint[] points)
    {
        points = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var groups = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<DecentSamplerTranslationPoint>(groups.Length);

        foreach (var group in groups)
        {
            var pair = group.Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !TryDouble(pair[0], out var input) || !TryDouble(pair[1], out var output))
            {
                return false;
            }

            parsed.Add(new DecentSamplerTranslationPoint(input, output));
        }

        if (parsed.Count < 2)
        {
            return false;
        }

        points = parsed.ToArray();
        return true;
    }

    /// <summary>
    /// Reads a note number or an inclusive note range such as <c>24-35</c>, as the
    /// <c>&lt;note&gt;</c> handler's <c>note</c> attribute allows.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="lowNote">The bottom of the range.</param>
    /// <param name="highNote">The top of the range, inclusive.</param>
    /// <returns><see langword="true"/> when the text is a note or a range of notes.</returns>
    public static bool TryNoteRange(string text, out int lowNote, out int highNote)
    {
        lowNote = 0;
        highNote = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var dash = trimmed.IndexOf('-', 1);

        if (dash > 0)
        {
            var low = trimmed.Substring(0, dash);
            var high = trimmed.Substring(dash + 1);
            if (TryNote(low, out lowNote) && TryNote(high, out highNote))
            {
                if (highNote < lowNote)
                {
                    (lowNote, highNote) = (highNote, lowNote);
                }

                return true;
            }

            lowNote = 0;
            highNote = 0;
        }

        if (!TryNote(trimmed, out lowNote))
        {
            return false;
        }

        highNote = lowNote;
        return true;
    }

    /// <summary>
    /// Keeps a colour exactly as the preset wrote it, minus surrounding whitespace and a leading
    /// <c>#</c>. Colours are eight hexadecimal digits in ARGB order; this engine renders nothing, so
    /// the text is carried through untouched for whoever does.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <returns>The trimmed colour text, or null when there is none.</returns>
    public static string Color(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;
    }

    /// <summary>
    /// Reads the leading number of a volume string and decides whether the exact suffix <c>dB</c>
    /// follows it. Anything after that is ignored, which is what the reference player does.
    /// </summary>
    /// <param name="text">The attribute text.</param>
    /// <param name="number">The leading number.</param>
    /// <param name="wasDecibels">Whether the exact suffix <c>dB</c> followed it.</param>
    /// <returns><see langword="true"/> when the text starts with a number.</returns>
    private static bool TryLeadingVolumeNumber(string text, out double number, out bool wasDecibels)
    {
        number = 0;
        wasDecibels = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var length = 0;

        while (length < trimmed.Length && IsNumberCharacter(trimmed[length]))
        {
            length++;
        }

        // Shrink the candidate until it parses, so that "1e" and "1." give up their trailing
        // character rather than the whole number.
        while (length > 0 && !TryDouble(trimmed.Substring(0, length), out number))
        {
            length--;
        }

        if (length == 0)
        {
            return false;
        }

        var rest = trimmed.Substring(length).TrimStart();
        wasDecibels = rest.StartsWith("dB", StringComparison.Ordinal);
        return true;
    }

    private static bool IsNumberCharacter(char character) =>
        char.IsAsciiDigit(character) || character == '.' || character == '+' || character == '-' ||
        character == 'e' || character == 'E';

    private static bool TryDecibelNumber(string text, out double number, out bool wasDecibels)
    {
        number = 0;
        wasDecibels = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
        {
            wasDecibels = true;
            trimmed = trimmed.Substring(0, trimmed.Length - 2).TrimEnd();
        }

        return TryDouble(trimmed, out number);
    }
}

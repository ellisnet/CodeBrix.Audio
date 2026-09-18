using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Audio.Abc.Internal;

// The values of the five information fields that change how a tune sounds: M:, L:, Q:, K: and V:.
// Everything here works on the text of one field value and reports what it could not read rather
// than throwing.
internal static class AbcFieldParser
{
    // The circle of fifths for the seven natural letters: how many sharps the MAJOR key on that
    // letter carries. A '#' moves seven further round, a 'b' seven back.
    private static int MajorSharpsFor(char letter)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'F': return -1;
            case 'C': return 0;
            case 'G': return 1;
            case 'D': return 2;
            case 'A': return 3;
            case 'E': return 4;
            case 'B': return 5;
            default: return 0;
        }
    }

    // How far a mode sits from the major of the same tonic, in fifths.
    private static int ModeOffset(AbcMode mode)
    {
        switch (mode)
        {
            case AbcMode.Major:
            case AbcMode.Ionian:
                return 0;
            case AbcMode.Minor:
            case AbcMode.Aeolian:
                return -3;
            case AbcMode.Dorian:
                return -2;
            case AbcMode.Phrygian:
                return -4;
            case AbcMode.Lydian:
                return 1;
            case AbcMode.Mixolydian:
                return -1;
            case AbcMode.Locrian:
                return -5;
            default:
                return 0;
        }
    }

    internal static AbcMeter ParseMeter(string value, AbcParseContext context)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            return AbcMeter.Free;
        }

        if (text == "C")
        {
            return AbcMeter.CommonTime;
        }

        if (text == "C|")
        {
            return AbcMeter.CutTime;
        }

        int slash = text.IndexOf('/');
        if (slash <= 0)
        {
            context.Add($"The meter \"{text}\" could not be read; free meter was used instead.");
            return AbcMeter.Free;
        }

        string numeratorText = text.Substring(0, slash).Trim().Trim('(', ')');
        string denominatorText = text.Substring(slash + 1).Trim();

        var numerators = new List<int>();
        foreach (string part in numeratorText.Split('+'))
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) &&
                number > 0)
            {
                numerators.Add(number);
            }
        }

        if (numerators.Count == 0 ||
            !int.TryParse(denominatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int denominator) ||
            denominator <= 0)
        {
            context.Add($"The meter \"{text}\" could not be read; free meter was used instead.");
            return AbcMeter.Free;
        }

        return new AbcMeter(numerators, denominator, false, text);
    }

    internal static AbcDuration ParseUnitNoteLength(string value, AbcParseContext context, AbcDuration fallback)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            context.Add("An empty L: field was ignored.");
            return fallback;
        }

        int slash = text.IndexOf('/');
        if (slash < 0)
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole) && whole > 0)
            {
                return new AbcDuration(whole, 1);
            }

            context.Add($"The unit note length \"{text}\" could not be read; {fallback} was used instead.");
            return fallback;
        }

        string numeratorText = text.Substring(0, slash).Trim();
        string denominatorText = text.Substring(slash + 1).Trim();
        long numerator = 1;
        if (numeratorText.Length > 0 &&
            !long.TryParse(numeratorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator))
        {
            context.Add($"The unit note length \"{text}\" could not be read; {fallback} was used instead.");
            return fallback;
        }

        if (!long.TryParse(denominatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long denominator) ||
            denominator <= 0 || numerator <= 0)
        {
            context.Add($"The unit note length \"{text}\" could not be read; {fallback} was used instead.");
            return fallback;
        }

        return new AbcDuration(numerator, denominator);
    }

    internal static AbcTempo ParseTempo(string value, AbcDuration unitNoteLength, AbcParseContext context)
    {
        string original = (value ?? string.Empty).Trim();

        // Pull the quoted label out first; the standard allows it before the value, after it, or
        // instead of it.
        var label = new StringBuilder();
        var remaining = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < original.Length; i++)
        {
            char c = original[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                label.Append(c);
            }
            else
            {
                remaining.Append(c);
            }
        }

        string body = remaining.ToString().Trim();
        if (body.Length == 0)
        {
            return new AbcTempo(new AbcDuration[0], 0.0, false, label.ToString(), original);
        }

        int equals = body.LastIndexOf('=');
        if (equals < 0)
        {
            // The deprecated bare number: so many UNIT NOTE LENGTHS per minute.
            if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out double bare) && bare > 0)
            {
                return new AbcTempo(new[] { unitNoteLength }, bare, true, label.ToString(), original);
            }

            context.Add($"The tempo \"{original}\" could not be read; it was ignored.");
            return new AbcTempo(new AbcDuration[0], 0.0, false, label.ToString(), original);
        }

        string beatsText = body.Substring(0, equals).Trim();
        string rateText = body.Substring(equals + 1).Trim();

        if (!double.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || rate <= 0)
        {
            context.Add($"The tempo \"{original}\" could not be read; it was ignored.");
            return new AbcTempo(new AbcDuration[0], 0.0, false, label.ToString(), original);
        }

        var beats = new List<AbcDuration>();
        foreach (string part in beatsText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int slash = part.IndexOf('/');
            if (slash > 0 &&
                long.TryParse(part.Substring(0, slash), NumberStyles.Integer, CultureInfo.InvariantCulture, out long numerator) &&
                long.TryParse(part.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long denominator) &&
                numerator > 0 && denominator > 0)
            {
                beats.Add(new AbcDuration(numerator, denominator));
                continue;
            }

            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole) && whole > 0)
            {
                beats.Add(new AbcDuration(whole, 1));
            }
        }

        if (beats.Count == 0)
        {
            // "Q:=120" and anything else with no readable beat: the standard's own deprecated form
            // counts unit note lengths, which is the nearest sensible reading.
            beats.Add(unitNoteLength);
        }

        return new AbcTempo(beats, rate, true, label.ToString(), original);
    }

    internal static AbcKey ParseKey(string value, AbcParseContext context)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            return AbcKey.None;
        }

        if (text == "HP" || text == "Hp" ||
            text.StartsWith("HP ", StringComparison.Ordinal) || text.StartsWith("Hp ", StringComparison.Ordinal))
        {
            context.AddOnce(
                "bagpipe-key",
                $"The highland bagpipe key \"{text}\" carries no key signature this reader applies; the notes sound as written.");
            return new AbcKey(text.Substring(0, 2), AbcMode.Bagpipe, 0, new AbcKeyAccidental[0], false, text);
        }

        int position = 0;
        char letter = text[position];
        if (!((letter >= 'A' && letter <= 'G') || (letter >= 'a' && letter <= 'g')))
        {
            context.Add($"The key \"{text}\" could not be read; no key signature was applied.");
            return AbcKey.None;
        }

        position++;
        var tonic = new StringBuilder();
        tonic.Append(char.ToUpperInvariant(letter));
        int sharps = MajorSharpsFor(letter);
        if (position < text.Length && (text[position] == '#' || text[position] == 'b'))
        {
            sharps += text[position] == '#' ? 7 : -7;
            tonic.Append(text[position]);
            position++;
        }

        var mode = AbcMode.Major;
        bool isExplicit = false;
        var accidentals = new List<AbcKeyAccidental>();
        bool modeRead = false;

        while (position < text.Length)
        {
            while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
            {
                position++;
            }

            if (position >= text.Length)
            {
                break;
            }

            char c = text[position];

            if (c == '^' || c == '_' || c == '=')
            {
                int consumed = ReadKeyAccidental(text, position, accidentals);
                if (consumed == position)
                {
                    position++;
                }
                else
                {
                    position = consumed;
                }

                continue;
            }

            if (char.IsLetter(c))
            {
                int start = position;
                while (position < text.Length && char.IsLetter(text[position]))
                {
                    position++;
                }

                string word = text.Substring(start, position - start);

                // A property such as clef=bass or transpose=-24 keeps going past its name.
                if (position < text.Length && text[position] == '=')
                {
                    while (position < text.Length && text[position] != ' ' && text[position] != '\t')
                    {
                        position++;
                    }

                    context.AddOnce(
                        "key-properties",
                        "Clef, transposition and staff properties in a K: field were skipped; they do not change what is played.");
                    continue;
                }

                if (string.Equals(word, "exp", StringComparison.OrdinalIgnoreCase))
                {
                    isExplicit = true;
                    continue;
                }

                if (!modeRead && TryReadMode(word, out AbcMode parsed))
                {
                    mode = parsed;
                    modeRead = true;
                    continue;
                }

                context.AddOnce(
                    "key-properties",
                    "Clef, transposition and staff properties in a K: field were skipped; they do not change what is played.");
                continue;
            }

            // Anything else in a K: field is a property this reader does not need.
            context.AddOnce(
                "key-properties",
                "Clef, transposition and staff properties in a K: field were skipped; they do not change what is played.");
            position++;
        }

        int signature = sharps + ModeOffset(mode);
        if (signature > 7 || signature < -7)
        {
            context.AddOnce(
                "key-out-of-range",
                $"The key \"{text}\" needs {Math.Abs(signature)} accidentals, which a MIDI key signature cannot carry; it was clamped to seven.");
            signature = signature > 7 ? 7 : -7;
        }

        return new AbcKey(tonic.ToString(), mode, signature, accidentals, isExplicit, text);
    }

    private static int ReadKeyAccidental(string text, int position, List<AbcKeyAccidental> accidentals)
    {
        var accidental = AbcAccidental.None;
        int start = position;

        if (text[position] == '^')
        {
            position++;
            if (position < text.Length && text[position] == '^')
            {
                position++;
                accidental = AbcAccidental.DoubleSharp;
            }
            else
            {
                accidental = AbcAccidental.Sharp;
            }
        }
        else if (text[position] == '_')
        {
            position++;
            if (position < text.Length && text[position] == '_')
            {
                position++;
                accidental = AbcAccidental.DoubleFlat;
            }
            else
            {
                accidental = AbcAccidental.Flat;
            }
        }
        else
        {
            position++;
            accidental = AbcAccidental.Natural;
        }

        if (position >= text.Length)
        {
            return start;
        }

        char letter = text[position];
        if (!((letter >= 'A' && letter <= 'G') || (letter >= 'a' && letter <= 'g')))
        {
            return start;
        }

        position++;

        // The case of the letter says which line it is printed on; a signature applies to every
        // octave, so the octave marks that may follow change nothing here.
        while (position < text.Length && (text[position] == '\'' || text[position] == ','))
        {
            position++;
        }

        accidentals.Add(new AbcKeyAccidental(char.ToUpperInvariant(letter), accidental));
        return position;
    }

    // The standard parses only the first three letters of a mode name, ignores capitalisation, and
    // lets minor shorten all the way to "m".
    private static bool TryReadMode(string word, out AbcMode mode)
    {
        mode = AbcMode.Major;
        if (word.Length == 0)
        {
            return false;
        }

        string lower = word.ToLowerInvariant();
        if (lower == "m")
        {
            mode = AbcMode.Minor;
            return true;
        }

        if (lower.Length < 3)
        {
            return false;
        }

        switch (lower.Substring(0, 3))
        {
            case "maj":
                mode = AbcMode.Major;
                return true;
            case "min":
                mode = AbcMode.Minor;
                return true;
            case "ion":
                mode = AbcMode.Ionian;
                return true;
            case "aeo":
                mode = AbcMode.Aeolian;
                return true;
            case "dor":
                mode = AbcMode.Dorian;
                return true;
            case "phr":
                mode = AbcMode.Phrygian;
                return true;
            case "lyd":
                mode = AbcMode.Lydian;
                return true;
            case "mix":
                mode = AbcMode.Mixolydian;
                return true;
            case "loc":
                mode = AbcMode.Locrian;
                return true;
            default:
                return false;
        }
    }

    // "V:T1 clef=treble-8 name=\"Tenore I\" snm=\"T.I\"" -> the id and the name; everything else is
    // a printing matter.
    internal static void ParseVoice(string value, out string id, out string name)
    {
        id = string.Empty;
        name = string.Empty;
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        int position = 0;
        while (position < text.Length && text[position] != ' ' && text[position] != '\t')
        {
            position++;
        }

        id = text.Substring(0, position);

        while (position < text.Length)
        {
            while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
            {
                position++;
            }

            if (position >= text.Length)
            {
                break;
            }

            int nameStart = position;
            while (position < text.Length && text[position] != '=' && text[position] != ' ' && text[position] != '\t')
            {
                position++;
            }

            string property = text.Substring(nameStart, position - nameStart);
            if (position >= text.Length || text[position] != '=')
            {
                continue;
            }

            position++;
            string propertyValue;
            if (position < text.Length && text[position] == '"')
            {
                int end = text.IndexOf('"', position + 1);
                if (end < 0)
                {
                    propertyValue = text.Substring(position + 1);
                    position = text.Length;
                }
                else
                {
                    propertyValue = text.Substring(position + 1, end - position - 1);
                    position = end + 1;
                }
            }
            else
            {
                int start = position;
                while (position < text.Length && text[position] != ' ' && text[position] != '\t')
                {
                    position++;
                }

                propertyValue = text.Substring(start, position - start);
            }

            if (string.Equals(property, "name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property, "nm", StringComparison.OrdinalIgnoreCase))
            {
                name = propertyValue;
            }
        }
    }
}

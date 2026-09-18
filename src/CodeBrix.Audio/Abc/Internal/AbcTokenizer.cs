using System.Collections.Generic;

namespace CodeBrix.Audio.Abc.Internal;

// A character-level scanner for one line of abc music code.
//
// Written by hand rather than with regular expressions, because abc's own grammar is positional and
// ambiguous at exactly the places a pattern would have to guess: '[' opens a chord, an inline field,
// an ending mark or a thick bar line depending on what follows it; ':' is a repeat dot inside a bar
// line and a field separator inside brackets; '-' is a tie between notes and a range inside an
// ending list; '.' is a staccato dot, the first half of a dotted bar line, and the marker on a
// dotted slur. One scanner that looks ahead a character at a time settles all of them; a set of
// patterns competing over the same line does not.
//
// The scanner knows nothing about the unit note length, the key or the meter. It reports what was
// written - a letter, an octave, a length MULTIPLIER - and AbcTuneParser turns that into music.
internal static class AbcTokenizer
{
    // The letters the standard assigns as shorthand decorations: H-W, h-w and '~'. They cannot
    // collide with note letters, which are A-G and a-g, nor with the rests z, x, Z and X.
    private static bool IsDecorationLetter(char c) =>
        c == '~' || (c >= 'H' && c <= 'W') || (c >= 'h' && c <= 'w');

    private static bool IsNoteLetter(char c) => (c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g');

    internal static List<AbcToken> Tokenize(string line)
    {
        var tokens = new List<AbcToken>();
        if (string.IsNullOrEmpty(line))
        {
            return tokens;
        }

        int position = 0;
        int length = line.Length;

        while (position < length)
        {
            char c = line[position];

            if (c == ' ' || c == '\t' || c == '`')
            {
                // White space breaks a beam and nothing else; back quotes are legibility only.
                position++;
                continue;
            }

            if (c == '\\' || c == '$')
            {
                // A trailing backslash suppresses a printed line break; '$' is a line-break marker.
                // Neither changes a tick.
                position++;
                continue;
            }

            if (c == '"')
            {
                position = ReadQuoted(line, position, tokens);
                continue;
            }

            if (c == '!' || c == '+')
            {
                position = ReadDelimitedDecoration(line, position, c, tokens);
                continue;
            }

            if (c == '.')
            {
                // ".|" is a dotted bar line; ".(" and ".-" mark a dotted slur or tie; on its own
                // '.' is the staccato decoration.
                if (position + 1 < length && line[position + 1] == '|')
                {
                    position = ReadBarLine(line, position, tokens);
                    continue;
                }

                if (position + 1 < length && (line[position + 1] == '(' || line[position + 1] == '-'))
                {
                    position++;
                    continue;
                }

                tokens.Add(new AbcToken(AbcTokenKind.Decoration) { Text = "." });
                position++;
                continue;
            }

            if (c == '|' || (c == ':' && position + 1 < length && (line[position + 1] == '|' || line[position + 1] == ':')))
            {
                position = ReadBarLine(line, position, tokens);
                continue;
            }

            if (c == '[')
            {
                position = ReadOpenBracket(line, position, tokens);
                continue;
            }

            if (c == ']')
            {
                var close = new AbcToken(AbcTokenKind.ChordEnd);
                position = ReadLength(line, position + 1, close);
                tokens.Add(close);
                continue;
            }

            if (c == '{')
            {
                bool acciaccatura = position + 1 < length && line[position + 1] == '/';
                tokens.Add(new AbcToken(AbcTokenKind.GraceStart) { IsAcciaccatura = acciaccatura });
                position += acciaccatura ? 2 : 1;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new AbcToken(AbcTokenKind.GraceEnd));
                position++;
                continue;
            }

            if (c == '(')
            {
                if (position + 1 < length && line[position + 1] >= '0' && line[position + 1] <= '9')
                {
                    position = ReadTuplet(line, position + 1, tokens);
                    continue;
                }

                tokens.Add(new AbcToken(AbcTokenKind.SlurStart));
                position++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new AbcToken(AbcTokenKind.SlurEnd));
                position++;
                continue;
            }

            if (c == '-')
            {
                tokens.Add(new AbcToken(AbcTokenKind.Tie));
                position++;
                continue;
            }

            if (c == '>' || c == '<')
            {
                int count = 0;
                while (position < length && line[position] == c)
                {
                    count++;
                    position++;
                }

                tokens.Add(new AbcToken(AbcTokenKind.BrokenRhythm) { BrokenRhythm = c == '>' ? count : -count });
                continue;
            }

            if (c == '&')
            {
                tokens.Add(new AbcToken(AbcTokenKind.VoiceOverlay));
                position++;
                continue;
            }

            if (c == 'z' || c == 'x')
            {
                var rest = new AbcToken(AbcTokenKind.Rest) { IsVisible = c == 'z' };
                position = ReadLength(line, position + 1, rest);
                tokens.Add(rest);
                continue;
            }

            if (c == 'Z' || c == 'X')
            {
                position++;
                int measures = 0;
                bool any = false;
                while (position < length && line[position] >= '0' && line[position] <= '9')
                {
                    measures = (measures * 10) + (line[position] - '0');
                    any = true;
                    position++;
                }

                tokens.Add(new AbcToken(AbcTokenKind.MultiMeasureRest)
                {
                    MeasureCount = any && measures > 0 ? measures : 1,
                    IsVisible = c == 'Z',
                });
                continue;
            }

            if (c == '^' || c == '_' || c == '=' || IsNoteLetter(c))
            {
                int next = ReadNote(line, position, tokens);
                if (next > position)
                {
                    position = next;
                    continue;
                }

                // An accidental with no note letter after it.
                tokens.Add(new AbcToken(AbcTokenKind.Unknown) { Text = c.ToString() });
                position++;
                continue;
            }

            if (IsDecorationLetter(c))
            {
                tokens.Add(new AbcToken(AbcTokenKind.Decoration) { Text = c.ToString() });
                position++;
                continue;
            }

            if (c == 'y')
            {
                // A typesetting spacer: it occupies width on the page and no time at all.
                tokens.Add(new AbcToken(AbcTokenKind.LayoutMark) { Text = "y" });
                position++;
                continue;
            }

            tokens.Add(new AbcToken(AbcTokenKind.Unknown) { Text = c.ToString() });
            position++;
        }

        return tokens;
    }

    private static int ReadQuoted(string line, int position, List<AbcToken> tokens)
    {
        int start = position + 1;
        int end = line.IndexOf('"', start);
        if (end < 0)
        {
            tokens.Add(new AbcToken(AbcTokenKind.Unknown) { Text = line.Substring(position) });
            return line.Length;
        }

        string text = line.Substring(start, end - start);
        // A placement specifier - ^ _ < > @ - is what distinguishes an annotation from a chord
        // symbol, and the standard says a program must not try to play an annotation.
        bool annotation = text.Length > 0 &&
            (text[0] == '^' || text[0] == '_' || text[0] == '<' || text[0] == '>' || text[0] == '@');
        tokens.Add(new AbcToken(annotation ? AbcTokenKind.Annotation : AbcTokenKind.ChordSymbol) { Text = text });
        return end + 1;
    }

    private static int ReadDelimitedDecoration(string line, int position, char delimiter, List<AbcToken> tokens)
    {
        int end = line.IndexOf(delimiter, position + 1);
        if (end < 0)
        {
            // A lone '!' is the old line-break shorthand; a lone '+' is not anything at all.
            tokens.Add(delimiter == '!'
                ? new AbcToken(AbcTokenKind.LayoutMark) { Text = "!" }
                : new AbcToken(AbcTokenKind.Unknown) { Text = "+" });
            return position + 1;
        }

        tokens.Add(new AbcToken(AbcTokenKind.Decoration)
        {
            Text = line.Substring(position + 1, end - position - 1),
        });
        return end + 1;
    }

    private static int ReadOpenBracket(string line, int position, List<AbcToken> tokens)
    {
        int length = line.Length;
        char next = position + 1 < length ? line[position + 1] : '\0';

        if (next == '|' || next == ':')
        {
            return ReadBarLine(line, position, tokens);
        }

        if (next >= '0' && next <= '9')
        {
            return ReadEnding(line, position + 1, tokens);
        }

        if (position + 2 < length && char.IsLetter(next) && line[position + 2] == ':')
        {
            int end = line.IndexOf(']', position + 3);
            if (end < 0)
            {
                tokens.Add(new AbcToken(AbcTokenKind.InlineField)
                {
                    Field = next,
                    Text = line.Substring(position + 3).Trim(),
                });
                return length;
            }

            tokens.Add(new AbcToken(AbcTokenKind.InlineField)
            {
                Field = next,
                Text = line.Substring(position + 3, end - position - 3).Trim(),
            });
            return end + 1;
        }

        tokens.Add(new AbcToken(AbcTokenKind.ChordStart));
        return position + 1;
    }

    // "|1", ":|2", "[1,3" and "[1-3" all reach here pointing at the first digit.
    private static int ReadEnding(string line, int position, List<AbcToken> tokens)
    {
        var numbers = new List<int>();
        int length = line.Length;

        while (position < length)
        {
            int value = 0;
            bool any = false;
            while (position < length && line[position] >= '0' && line[position] <= '9')
            {
                value = (value * 10) + (line[position] - '0');
                any = true;
                position++;
            }

            if (!any)
            {
                break;
            }

            if (position < length && line[position] == '-' &&
                position + 1 < length && line[position + 1] >= '0' && line[position + 1] <= '9')
            {
                position++;
                int upper = 0;
                while (position < length && line[position] >= '0' && line[position] <= '9')
                {
                    upper = (upper * 10) + (line[position] - '0');
                    position++;
                }

                for (int n = value; n <= upper; n++)
                {
                    numbers.Add(n);
                }
            }
            else
            {
                numbers.Add(value);
            }

            if (position < length && line[position] == ',')
            {
                position++;
                continue;
            }

            break;
        }

        tokens.Add(new AbcToken(AbcTokenKind.Ending) { Endings = numbers.ToArray() });
        return position;
    }

    // The standard asks parsers to be liberal here: in the wild a bar line is any run of '|', '['
    // or ']' (thick) and ':' (dots). A ']' only joins a run that has already started, so a chord's
    // closing bracket is never swallowed.
    private static int ReadBarLine(string line, int position, List<AbcToken> tokens)
    {
        int length = line.Length;
        int start = position;

        if (position < length && line[position] == '.')
        {
            position++;
        }

        while (position < length)
        {
            char c = line[position];
            if (c == '|' || c == ':')
            {
                position++;
                continue;
            }

            if (c == ']' && position > start)
            {
                position++;
                continue;
            }

            if (c == '[' && position + 1 < length && (line[position + 1] == '|' || line[position + 1] == ':'))
            {
                position++;
                continue;
            }

            break;
        }

        tokens.Add(new AbcToken(AbcTokenKind.BarLine) { Text = line.Substring(start, position - start) });

        // "|1" and ":|2" are a bar line followed by an ending mark with the bracket left out.
        if (position < length && line[position] >= '0' && line[position] <= '9')
        {
            position = ReadEnding(line, position, tokens);
        }

        return position;
    }

    // Enters pointing at the first digit of "(p", "(p:q" or "(p:q:r".
    private static int ReadTuplet(string line, int position, List<AbcToken> tokens)
    {
        int length = line.Length;
        int p = 0;
        while (position < length && line[position] >= '0' && line[position] <= '9')
        {
            p = (p * 10) + (line[position] - '0');
            position++;
        }

        int q = 0;
        int r = 0;
        if (position < length && line[position] == ':')
        {
            position++;
            while (position < length && line[position] >= '0' && line[position] <= '9')
            {
                q = (q * 10) + (line[position] - '0');
                position++;
            }

            if (position < length && line[position] == ':')
            {
                position++;
                while (position < length && line[position] >= '0' && line[position] <= '9')
                {
                    r = (r * 10) + (line[position] - '0');
                    position++;
                }
            }
        }

        tokens.Add(new AbcToken(AbcTokenKind.TupletStart) { TupletP = p, TupletQ = q, TupletR = r });
        return position;
    }

    // Returns the position after the note, or the position it was given when there was no note
    // letter to read.
    private static int ReadNote(string line, int position, List<AbcToken> tokens)
    {
        int length = line.Length;
        int start = position;
        var accidental = AbcAccidental.None;

        if (position < length && line[position] == '^')
        {
            position++;
            if (position < length && line[position] == '^')
            {
                position++;
                accidental = AbcAccidental.DoubleSharp;
            }
            else
            {
                accidental = AbcAccidental.Sharp;
            }
        }
        else if (position < length && line[position] == '_')
        {
            position++;
            if (position < length && line[position] == '_')
            {
                position++;
                accidental = AbcAccidental.DoubleFlat;
            }
            else
            {
                accidental = AbcAccidental.Flat;
            }
        }
        else if (position < length && line[position] == '=')
        {
            position++;
            accidental = AbcAccidental.Natural;
        }

        if (position >= length || !IsNoteLetter(line[position]))
        {
            return start;
        }

        char letter = line[position];
        int octave = letter >= 'a' && letter <= 'g' ? 1 : 0;
        position++;

        // Any combination of commas and apostrophes, in any order, per the standard.
        while (position < length && (line[position] == '\'' || line[position] == ','))
        {
            octave += line[position] == '\'' ? 1 : -1;
            position++;
        }

        var token = new AbcToken(AbcTokenKind.Note)
        {
            Letter = char.ToUpperInvariant(letter),
            Octave = octave,
            Accidental = accidental,
        };
        position = ReadLength(line, position, token);
        tokens.Add(token);
        return position;
    }

    // "3", "/2", "3/2", "/" (= /2) and "//" (= /4) all reach here.
    private static int ReadLength(string line, int position, AbcToken token)
    {
        int length = line.Length;
        long numerator = 0;
        bool hasNumerator = false;
        while (position < length && line[position] >= '0' && line[position] <= '9')
        {
            numerator = (numerator * 10) + (line[position] - '0');
            hasNumerator = true;
            position++;
        }

        long denominator = 1;
        if (position < length && line[position] == '/')
        {
            int slashes = 0;
            while (position < length && line[position] == '/')
            {
                slashes++;
                position++;
            }

            long written = 0;
            bool hasDenominator = false;
            while (position < length && line[position] >= '0' && line[position] <= '9')
            {
                written = (written * 10) + (line[position] - '0');
                hasDenominator = true;
                position++;
            }

            if (hasDenominator && written > 0)
            {
                denominator = written;
                // "//4" is not in the standard; halve once more per extra slash so it still means
                // something shorter rather than nothing at all.
                for (int i = 1; i < slashes; i++)
                {
                    denominator *= 2;
                }
            }
            else
            {
                denominator = 1;
                for (int i = 0; i < slashes; i++)
                {
                    denominator *= 2;
                }
            }
        }

        token.LengthNumerator = hasNumerator && numerator > 0 ? numerator : 1;
        token.LengthDenominator = denominator;
        return position;
    }
}

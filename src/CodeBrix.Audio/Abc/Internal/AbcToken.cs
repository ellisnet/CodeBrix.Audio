namespace CodeBrix.Audio.Abc.Internal;

// One token of music code. Deliberately one class with a kind rather than a hierarchy: a token
// lives for the length of one line and is read once, and a switch on Kind in one parser is easier
// to hold in the head than fifteen tiny types.
//
// Lengths are stored as a MULTIPLIER of the unit note length, not as an absolute duration, because
// the unit note length can change part-way along a line and only the parser knows what is in force.
internal sealed class AbcToken
{
    internal AbcToken(AbcTokenKind kind)
    {
        Kind = kind;
        LengthNumerator = 1;
        LengthDenominator = 1;
        Text = string.Empty;
    }

    internal AbcTokenKind Kind { get; }

    // Note: the letter, its octave and the accidental written in front of it.
    internal char Letter { get; set; }

    internal int Octave { get; set; }

    internal AbcAccidental Accidental { get; set; }

    // Note, Rest and ChordEnd: the multiplier written after the symbol. 1/1 when none was.
    internal long LengthNumerator { get; set; }

    internal long LengthDenominator { get; set; }

    // Rest: whether it was z (printed) or x (invisible). MultiMeasureRest: the number of measures.
    internal bool IsVisible { get; set; }

    internal int MeasureCount { get; set; }

    // GraceStart: whether the group opened with "{/".
    internal bool IsAcciaccatura { get; set; }

    // TupletStart: the p, q and r of "(p:q:r". Zero means "not written".
    internal int TupletP { get; set; }

    internal int TupletQ { get; set; }

    internal int TupletR { get; set; }

    // BrokenRhythm: positive for ">", negative for "<", the magnitude being how many were written.
    internal int BrokenRhythm { get; set; }

    // Ending: the ending numbers, expanded from the lists and ranges the standard allows.
    internal int[] Endings { get; set; }

    // InlineField: the field letter.
    internal char Field { get; set; }

    // BarLine, InlineField, Decoration, ChordSymbol, Annotation, Unknown: the literal text.
    internal string Text { get; set; }
}

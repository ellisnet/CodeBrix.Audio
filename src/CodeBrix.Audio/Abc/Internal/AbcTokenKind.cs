namespace CodeBrix.Audio.Abc.Internal;

// What one piece of music code is. The tokenizer produces these; AbcTuneParser turns them into
// bars. Everything abc can write in a tune body has a kind here, including the kinds that are
// dropped, so that each of them can be counted once and named once.
internal enum AbcTokenKind
{
    Note,
    Rest,
    MultiMeasureRest,
    ChordStart,
    ChordEnd,
    GraceStart,
    GraceEnd,
    TupletStart,
    BarLine,
    Ending,
    Tie,
    BrokenRhythm,
    InlineField,
    SlurStart,
    SlurEnd,
    Decoration,
    ChordSymbol,
    Annotation,
    LayoutMark,
    VoiceOverlay,
    Unknown,
}

namespace CodeBrix.Audio.Abc;

/// <summary>
/// An accidental written in front of a note, as abc spells it.
/// </summary>
/// <remarks>
/// Abc writes a sharp as <c>^</c>, a flat as <c>_</c>, a natural as <c>=</c>, and doubles them for
/// the double sharp <c>^^</c> and the double flat <c>__</c>. <see cref="None"/> means no accidental
/// was written, which is not the same as <see cref="Natural"/>: a written natural cancels the key
/// signature and any accidental earlier in the bar, while nothing written leaves both in force.
/// </remarks>
public enum AbcAccidental
{
    /// <summary>No accidental was written.</summary>
    None = 0,

    /// <summary>A double flat, <c>__</c>. Lowers the note two semitones.</summary>
    DoubleFlat = -2,

    /// <summary>A flat, <c>_</c>. Lowers the note one semitone.</summary>
    Flat = -1,

    /// <summary>A natural, <c>=</c>. Cancels the key signature and any accidental earlier in the bar.</summary>
    Natural = 3,

    /// <summary>A sharp, <c>^</c>. Raises the note one semitone.</summary>
    Sharp = 1,

    /// <summary>A double sharp, <c>^^</c>. Raises the note two semitones.</summary>
    DoubleSharp = 2,
}

using System.Globalization;
using System.Text;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// One sounding note.
/// </summary>
/// <remarks>
/// <para>
/// Pitch is kept the way abc writes it - a letter, an octave and whatever accidental stood in front
/// of it - rather than as a MIDI note number, because the key signature and the accidentals earlier
/// in the bar are what turn the two into a pitch, and those belong to the tune rather than to the
/// note. <see cref="AbcToMidi"/> applies them.
/// </para>
/// <para>
/// <see cref="Length"/> is this note's own written length as a fraction of a whole note: the unit
/// note length in force multiplied by whatever followed the letter, with any broken-rhythm marker
/// already applied. A tuplet does NOT scale it - the ratio lives on the
/// <see cref="AbcTupletGroup"/> holding the note, so the written length and the ratio can both be
/// read back.
/// </para>
/// </remarks>
public sealed class AbcNote : AbcElement
{
    /// <summary>
    /// Creates a note.
    /// </summary>
    /// <param name="letter">The note letter, <c>A</c> to <c>G</c>, upper case.</param>
    /// <param name="octave">The octave, where 0 is the octave of abc's upper-case <c>C</c>.</param>
    /// <param name="accidental">The accidental written in front of the note.</param>
    /// <param name="length">The written length, as a fraction of a whole note.</param>
    /// <param name="tiedToNext">Whether a tie was written after this note.</param>
    public AbcNote(char letter, int octave, AbcAccidental accidental, AbcDuration length, bool tiedToNext)
    {
        Letter = letter;
        Octave = octave;
        Accidental = accidental;
        Length = length;
        TiedToNext = tiedToNext;
    }

    /// <summary>The note letter, <c>A</c> to <c>G</c>, upper case.</summary>
    public char Letter { get; }

    /// <summary>
    /// The octave, counted from abc's upper-case <c>C</c> - middle C, MIDI note 60 - which is
    /// octave 0. Lower-case <c>c</c> is octave 1, <c>C,</c> is octave -1, <c>c'</c> is octave 2.
    /// </summary>
    public int Octave { get; }

    /// <summary>The accidental written in front of the note, if any.</summary>
    public AbcAccidental Accidental { get; }

    /// <summary>
    /// The written length, as a fraction of a whole note. Any tuplet ratio is on the group rather
    /// than here.
    /// </summary>
    public AbcDuration Length { get; }

    /// <summary>
    /// Whether a tie, <c>-</c>, was written after this note. A tie joins this note to the next note
    /// of the same pitch, and the two sound as one longer note.
    /// </summary>
    public bool TiedToNext { get; }

    /// <summary>
    /// The number of diatonic steps above abc's upper-case <c>C</c>: 0 for <c>C</c>, 1 for
    /// <c>D</c>, 7 for <c>c</c>, -7 for <c>C,</c>.
    /// </summary>
    public int DiatonicStep => LetterIndex(Letter) + (Octave * 7);

    /// <summary>Describes the note the way abc writes it.</summary>
    /// <returns>For example <c>"^c'2"</c>.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        switch (Accidental)
        {
            case AbcAccidental.DoubleFlat:
                text.Append("__");
                break;
            case AbcAccidental.Flat:
                text.Append('_');
                break;
            case AbcAccidental.Natural:
                text.Append('=');
                break;
            case AbcAccidental.Sharp:
                text.Append('^');
                break;
            case AbcAccidental.DoubleSharp:
                text.Append("^^");
                break;
        }

        text.Append(Octave >= 1 ? char.ToLowerInvariant(Letter) : Letter);
        for (int i = 1; i < Octave; i++)
        {
            text.Append('\'');
        }

        for (int i = 0; i > Octave; i--)
        {
            text.Append(',');
        }

        text.Append(' ');
        text.Append(Length.ToString());
        return text.ToString();
    }

    internal static int LetterIndex(char letter)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'C': return 0;
            case 'D': return 1;
            case 'E': return 2;
            case 'F': return 3;
            case 'G': return 4;
            case 'A': return 5;
            case 'B': return 6;
            default: return 0;
        }
    }

    // Semitones above the C of the same octave, for the seven natural letters.
    internal static int NaturalSemitone(char letter)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'C': return 0;
            case 'D': return 2;
            case 'E': return 4;
            case 'F': return 5;
            case 'G': return 7;
            case 'A': return 9;
            case 'B': return 11;
            default: return 0;
        }
    }

    internal string DescribePitch() =>
        string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(Letter)}{Octave}");
}

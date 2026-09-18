namespace CodeBrix.Audio.Abc;

/// <summary>
/// One accidental written after the key in a <c>K:</c> field.
/// </summary>
/// <remarks>
/// The standard lets a key be modified - <c>K:D Phr ^f</c> - or spelled out in full -
/// <c>K:D exp _b _e ^f</c>. Either way each accidental is a sign followed by a note letter, and the
/// case of the letter says which line it is printed on. For PLAYBACK the case does not matter: a key
/// signature applies to the letter in every octave, so <see cref="Letter"/> is stored upper case and
/// applied to every octave of that letter.
/// </remarks>
public sealed class AbcKeyAccidental
{
    /// <summary>
    /// Creates a key accidental.
    /// </summary>
    /// <param name="letter">The note letter, <c>A</c> to <c>G</c>, upper case.</param>
    /// <param name="accidental">The accidental to apply to it.</param>
    public AbcKeyAccidental(char letter, AbcAccidental accidental)
    {
        Letter = letter;
        Accidental = accidental;
    }

    /// <summary>The note letter, <c>A</c> to <c>G</c>, upper case.</summary>
    public char Letter { get; }

    /// <summary>The accidental to apply to that letter in every octave.</summary>
    public AbcAccidental Accidental { get; }

    /// <summary>Describes the accidental.</summary>
    /// <returns>For example <c>"Sharp F"</c>.</returns>
    public override string ToString() => $"{Accidental} {Letter}";
}

using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// The key from a <c>K:</c> field: a tonic, a mode, and any accidentals written after them.
/// </summary>
/// <remarks>
/// <para>
/// A mode is mapped to the signature of its relative major, which is what the standard's own table
/// of equivalent spellings says: <c>K:DDor</c>, <c>K:GMix</c>, <c>K:Am</c> and <c>K:C</c> all have
/// no sharps and no flats. <see cref="SharpsFlats"/> is that signature, positive for sharps and
/// negative for flats.
/// </para>
/// <para>
/// <c>K:none</c> and an empty <c>K:</c> field mean no signature at all. <c>K:HP</c> and <c>K:Hp</c>
/// are the two highland bagpipe keys; neither writes a signature this reader can honour, so the
/// notes sound as written and the key is listed once in the tune's problems.
/// </para>
/// </remarks>
public sealed class AbcKey
{
    private readonly List<AbcKeyAccidental> _accidentals;

    /// <summary>
    /// Creates a key.
    /// </summary>
    /// <param name="tonic">The tonic letter with its accidental, such as <c>F#</c>, or an empty string.</param>
    /// <param name="mode">The mode named, or <see cref="AbcMode.Major"/> when none was.</param>
    /// <param name="sharpsFlats">The signature: positive for sharps, negative for flats, -7 to 7.</param>
    /// <param name="accidentals">Accidentals written after the key.</param>
    /// <param name="isExplicit">Whether the accidentals were introduced by <c>exp</c>.</param>
    /// <param name="text">The field value exactly as it was written.</param>
    public AbcKey(
        string tonic,
        AbcMode mode,
        int sharpsFlats,
        IEnumerable<AbcKeyAccidental> accidentals,
        bool isExplicit,
        string text)
    {
        Tonic = tonic ?? string.Empty;
        Mode = mode;
        SharpsFlats = sharpsFlats;
        _accidentals = accidentals == null
            ? new List<AbcKeyAccidental>()
            : new List<AbcKeyAccidental>(accidentals);
        IsExplicit = isExplicit;
        Text = text ?? string.Empty;
    }

    /// <summary>C major: no sharps, no flats. What a tune with no readable key is read as.</summary>
    public static AbcKey CMajor { get; } =
        new AbcKey("C", AbcMode.Major, 0, new AbcKeyAccidental[0], false, "C");

    /// <summary>No key signature at all - <c>K:none</c>.</summary>
    public static AbcKey None { get; } =
        new AbcKey(string.Empty, AbcMode.None, 0, new AbcKeyAccidental[0], false, "none");

    /// <summary>The tonic letter with its accidental, such as <c>F#</c>. Empty for <c>K:none</c>.</summary>
    public string Tonic { get; }

    /// <summary>The mode named, or <see cref="AbcMode.Major"/> when none was.</summary>
    public AbcMode Mode { get; }

    /// <summary>
    /// The key signature: positive for sharps, negative for flats, in the range -7 to 7. Zero when
    /// the key carries no signature.
    /// </summary>
    public int SharpsFlats { get; }

    /// <summary>Accidentals written after the key, applied on top of the signature.</summary>
    public IReadOnlyList<AbcKeyAccidental> Accidentals => _accidentals;

    /// <summary>
    /// Whether the accidentals were introduced by <c>exp</c>, which means they REPLACE the mode's
    /// signature rather than modifying it.
    /// </summary>
    public bool IsExplicit { get; }

    /// <summary>The field value exactly as it was written.</summary>
    public string Text { get; }

    /// <summary>Whether this key carries no signature - <c>K:none</c>, an empty field, or a bagpipe key.</summary>
    public bool HasNoSignature => Mode == AbcMode.None || Mode == AbcMode.Bagpipe;

    /// <summary>
    /// The <c>majorMinor</c> byte of a MIDI key signature event: 1 for a minor key, 0 for every
    /// other mode.
    /// </summary>
    public int MajorMinor => Mode == AbcMode.Minor || Mode == AbcMode.Aeolian ? 1 : 0;

    /// <summary>Describes the key.</summary>
    /// <returns>The field value as it was written.</returns>
    public override string ToString() => Text.Length == 0 ? "none" : Text;

    /// <summary>
    /// The accidental this key applies to a note letter, taking the signature and any written
    /// accidentals together.
    /// </summary>
    /// <param name="letter">The note letter, in either case.</param>
    /// <returns>The accidental to apply, or <see cref="AbcAccidental.None"/>.</returns>
    /// <remarks>
    /// A written accidental wins over the signature, which is what <c>K:D maj =c</c> - D major with
    /// a natural C, the mixolydian mode - relies on. With <c>exp</c> the written accidentals are the
    /// whole signature.
    /// </remarks>
    public AbcAccidental AccidentalFor(char letter)
    {
        char upper = char.ToUpperInvariant(letter);
        for (int i = 0; i < _accidentals.Count; i++)
        {
            if (_accidentals[i].Letter == upper)
            {
                return _accidentals[i].Accidental;
            }
        }

        if (IsExplicit || HasNoSignature || SharpsFlats == 0)
        {
            return AbcAccidental.None;
        }

        if (SharpsFlats > 0)
        {
            // Sharps are added in the order F C G D A E B.
            const string SharpOrder = "FCGDAEB";
            int index = SharpOrder.IndexOf(upper);
            return index >= 0 && index < SharpsFlats ? AbcAccidental.Sharp : AbcAccidental.None;
        }

        // Flats are added in the order B E A D G C F.
        const string FlatOrder = "BEADGCF";
        int flatIndex = FlatOrder.IndexOf(upper);
        return flatIndex >= 0 && flatIndex < -SharpsFlats ? AbcAccidental.Flat : AbcAccidental.None;
    }
}

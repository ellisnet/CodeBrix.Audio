namespace CodeBrix.Audio.Abc;

/// <summary>
/// The mode named in a <c>K:</c> field.
/// </summary>
/// <remarks>
/// The abc standard accepts the full mode name or any abbreviation of it down to the first three
/// letters, ignores capitalisation, and lets minor shorten all the way to <c>m</c>. A <c>K:</c>
/// field with no mode is <see cref="Major"/>.
/// </remarks>
public enum AbcMode
{
    /// <summary>Major, the mode assumed when none is named. Also spelled <c>ionian</c>.</summary>
    Major = 0,

    /// <summary>Minor, <c>min</c> or <c>m</c>. Also spelled <c>aeolian</c>.</summary>
    Minor = 1,

    /// <summary>Ionian, <c>ion</c>. The same signature as <see cref="Major"/>.</summary>
    Ionian = 2,

    /// <summary>Dorian, <c>dor</c>. Two flats further round than the major of the same tonic.</summary>
    Dorian = 3,

    /// <summary>Phrygian, <c>phr</c>. Four flats further round than the major of the same tonic.</summary>
    Phrygian = 4,

    /// <summary>Lydian, <c>lyd</c>. One sharp further round than the major of the same tonic.</summary>
    Lydian = 5,

    /// <summary>Mixolydian, <c>mix</c>. One flat further round than the major of the same tonic.</summary>
    Mixolydian = 6,

    /// <summary>Aeolian, <c>aeo</c>. The same signature as <see cref="Minor"/>.</summary>
    Aeolian = 7,

    /// <summary>Locrian, <c>loc</c>. Five flats further round than the major of the same tonic.</summary>
    Locrian = 8,

    /// <summary>
    /// No mode, because the field was <c>K:none</c> or empty: the music carries no key signature.
    /// </summary>
    None = 9,

    /// <summary>
    /// A highland bagpipe key, <c>K:HP</c> or <c>K:Hp</c>. Neither writes a signature this library
    /// can honour, so the notes sound as written.
    /// </summary>
    Bagpipe = 10,
}

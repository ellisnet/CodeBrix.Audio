namespace CodeBrix.Audio.Abc;

/// <summary>
/// The shape of a bar line, as abc draws it.
/// </summary>
/// <remarks>
/// The repeat dots are held separately, on <see cref="AbcBarLine.StartsRepeat"/> and
/// <see cref="AbcBarLine.EndsRepeat"/>, because abc spells any of these shapes with or without them.
/// None of these shapes affects playback on its own; they are here because the tune model is a
/// faithful reading of the text.
/// </remarks>
public enum AbcBarLineKind
{
    /// <summary>
    /// No bar line was written. Only the opening of the first bar of a voice and the close of a
    /// voice whose last bar was left open carry this.
    /// </summary>
    None = 0,

    /// <summary>A plain bar line, <c>|</c>.</summary>
    Thin = 1,

    /// <summary>A thin-thin double bar line, <c>||</c>.</summary>
    ThinThin = 2,

    /// <summary>A thin-thick double bar line, <c>|]</c>; the usual end of a tune.</summary>
    ThinThick = 3,

    /// <summary>A thick-thin double bar line, <c>[|</c>.</summary>
    ThickThin = 4,

    /// <summary>A dotted bar line, <c>.|</c>; an editorial bar line inside a long measure.</summary>
    Dotted = 5,

    /// <summary>An invisible bar line, <c>[|]</c>.</summary>
    Invisible = 6,
}

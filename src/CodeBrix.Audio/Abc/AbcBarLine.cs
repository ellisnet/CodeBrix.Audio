namespace CodeBrix.Audio.Abc;

/// <summary>
/// One bar line, with whatever repeat dots it carried.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="AbcBar"/> holds the bar line that opens it and the one that closes it, and the
/// line closing one bar is the SAME instance as the one opening the next, so a repeat mark is never
/// counted twice.
/// </para>
/// <para>
/// <c>::</c> is short for <c>:|</c> followed by <c>|:</c>, so it arrives here as one bar line with
/// both <see cref="EndsRepeat"/> and <see cref="StartsRepeat"/> set. Extra dots multiply the repeat:
/// <c>::|</c> plays its section three times rather than twice, which is what
/// <see cref="RepeatCount"/> carries.
/// </para>
/// </remarks>
public sealed class AbcBarLine
{
    /// <summary>
    /// Creates a bar line.
    /// </summary>
    /// <param name="kind">The shape drawn.</param>
    /// <param name="startsRepeat">Whether a repeated section starts here.</param>
    /// <param name="endsRepeat">Whether a repeated section ends here.</param>
    /// <param name="repeatCount">How many times the section ending here is played in total.</param>
    /// <param name="text">The bar line exactly as it was written.</param>
    public AbcBarLine(
        AbcBarLineKind kind,
        bool startsRepeat,
        bool endsRepeat,
        int repeatCount,
        string text)
    {
        Kind = kind;
        StartsRepeat = startsRepeat;
        EndsRepeat = endsRepeat;
        RepeatCount = repeatCount;
        Text = text ?? string.Empty;
    }

    /// <summary>A bar line that was never written.</summary>
    public static AbcBarLine None { get; } = new AbcBarLine(AbcBarLineKind.None, false, false, 1, string.Empty);

    /// <summary>The shape drawn.</summary>
    public AbcBarLineKind Kind { get; }

    /// <summary>Whether a repeated section starts here - the <c>|:</c> dots.</summary>
    public bool StartsRepeat { get; }

    /// <summary>Whether a repeated section ends here - the <c>:|</c> dots.</summary>
    public bool EndsRepeat { get; }

    /// <summary>
    /// How many times the section ending at this bar line is played in total: 2 for <c>:|</c>, 3 for
    /// <c>::|</c>, and so on. 1 when <see cref="EndsRepeat"/> is <see langword="false"/>.
    /// </summary>
    public int RepeatCount { get; }

    /// <summary>The bar line exactly as it was written.</summary>
    public string Text { get; }

    /// <summary>Describes this bar line.</summary>
    /// <returns>The text that was written, or the shape when nothing was.</returns>
    public override string ToString() => Text.Length == 0 ? Kind.ToString() : Text;
}

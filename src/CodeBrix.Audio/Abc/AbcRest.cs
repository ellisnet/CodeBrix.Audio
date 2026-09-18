namespace CodeBrix.Audio.Abc;

/// <summary>
/// A rest: silence of a written length.
/// </summary>
/// <remarks>
/// Abc writes three of them. <c>z</c> is an ordinary rest, <c>x</c> is an invisible rest that takes
/// exactly the same time, and <c>Z</c> is a multi-measure rest counted in bars of the meter in
/// force. All three are read; the difference between <c>z</c> and <c>x</c> is a printing matter and
/// changes nothing that sounds, which is why <see cref="IsVisible"/> is recorded but never acted on.
/// </remarks>
public sealed class AbcRest : AbcElement
{
    /// <summary>
    /// Creates a rest.
    /// </summary>
    /// <param name="length">The length of the silence, as a fraction of a whole note.</param>
    /// <param name="isVisible">Whether the rest is printed - <see langword="false"/> for <c>x</c>.</param>
    /// <param name="measureCount">
    /// The number of measures a multi-measure rest covers, or 0 when this is an ordinary rest.
    /// </param>
    public AbcRest(AbcDuration length, bool isVisible, int measureCount)
    {
        Length = length;
        IsVisible = isVisible;
        MeasureCount = measureCount;
    }

    /// <summary>The length of the silence, as a fraction of a whole note.</summary>
    public AbcDuration Length { get; }

    /// <summary>
    /// Whether the rest is printed. <c>x</c> is invisible; it occupies exactly the same time as the
    /// <c>z</c> it replaces.
    /// </summary>
    public bool IsVisible { get; }

    /// <summary>
    /// The number of measures a multi-measure rest - <c>Z</c> or <c>X</c> - covers, or 0 when this
    /// is an ordinary rest. <see cref="Length"/> already holds the whole silence.
    /// </summary>
    public int MeasureCount { get; }

    /// <summary>Whether this is a multi-measure rest.</summary>
    public bool IsMultiMeasure => MeasureCount > 0;

    /// <summary>Describes the rest.</summary>
    /// <returns>For example <c>"z 1/8"</c>.</returns>
    public override string ToString() => (IsVisible ? "z " : "x ") + Length.ToString();
}

namespace CodeBrix.Audio.Abc;

/// <summary>
/// The unit note length: how long a bare note letter lasts.
/// </summary>
/// <remarks>
/// <para>
/// The <c>L:</c> field sets it. When there is none, the standard works it out from the meter: take
/// the meter as a decimal, and below 0.75 the unit note length is a sixteenth note, at 0.75 or
/// above an eighth. <c>M:C</c>, <c>M:C|</c> and free meter all default to an eighth.
/// </para>
/// <para>
/// A meter change inside the tune body does NOT change the unit note length; only an <c>L:</c>
/// field does. <see cref="WasDefaulted"/> says which of the two rules produced this value.
/// </para>
/// </remarks>
public sealed class AbcUnitNoteLength
{
    /// <summary>
    /// Creates a unit note length.
    /// </summary>
    /// <param name="length">The length, as a fraction of a whole note.</param>
    /// <param name="wasDefaulted">Whether it came from the meter rather than from an <c>L:</c> field.</param>
    public AbcUnitNoteLength(AbcDuration length, bool wasDefaulted)
    {
        Length = length;
        WasDefaulted = wasDefaulted;
    }

    /// <summary>The length, as a fraction of a whole note.</summary>
    public AbcDuration Length { get; }

    /// <summary>
    /// Whether this value came from the standard's default rule rather than from an <c>L:</c> field.
    /// </summary>
    public bool WasDefaulted { get; }

    /// <summary>
    /// Works out the default unit note length for a meter, as the standard defines it.
    /// </summary>
    /// <param name="meter">The meter in force, or <see langword="null"/> for none.</param>
    /// <returns>A sixteenth note below 0.75, an eighth note otherwise.</returns>
    public static AbcDuration DefaultFor(AbcMeter meter)
    {
        if (meter == null || meter.IsFree || meter.Denominator == 0)
        {
            return new AbcDuration(1, 8);
        }

        return meter.DecimalValue < 0.75 ? new AbcDuration(1, 16) : new AbcDuration(1, 8);
    }

    /// <summary>Describes the unit note length.</summary>
    /// <returns>For example <c>"1/8"</c>.</returns>
    public override string ToString() => Length.ToString();
}

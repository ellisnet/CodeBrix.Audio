using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// The meter from an <c>M:</c> field.
/// </summary>
/// <remarks>
/// <para>
/// Abc writes <c>M:6/8</c>, <c>M:C</c> for common time (4/4), <c>M:C|</c> for cut time (2/2),
/// <c>M:none</c> for free meter, and a complex meter such as <c>M:(2+3+2)/8</c> whose numerators say
/// which beats are accented. The parts of a complex numerator are kept in
/// <see cref="Numerators"/>; <see cref="Numerator"/> is their sum, which is the length the bar
/// actually holds.
/// </para>
/// <para>
/// When there is no <c>M:</c> field the standard assumes free meter, and
/// <see cref="IsFree"/> is then <see langword="true"/>.
/// </para>
/// </remarks>
public sealed class AbcMeter
{
    private readonly List<int> _numerators;

    /// <summary>
    /// Creates a meter.
    /// </summary>
    /// <param name="numerators">The parts of the numerator; one entry for a plain meter.</param>
    /// <param name="denominator">The denominator.</param>
    /// <param name="isFree">Whether the meter is free - <c>M:none</c> or no field at all.</param>
    /// <param name="text">The field value exactly as it was written.</param>
    public AbcMeter(IEnumerable<int> numerators, int denominator, bool isFree, string text)
    {
        _numerators = numerators == null ? new List<int>() : new List<int>(numerators);
        Denominator = denominator;
        IsFree = isFree;
        Text = text ?? string.Empty;
    }

    /// <summary>Free meter, which is what the standard assumes when there is no <c>M:</c> field.</summary>
    public static AbcMeter Free { get; } = new AbcMeter(new int[0], 0, true, "none");

    /// <summary>Common time, 4/4.</summary>
    public static AbcMeter CommonTime { get; } = new AbcMeter(new[] { 4 }, 4, false, "C");

    /// <summary>Cut time, 2/2.</summary>
    public static AbcMeter CutTime { get; } = new AbcMeter(new[] { 2 }, 2, false, "C|");

    /// <summary>The parts of the numerator, in order. One entry unless the meter is complex.</summary>
    public IReadOnlyList<int> Numerators => _numerators;

    /// <summary>
    /// The numerator: the sum of <see cref="Numerators"/>, so <c>(2+3+2)/8</c> reads as 7/8. Zero
    /// when the meter is free.
    /// </summary>
    public int Numerator
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _numerators.Count; i++)
            {
                total += _numerators[i];
            }

            return total;
        }
    }

    /// <summary>The denominator. Zero when the meter is free.</summary>
    public int Denominator { get; }

    /// <summary>Whether the meter is free - <c>M:none</c>, or no <c>M:</c> field at all.</summary>
    public bool IsFree { get; }

    /// <summary>The field value exactly as it was written.</summary>
    public string Text { get; }

    /// <summary>
    /// The length of one bar, as a fraction of a whole note. <see cref="AbcDuration.Zero"/> when the
    /// meter is free, because a free bar has no length of its own.
    /// </summary>
    public AbcDuration BarLength =>
        IsFree || Denominator == 0 ? AbcDuration.Zero : new AbcDuration(Numerator, Denominator);

    /// <summary>
    /// The meter as a decimal, which is what the standard's default unit-note-length rule reads:
    /// below 0.75 the default is a sixteenth note, at 0.75 or above an eighth. Zero when free.
    /// </summary>
    public double DecimalValue => IsFree || Denominator == 0 ? 0.0 : (double)Numerator / Denominator;

    /// <summary>
    /// Whether this is a compound meter - 6/8, 9/8 or 12/8 - which is what decides the implied
    /// <c>q</c> of a 5-, 7- or 9-tuplet.
    /// </summary>
    public bool IsCompound =>
        !IsFree && Denominator == 8 && (Numerator == 6 || Numerator == 9 || Numerator == 12);

    /// <summary>Describes the meter.</summary>
    /// <returns>For example <c>"6/8"</c> or <c>"none"</c>.</returns>
    public override string ToString() =>
        IsFree ? "none" : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}

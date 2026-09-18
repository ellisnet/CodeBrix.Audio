using System;
using System.Globalization;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// An exact musical duration, held as a reduced fraction of a whole note.
/// </summary>
/// <remarks>
/// <para>
/// Abc note lengths are built by multiplying and dividing a unit note length, and tuplets divide by
/// numbers that are not powers of two, so a duration expressed as a <see cref="double"/> stops being
/// exact almost immediately and a position accumulated from such durations drifts. Every length and
/// every position in <see cref="CodeBrix.Audio.Abc"/> is therefore this type: a numerator and a
/// denominator, always reduced, always with a positive denominator.
/// </para>
/// <para>
/// One whole note is 1/1. A quarter note is 1/4, a triplet eighth in the time of two is 1/12.
/// <see cref="ToTicks"/> is the only place a duration becomes an integer, and it rounds once, from
/// the exact value, so nothing accumulates rounding error.
/// </para>
/// </remarks>
public readonly struct AbcDuration : IEquatable<AbcDuration>, IComparable<AbcDuration>
{
    private readonly long _numerator;
    private readonly long _denominator;

    /// <summary>
    /// Creates a duration from a numerator and a denominator, reducing it.
    /// </summary>
    /// <param name="numerator">The numerator. May be zero or negative.</param>
    /// <param name="denominator">The denominator. Must not be zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="denominator"/> is zero.</exception>
    public AbcDuration(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator,
                "A duration cannot have a denominator of zero.");
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        long divisor = GreatestCommonDivisor(Math.Abs(numerator), denominator);
        if (divisor > 1)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        _numerator = numerator;
        _denominator = numerator == 0 ? 1 : denominator;
    }

    /// <summary>A duration of no time at all.</summary>
    public static AbcDuration Zero => new AbcDuration(0, 1);

    /// <summary>One whole note.</summary>
    public static AbcDuration Whole => new AbcDuration(1, 1);

    /// <summary>The numerator of the reduced fraction.</summary>
    public long Numerator => _numerator;

    /// <summary>The denominator of the reduced fraction; always one or more.</summary>
    public long Denominator => _denominator == 0 ? 1 : _denominator;

    /// <summary>Whether this duration is zero.</summary>
    public bool IsZero => _numerator == 0;

    /// <summary>
    /// The duration as a fraction of a whole note, for display and comparison against tolerances.
    /// Never use it for arithmetic that feeds a position.
    /// </summary>
    public double Value => (double)_numerator / Denominator;

    /// <summary>
    /// Builds a duration from a whole number of whole notes.
    /// </summary>
    /// <param name="wholeNotes">The number of whole notes.</param>
    /// <returns>The duration.</returns>
    public static AbcDuration FromWholeNotes(long wholeNotes) => new AbcDuration(wholeNotes, 1);

    /// <summary>Adds two durations.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns>The sum.</returns>
    public static AbcDuration operator +(AbcDuration left, AbcDuration right) =>
        new AbcDuration(
            (left._numerator * right.Denominator) + (right._numerator * left.Denominator),
            left.Denominator * right.Denominator);

    /// <summary>Subtracts one duration from another.</summary>
    /// <param name="left">The duration to subtract from.</param>
    /// <param name="right">The duration to subtract.</param>
    /// <returns>The difference.</returns>
    public static AbcDuration operator -(AbcDuration left, AbcDuration right) =>
        new AbcDuration(
            (left._numerator * right.Denominator) - (right._numerator * left.Denominator),
            left.Denominator * right.Denominator);

    /// <summary>Multiplies two durations.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns>The product.</returns>
    public static AbcDuration operator *(AbcDuration left, AbcDuration right) =>
        new AbcDuration(left._numerator * right._numerator, left.Denominator * right.Denominator);

    /// <summary>Multiplies a duration by a whole number.</summary>
    /// <param name="left">The duration.</param>
    /// <param name="right">The multiplier.</param>
    /// <returns>The product.</returns>
    public static AbcDuration operator *(AbcDuration left, long right) =>
        new AbcDuration(left._numerator * right, left.Denominator);

    /// <summary>Divides one duration by another.</summary>
    /// <param name="left">The duration to divide.</param>
    /// <param name="right">The divisor; must not be zero.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static AbcDuration operator /(AbcDuration left, AbcDuration right)
    {
        if (right.IsZero)
        {
            throw new DivideByZeroException("A duration cannot be divided by a zero duration.");
        }

        return new AbcDuration(left._numerator * right.Denominator, left.Denominator * right._numerator);
    }

    /// <summary>Divides a duration by a whole number.</summary>
    /// <param name="left">The duration to divide.</param>
    /// <param name="right">The divisor; must not be zero.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static AbcDuration operator /(AbcDuration left, long right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException("A duration cannot be divided by zero.");
        }

        return new AbcDuration(left._numerator, left.Denominator * right);
    }

    /// <summary>Whether two durations are the same length.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(AbcDuration left, AbcDuration right) => left.Equals(right);

    /// <summary>Whether two durations are different lengths.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(AbcDuration left, AbcDuration right) => !left.Equals(right);

    /// <summary>Whether the first duration is shorter than the second.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is shorter.</returns>
    public static bool operator <(AbcDuration left, AbcDuration right) => left.CompareTo(right) < 0;

    /// <summary>Whether the first duration is longer than the second.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is longer.</returns>
    public static bool operator >(AbcDuration left, AbcDuration right) => left.CompareTo(right) > 0;

    /// <summary>Whether the first duration is no longer than the second.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is shorter or equal.</returns>
    public static bool operator <=(AbcDuration left, AbcDuration right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the first duration is no shorter than the second.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is longer or equal.</returns>
    public static bool operator >=(AbcDuration left, AbcDuration right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Converts this duration to MIDI ticks, rounding to the nearest tick once, from the exact
    /// value.
    /// </summary>
    /// <param name="ticksPerQuarterNote">The resolution of the target collection.</param>
    /// <returns>The duration in ticks.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerQuarterNote"/> is not
    /// positive.</exception>
    /// <remarks>
    /// A whole note is four quarter notes, so the arithmetic is
    /// <c>numerator * 4 * ticksPerQuarterNote / denominator</c>, rounded half away from zero. Call
    /// it on an accumulated POSITION rather than accumulating ticks, which is what keeps a tuplet
    /// from pushing everything after it out of place.
    /// </remarks>
    public long ToTicks(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "Ticks per quarter note must be positive.");
        }

        long scaled = _numerator * 4L * ticksPerQuarterNote;
        long denominator = Denominator;
        long quotient = scaled / denominator;
        long remainder = scaled - (quotient * denominator);
        if (remainder == 0)
        {
            return quotient;
        }

        long twice = Math.Abs(remainder) * 2;
        if (twice >= denominator)
        {
            quotient += scaled < 0 ? -1 : 1;
        }

        return quotient;
    }

    /// <summary>
    /// Whether this duration lands exactly on a tick at the given resolution.
    /// </summary>
    /// <param name="ticksPerQuarterNote">The resolution to test against.</param>
    /// <returns><see langword="true"/> when no rounding is needed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerQuarterNote"/> is not
    /// positive.</exception>
    public bool IsWholeTicks(int ticksPerQuarterNote)
    {
        if (ticksPerQuarterNote <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "Ticks per quarter note must be positive.");
        }

        return (_numerator * 4L * ticksPerQuarterNote) % Denominator == 0;
    }

    /// <summary>Whether this duration equals another.</summary>
    /// <param name="other">The duration to compare with.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public bool Equals(AbcDuration other) => _numerator == other._numerator && Denominator == other.Denominator;

    /// <summary>Whether this duration equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when it is an equal duration.</returns>
    public override bool Equals(object obj) => obj is AbcDuration other && Equals(other);

    /// <summary>A hash code for this duration.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_numerator, Denominator);

    /// <summary>Orders this duration against another.</summary>
    /// <param name="other">The duration to compare with.</param>
    /// <returns>A negative number, zero or a positive number.</returns>
    public int CompareTo(AbcDuration other) =>
        (_numerator * other.Denominator).CompareTo(other._numerator * Denominator);

    /// <summary>Describes this duration as a fraction of a whole note.</summary>
    /// <returns>For example <c>"1/4"</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_numerator}/{Denominator}");

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            long remainder = left % right;
            left = right;
            right = remainder;
        }

        return left == 0 ? 1 : left;
    }
}

using System;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// The span of MIDI note numbers an instrument actually answers to, as an
/// <see cref="InstrumentCoverage"/> reports it.
/// </summary>
/// <remarks>
/// <para>
/// A synthesized instrument normally answers across the whole keyboard, so <see cref="Full"/> is
/// what most libraries report. A SAMPLED instrument usually does not: it holds recordings for the
/// range the real instrument plays and is silent outside it, which is the failure that looks like
/// a broken arrangement rather than like a missing sample.
/// </para>
/// <para>
/// The default value of this type is <see cref="Empty"/> - no note at all - so a range that was
/// never filled in reads as "nothing covered" rather than as "note zero covered".
/// </para>
/// </remarks>
public readonly struct InstrumentKeyRange : IEquatable<InstrumentKeyRange>
{
    private readonly int lowestKey;
    private readonly int keyCount;

    /// <summary>
    /// Creates a range over the notes from <paramref name="lowestKey"/> to
    /// <paramref name="highestKey"/> inclusive.
    /// </summary>
    /// <param name="lowestKey">The lowest MIDI note number covered, 0 to 127.</param>
    /// <param name="highestKey">The highest MIDI note number covered, 0 to 127.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either bound is outside 0 to 127, or <paramref name="highestKey"/> is below
    /// <paramref name="lowestKey"/>.
    /// </exception>
    public InstrumentKeyRange(int lowestKey, int highestKey)
    {
        if (lowestKey < 0 || lowestKey > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowestKey), lowestKey, "A MIDI note number is 0 to 127.");
        }

        if (highestKey < 0 || highestKey > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(highestKey), highestKey, "A MIDI note number is 0 to 127.");
        }

        if (highestKey < lowestKey)
        {
            throw new ArgumentOutOfRangeException(
                nameof(highestKey), highestKey, "The highest note cannot be below the lowest note.");
        }

        this.lowestKey = lowestKey;
        keyCount = highestKey - lowestKey + 1;
    }

    /// <summary>The whole MIDI keyboard, note 0 to note 127.</summary>
    public static InstrumentKeyRange Full => new InstrumentKeyRange(0, 127);

    /// <summary>No note at all - what an uncovered program reports.</summary>
    public static InstrumentKeyRange Empty => default;

    /// <summary>The lowest MIDI note number covered. Zero when the range is empty.</summary>
    public int LowestKey => keyCount == 0 ? 0 : lowestKey;

    /// <summary>The highest MIDI note number covered. Minus one when the range is empty.</summary>
    public int HighestKey => lowestKey + keyCount - 1;

    /// <summary>How many note numbers the range covers.</summary>
    public int KeyCount => keyCount;

    /// <summary>Whether the range covers no note at all.</summary>
    public bool IsEmpty => keyCount == 0;

    /// <summary>Whether a MIDI note number falls inside the range.</summary>
    /// <param name="key">The MIDI note number to test.</param>
    /// <returns>True when the instrument answers to that note.</returns>
    public bool Contains(int key) => keyCount != 0 && key >= lowestKey && key <= HighestKey;

    /// <summary>
    /// The range that covers every note either range covers, plus anything between them.
    /// </summary>
    /// <param name="other">The range to join with this one.</param>
    /// <returns>The joined range, or the non-empty one when the other is empty.</returns>
    public InstrumentKeyRange UnionWith(InstrumentKeyRange other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        return new InstrumentKeyRange(
            Math.Min(LowestKey, other.LowestKey),
            Math.Max(HighestKey, other.HighestKey));
    }

    /// <summary>Whether two ranges cover exactly the same notes.</summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns>True when both ranges are empty or both have the same bounds.</returns>
    public bool Equals(InstrumentKeyRange other) =>
        keyCount == other.keyCount && (keyCount == 0 || lowestKey == other.lowestKey);

    /// <summary>Whether this range covers exactly the same notes as another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when <paramref name="obj"/> is an equal range.</returns>
    public override bool Equals(object obj) => obj is InstrumentKeyRange other && Equals(other);

    /// <summary>A hash code consistent with <see cref="Equals(InstrumentKeyRange)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => keyCount == 0 ? 0 : HashCode.Combine(lowestKey, keyCount);

    /// <summary>The range as "35-81", or "(none)" when it is empty.</summary>
    /// <returns>A short description of the range.</returns>
    public override string ToString() => keyCount == 0 ? "(none)" : $"{LowestKey}-{HighestKey}";

    /// <summary>Whether two ranges cover exactly the same notes.</summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns>True when the two are equal.</returns>
    public static bool operator ==(InstrumentKeyRange left, InstrumentKeyRange right) => left.Equals(right);

    /// <summary>Whether two ranges cover different notes.</summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns>True when the two are not equal.</returns>
    public static bool operator !=(InstrumentKeyRange left, InstrumentKeyRange right) => !left.Equals(right);
}

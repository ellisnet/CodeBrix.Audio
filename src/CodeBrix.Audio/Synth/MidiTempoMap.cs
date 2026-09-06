using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// The tempo map of a MIDI sequence: every tempo change in it, and the arithmetic for converting
/// between wall-clock time and musical time in both directions.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="MidiSequence"/> flattens its tracks into absolute-time messages and applies the
/// tempo as it goes, so nothing downstream has to think about ticks. That is convenient for
/// playback and useless for anything musical, which is what this restores: given a position in
/// seconds it says which beat that is, and given a beat it says when that beat happens.
/// </para>
/// <para>
/// A map always has at least one entry. A sequence with no tempo event of its own gets the MIDI
/// default of 120 BPM at time zero, so <see cref="Changes"/> is never empty and the lookups never
/// need a special case.
/// </para>
/// <para>
/// Both lookups are a binary search over an array and allocate nothing, so they are safe to call
/// from a render callback.
/// </para>
/// </remarks>
public sealed class MidiTempoMap
{
    /// <summary>The tempo a sequence runs at when it carries no tempo event of its own.</summary>
    public const double DefaultBeatsPerMinute = 120.0;

    private readonly MidiTempoChange[] changes;

    /// <summary>Builds a tempo map from an ordered set of changes.</summary>
    /// <param name="changes">
    /// The changes, in ascending time order. When the collection is empty, or its first entry is
    /// not at time zero, a 120 BPM entry is prepended so the map covers the whole sequence.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    public MidiTempoMap(IReadOnlyList<MidiTempoChange> changes)
    {
        if (changes == null)
        {
            throw new ArgumentNullException(nameof(changes));
        }

        if (changes.Count == 0)
        {
            this.changes = [new MidiTempoChange(TimeSpan.Zero, 0.0, DefaultBeatsPerMinute)];
            return;
        }

        var needsDefault = changes[0].Time > TimeSpan.Zero || changes[0].BeatPosition > 0.0;
        var result = new MidiTempoChange[changes.Count + (needsDefault ? 1 : 0)];
        var index = 0;

        if (needsDefault)
        {
            result[index++] = new MidiTempoChange(TimeSpan.Zero, 0.0, DefaultBeatsPerMinute);
        }

        for (var i = 0; i < changes.Count; i++)
        {
            result[index++] = changes[i];
        }

        this.changes = result;
    }

    /// <summary>Every tempo change in the sequence, in ascending time order.</summary>
    public IReadOnlyList<MidiTempoChange> Changes => changes;

    /// <summary>Whether the sequence keeps one tempo from beginning to end.</summary>
    public bool IsConstant => changes.Length == 1;

    /// <summary>The tempo the sequence starts at, in quarter notes per minute.</summary>
    public double InitialBeatsPerMinute => changes[0].BeatsPerMinute;

    /// <summary>The tempo in force at a given moment, in quarter notes per minute.</summary>
    /// <param name="time">A position from the start of the sequence. Negative values read as zero.</param>
    /// <returns>The tempo at that moment.</returns>
    public double BeatsPerMinuteAt(TimeSpan time) => changes[IndexAt(time)].BeatsPerMinute;

    /// <summary>How many quarter-note beats have elapsed at a given moment.</summary>
    /// <param name="time">A position from the start of the sequence. Negative values read as zero.</param>
    /// <returns>The beat position, fractional and zero-based.</returns>
    public double BeatPositionAt(TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return 0.0;
        }

        var entry = changes[IndexAt(time)];
        var seconds = (time - entry.Time).TotalSeconds;
        return entry.BeatPosition + seconds * entry.BeatsPerMinute / 60.0;
    }

    /// <summary>When a given beat happens.</summary>
    /// <param name="beatPosition">A beat position from the start of the sequence, fractional and zero-based.</param>
    /// <returns>The time of that beat. Negative positions read as zero.</returns>
    public TimeSpan TimeAt(double beatPosition)
    {
        if (!(beatPosition > 0.0))
        {
            return TimeSpan.Zero;
        }

        var index = 0;
        var low = 0;
        var high = changes.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (changes[middle].BeatPosition <= beatPosition)
            {
                index = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        var entry = changes[index];
        var beats = beatPosition - entry.BeatPosition;
        return entry.Time + TimeSpan.FromTicks((long)(beats * 60.0 / entry.BeatsPerMinute * TimeSpan.TicksPerSecond));
    }

    // The index of the last change at or before the given time (0 for anything earlier).
    private int IndexAt(TimeSpan time)
    {
        var index = 0;
        var low = 0;
        var high = changes.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (changes[middle].Time <= time)
            {
                index = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return index;
    }
}

using System;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// One entry in a sequence's tempo map: the moment the tempo changed, the tempo it changed to, and
/// how many beats had already gone by.
/// </summary>
/// <remarks>
/// A sequence with no tempo event of its own still has one of these at time zero, carrying the MIDI
/// default of 120 BPM, so a tempo map is never empty.
/// </remarks>
public readonly struct MidiTempoChange : IEquatable<MidiTempoChange>
{
    /// <summary>Creates a tempo-map entry.</summary>
    /// <param name="time">When the change takes effect, from the start of the sequence.</param>
    /// <param name="beatPosition">How many quarter-note beats had elapsed at that moment.</param>
    /// <param name="beatsPerMinute">The tempo from this moment on, in quarter notes per minute.</param>
    public MidiTempoChange(TimeSpan time, double beatPosition, double beatsPerMinute)
    {
        Time = time;
        BeatPosition = beatPosition;
        BeatsPerMinute = beatsPerMinute;
    }

    /// <summary>When the change takes effect, from the start of the sequence.</summary>
    public TimeSpan Time { get; }

    /// <summary>How many quarter-note beats had elapsed when the change took effect.</summary>
    public double BeatPosition { get; }

    /// <summary>The tempo from this moment on, in quarter notes per minute.</summary>
    public double BeatsPerMinute { get; }

    /// <inheritdoc/>
    public bool Equals(MidiTempoChange other) =>
        Time == other.Time &&
        BeatPosition.Equals(other.BeatPosition) &&
        BeatsPerMinute.Equals(other.BeatsPerMinute);

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is MidiTempoChange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Time, BeatPosition, BeatsPerMinute);

    /// <summary>Whether two entries describe the same change.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when every field matches.</returns>
    public static bool operator ==(MidiTempoChange left, MidiTempoChange right) => left.Equals(right);

    /// <summary>Whether two entries describe different changes.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when any field differs.</returns>
    public static bool operator !=(MidiTempoChange left, MidiTempoChange right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Time} (beat {BeatPosition:0.###}): {BeatsPerMinute:0.###} BPM";
}

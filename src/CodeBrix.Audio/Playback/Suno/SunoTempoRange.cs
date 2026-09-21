using System.Collections.Generic;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// The lowest and highest tempos in a Suno song's tempo map, and the number of tempo events.
/// </summary>
public readonly struct SunoTempoRange
{
    internal SunoTempoRange(IReadOnlyList<SunoTempoChange> changes)
    {
        Count = changes.Count;
        if (Count == 0)
        {
            LowestBeatsPerMinute = 0;
            HighestBeatsPerMinute = 0;
            return;
        }

        var lowest = changes[0].BeatsPerMinute;
        var highest = lowest;
        for (var i = 1; i < Count; i++)
        {
            var tempo = changes[i].BeatsPerMinute;
            if (tempo < lowest)
            {
                lowest = tempo;
            }

            if (tempo > highest)
            {
                highest = tempo;
            }
        }

        LowestBeatsPerMinute = lowest;
        HighestBeatsPerMinute = highest;
    }

    /// <summary>The slowest tempo in BPM, or zero when the map is empty.</summary>
    public double LowestBeatsPerMinute { get; }

    /// <summary>The fastest tempo in BPM, or zero when the map is empty.</summary>
    public double HighestBeatsPerMinute { get; }

    /// <summary>The number of tempo events in the map.</summary>
    public int Count { get; }

    /// <summary>A short description of the range and event count.</summary>
    /// <returns>The range in BPM and its event count, or a message for an empty map.</returns>
    public override string ToString() => Count == 0
        ? "No tempo events"
        : $"{LowestBeatsPerMinute:0.###} - {HighestBeatsPerMinute:0.###} BPM over {Count} tempo events";
}

using System;
using System.Diagnostics;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>A common monotonic clock for all MidiConnect inputs in this process. It is not wall-clock time.</summary>
public static class MidiClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();
    /// <summary>Current time since this clock was initialized.</summary>
    public static TimeSpan Now => Stopwatch.GetElapsedTime(Origin);
}

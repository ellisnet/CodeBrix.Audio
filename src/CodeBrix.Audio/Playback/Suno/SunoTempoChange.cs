using System;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// One entry of a song's tempo map: the moment a tempo takes effect, and the tempo itself.
/// </summary>
/// <remarks>
/// A stems export written with Suno's "Follow tempo changes" option carries one of these per beat,
/// so a four-minute song has several hundred. The map is identical in every MIDI stem of a song.
/// </remarks>
public readonly struct SunoTempoChange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SunoTempoChange"/> structure.
    /// </summary>
    /// <param name="time">When the tempo takes effect, measured from the start of the song.</param>
    /// <param name="beatsPerMinute">The tempo in quarter-note beats per minute.</param>
    public SunoTempoChange(TimeSpan time, double beatsPerMinute)
    {
        Time = time;
        BeatsPerMinute = beatsPerMinute;
    }

    /// <summary>When the tempo takes effect, measured from the start of the song.</summary>
    public TimeSpan Time { get; }

    /// <summary>The tempo in quarter-note beats per minute.</summary>
    public double BeatsPerMinute { get; }

    /// <summary>The same tempo expressed the way a MIDI file stores it, in microseconds per quarter note.</summary>
    public double MicrosecondsPerQuarterNote => BeatsPerMinute > 0 ? 60000000.0 / BeatsPerMinute : 0;

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>The time and tempo.</returns>
    public override string ToString() => $"{Time} {BeatsPerMinute:0.###} BPM";
}

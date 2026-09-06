using System;
using System.Threading;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// The live musical clock of a running transport: how fast the music is going, and how far into it
/// the transport has travelled, expressed in beats rather than in seconds.
/// </summary>
/// <remarks>
/// <para>
/// A transport WRITES this - <c>CodeBrix.Audio.Playback.MultiTrackPlayer</c> and
/// <c>CodeBrix.Audio.Playback.MidiMusicPlayer</c> both publish one, fed from the tempo map of the
/// MIDI they are playing - and anything that needs musical time READS it. That is how a
/// tempo-synced effect (a delay in dotted eighths, an LFO in bars) knows what "an eighth note" is
/// worth at this instant.
/// </para>
/// <para>
/// Every read and write is lock-free (<see cref="Volatile"/>), because the writer is usually the
/// real-time audio thread and the readers may be anything - a UI poll, another audio component.
/// No individual value ever tears, but the four are not written atomically together: a reader that
/// samples <see cref="BeatsPerMinute"/> and <see cref="BeatPosition"/> in two statements can see
/// them one render block apart. That is a few milliseconds and never matters musically; if it
/// does, read once into locals and work from those.
/// </para>
/// <para>
/// A tempo source that no transport is driving simply reports its defaults: 120 BPM, beat 0,
/// 4 beats to the bar, not playing.
/// </para>
/// </remarks>
public sealed class TempoSource
{
    /// <summary>The tempo reported before a transport has set one.</summary>
    public const double DefaultBeatsPerMinute = 120.0;

    /// <summary>The number of beats per bar reported before a transport has set one.</summary>
    public const int DefaultBeatsPerBar = 4;

    private double beatsPerMinute = DefaultBeatsPerMinute;
    private double beatPosition;
    private int beatsPerBar = DefaultBeatsPerBar;
    private int isPlaying;

    /// <summary>
    /// The tempo at the transport's current position, in quarter notes per minute. Never zero or
    /// negative: a value that is not positive is ignored.
    /// </summary>
    public double BeatsPerMinute
    {
        get => Volatile.Read(ref beatsPerMinute);
        set
        {
            if (value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                Volatile.Write(ref beatsPerMinute, value);
            }
        }
    }

    /// <summary>
    /// How far the transport has travelled, in quarter-note beats from the start of the song.
    /// Fractional: 6.5 is halfway through the seventh beat. Negative values are clamped to zero.
    /// </summary>
    public double BeatPosition
    {
        get => Volatile.Read(ref beatPosition);
        set
        {
            var clamped = value > 0.0 && !double.IsNaN(value) ? value : 0.0;
            Volatile.Write(ref beatPosition, clamped);
        }
    }

    /// <summary>
    /// The number of beats in a bar - the numerator of the time signature, for music in quarters.
    /// Defaults to <see cref="DefaultBeatsPerBar"/>. Values below 1 are ignored.
    /// </summary>
    public int BeatsPerBar
    {
        get => Volatile.Read(ref beatsPerBar);
        set
        {
            if (value >= 1)
            {
                Volatile.Write(ref beatsPerBar, value);
            }
        }
    }

    /// <summary>Whether the transport driving this source is currently running.</summary>
    public bool IsPlaying
    {
        get => Volatile.Read(ref isPlaying) != 0;
        set => Volatile.Write(ref isPlaying, value ? 1 : 0);
    }

    /// <summary>
    /// The bar the transport is in, zero-based and fractional: 2.25 is a quarter of the way
    /// through the third bar.
    /// </summary>
    public double BarPosition => BeatPosition / BeatsPerBar;

    /// <summary>The length of one beat at the current tempo.</summary>
    public TimeSpan BeatDuration =>
        TimeSpan.FromTicks((long)(TimeSpan.TicksPerMinute / BeatsPerMinute));

    /// <summary>
    /// Publishes a whole reading at once - what a transport calls each render block.
    /// </summary>
    /// <param name="beatsPerMinute">The tempo at the new position, in quarter notes per minute.</param>
    /// <param name="beatPosition">The new position, in beats from the start of the song.</param>
    /// <param name="isPlaying">Whether the transport is running.</param>
    /// <remarks>
    /// A convenience over the three setters, not an atomic write: see the class remarks. It is
    /// allocation-free and safe to call from a render callback.
    /// </remarks>
    public void Update(double beatsPerMinute, double beatPosition, bool isPlaying)
    {
        BeatsPerMinute = beatsPerMinute;
        BeatPosition = beatPosition;
        IsPlaying = isPlaying;
    }

    /// <summary>
    /// Returns the source to its defaults: 120 BPM, beat 0, four beats to the bar, not playing.
    /// </summary>
    /// <remarks><see cref="BeatsPerBar"/> is left alone - it describes the music, not the position.</remarks>
    public void Reset()
    {
        Volatile.Write(ref beatsPerMinute, DefaultBeatsPerMinute);
        Volatile.Write(ref beatPosition, 0.0);
        Volatile.Write(ref isPlaying, 0);
    }
}

using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Writes the five-tone "Close Encounters" motif into a <see cref="MidiStream"/> a bar at a time,
/// on demand, so a test drives the producer from its own thread.
/// </summary>
/// <remarks>
/// <para>
/// Same tune as the rest of the suite plays, and for the same reason: one recognisable phrase
/// means a good run is obvious by ear and a broken one sounds broken. Here it arrives the way a
/// growing timeline's does - bar by bar, while playback is already under way.
/// </para>
/// <para>
/// There are no timers, no sleeps and no real time anywhere: every bar is appended by an explicit
/// call, so what a test renders is a pure function of what it asked for.
/// </para>
/// </remarks>
internal sealed class StreamingTestProducer
{
    /// <summary>The resolution the producer writes at.</summary>
    public const int TicksPerQuarterNote = 480;

    /// <summary>How many ticks a bar of four quarter notes takes.</summary>
    public const int TicksPerBar = 4 * TicksPerQuarterNote;

    /// <summary>How long each note of the motif sounds, leaving a gap before the next bar.</summary>
    public const int NoteTicks = 3 * TicksPerQuarterNote;

    /// <summary>The velocity every note is struck at.</summary>
    public const int Velocity = 100;

    /// <summary>The channel the motif is played on.</summary>
    public const int Channel = 1;

    private static readonly int[] MotifNotes = [79, 81, 77, 65, 72];

    private readonly MidiStream stream;
    private int barsAppended;

    /// <summary>Creates a producer that writes into the given stream.</summary>
    /// <param name="stream">The stream to append to.</param>
    public StreamingTestProducer(MidiStream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        this.stream = stream;
        TempoChangeBar = -1;
        TempoChangeBeatsPerMinute = 90.0;
    }

    /// <summary>The motif, in order: G5, A5, F5, F4, C5.</summary>
    public static IReadOnlyList<int> Notes => MotifNotes;

    /// <summary>How many bars the motif runs for - one note each.</summary>
    public static int BarCount => MotifNotes.Length;

    /// <summary>How many bars have been appended so far.</summary>
    public int BarsAppended => barsAppended;

    /// <summary>
    /// Whether a 3/4 time signature is written at tick zero with the first bar. Off by default.
    /// </summary>
    public bool IncludeTimeSignature { get; set; }

    /// <summary>
    /// The bar a tempo change is written at the start of, or -1 for none. Off by default.
    /// </summary>
    public int TempoChangeBar { get; set; }

    /// <summary>The tempo <see cref="TempoChangeBar"/> changes to.</summary>
    public double TempoChangeBeatsPerMinute { get; set; }

    /// <summary>The tick a bar starts at.</summary>
    /// <param name="bar">The bar number, counting from zero.</param>
    /// <returns>The tick the bar's note is written at.</returns>
    public static long TickOfBar(int bar) => (long)bar * TicksPerBar;

    /// <summary>
    /// Appends the next bar of the motif, if there is one left.
    /// </summary>
    /// <returns><see langword="true"/> if a bar was appended; <see langword="false"/> when the motif is complete.</returns>
    public bool AppendNextBar()
    {
        if (barsAppended >= MotifNotes.Length)
        {
            return false;
        }

        var bar = barsAppended;
        var tick = TickOfBar(bar);

        if (bar == 0 && IncludeTimeSignature)
        {
            stream.Append(new TimeSignatureEvent(0, 3, 2, 24, 8));
        }

        if (bar == TempoChangeBar)
        {
            stream.AppendTempo(tick, TempoChangeBeatsPerMinute);
        }

        stream.AppendNote(tick, Channel, MotifNotes[bar], Velocity, NoteTicks);

        barsAppended++;
        return true;
    }

    /// <summary>Appends every bar that has not been appended yet.</summary>
    public void AppendAllBars()
    {
        while (AppendNextBar())
        {
        }
    }

    /// <summary>Appends every remaining bar and tells the stream that is all there is.</summary>
    public void AppendAllBarsAndComplete()
    {
        AppendAllBars();
        stream.Complete();
    }
}

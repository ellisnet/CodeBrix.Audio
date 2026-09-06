using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// The one musical-time subdivision table the format shares between a delay in
/// <c>musical_time</c> mode, an LFO with <c>frequencyFormat="musical_time"</c>, the arpeggiator's
/// <c>arpSyncDivision</c>, and a UI control of <c>valueType="musical_time"</c>.
/// </summary>
/// <remarks>
/// <para>
/// The developer guide gives only four points of the list (10 = 1/8, 13 = 1/4, 19 = 1, 24 = 5/1). The
/// remaining twenty-one were measured against the reference player by timing the echoes of a delay
/// driven from a MIDI CC, one index per CC value.
/// </para>
/// <para>
/// Indices 1 to 18 run in triples of [straight, triplet, dotted] doubling from a sixty-fourth;
/// 19 to 24 are the long values. Index 0 is a 1/64 triplet - the list starts in the middle of a triple.
/// Anything above 24 clamps to 5/1, as the reference player does.
/// </para>
/// </remarks>
public static class DecentSamplerMusicalTime
{
    // In whole notes. Measured to better than 0.1 % at 120 BPM.
    private static readonly double[] WholeNotes =
    [
        1.0 / 96.0,     //  0  1/64 triplet
        1.0 / 64.0,     //  1  1/64
        1.0 / 48.0,     //  2  1/32 triplet
        3.0 / 128.0,    //  3  dotted 1/64
        1.0 / 32.0,     //  4  1/32
        1.0 / 24.0,     //  5  1/16 triplet
        3.0 / 64.0,     //  6  dotted 1/32
        1.0 / 16.0,     //  7  1/16
        1.0 / 12.0,     //  8  1/8 triplet
        3.0 / 32.0,     //  9  dotted 1/16
        1.0 / 8.0,      // 10  1/8
        1.0 / 6.0,      // 11  1/4 triplet
        3.0 / 16.0,     // 12  dotted 1/8
        1.0 / 4.0,      // 13  1/4
        1.0 / 3.0,      // 14  1/2 triplet
        3.0 / 8.0,      // 15  dotted 1/4
        1.0 / 2.0,      // 16  1/2
        2.0 / 3.0,      // 17  whole-note triplet
        3.0 / 4.0,      // 18  dotted 1/2
        1.0,            // 19  1/1
        3.0 / 2.0,      // 20  dotted 1/1
        2.0,            // 21  2/1
        3.0,            // 22  3/1
        4.0,            // 23  4/1
        5.0,            // 24  5/1
    ];

    /// <summary>How many subdivisions the table holds: indices 0 to 24.</summary>
    public static int Count => WholeNotes.Length;

    /// <summary>
    /// The length of a subdivision in whole notes - four quarter-note beats to the whole note.
    /// </summary>
    /// <param name="index">The subdivision index. Below 0 reads as 0; above 24 clamps to 5/1.</param>
    /// <returns>The length in whole notes.</returns>
    public static double WholeNotesAt(int index) =>
        WholeNotes[Math.Clamp(index, 0, WholeNotes.Length - 1)];

    /// <summary>
    /// The length of a subdivision in seconds at a tempo.
    /// </summary>
    /// <param name="index">The subdivision index. Below 0 reads as 0; above 24 clamps to 5/1.</param>
    /// <param name="beatsPerMinute">The tempo in quarter notes per minute.</param>
    /// <returns>The length in seconds, or 0 when the tempo is not positive.</returns>
    public static double SecondsAt(int index, double beatsPerMinute)
    {
        if (!(beatsPerMinute > 0.0))
        {
            return 0.0;
        }

        return WholeNotesAt(index) * 4.0 * 60.0 / beatsPerMinute;
    }

    /// <summary>
    /// The length of a subdivision in quarter-note beats.
    /// </summary>
    /// <param name="index">The subdivision index. Below 0 reads as 0; above 24 clamps to 5/1.</param>
    /// <returns>The length in beats.</returns>
    public static double BeatsAt(int index) => WholeNotesAt(index) * 4.0;
}

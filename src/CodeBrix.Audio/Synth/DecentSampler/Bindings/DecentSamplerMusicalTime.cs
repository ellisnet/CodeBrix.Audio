using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The one subdivision table the format shares between the delay effect's <c>musical_time</c> format,
/// an LFO's <c>frequencyFormat</c>, the arpeggiator's <c>arpSyncDivision</c> and a control whose
/// <c>valueType</c> is <c>musical_time</c>.
/// </summary>
/// <remarks>
/// <para>
/// The developer guide names only four points of the list (10 = 1/8, 13 = 1/4, 19 = 1, 24 = 5/1). The
/// rest of the table below was measured against the reference player: a delay in <c>musical_time</c>
/// mode was driven from a MIDI CC whose translated output was the subdivision index, and the spacing
/// of the echoes was measured for every index. Indices 1 to 24 came back to better than 0.1 %, and
/// anything above 24 clamps to 5/1.
/// </para>
/// <para>
/// Index 0 was the one row the measurement could not set, because a controller's stored value starts
/// at 0 and a binding fires on a CC <em>change</em>. It is settled instead by the guide's own
/// <c>arpSyncDivision</c> list, which names exactly twenty-one note values in the same order as
/// measured indices 0 to 20: <c>noteOneSixtyFourthTriplet</c> lines up with index 0, and every one of
/// the twenty names after it matches a measured value. Index 0 is therefore a 1/64 triplet, one
/// ninety-sixth of a whole note.
/// </para>
/// <para>
/// Values are in WHOLE NOTES. One beat is a quarter note, so beats = wholeNotes * 4 and, at T beats
/// per minute, seconds = wholeNotes * 4 * 60 / T.
/// </para>
/// </remarks>
internal static class DecentSamplerMusicalTime
{
    /// <summary>The highest index the table holds. Anything above it clamps to this entry.</summary>
    internal const int HighestIndex = 24;

    // Indices 0..18 are [straight, triplet, dotted] triples doubling from a 1/64 triplet; 19..24 are
    // 1/1, dotted 1/1, 2/1, 3/1, 4/1 and 5/1.
    private static readonly double[] WholeNoteLengths =
    [
        1.0 / 96.0, 1.0 / 64.0, 1.0 / 48.0, 3.0 / 128.0, 1.0 / 32.0, 1.0 / 24.0,
        3.0 / 64.0, 1.0 / 16.0, 1.0 / 12.0, 3.0 / 32.0, 1.0 / 8.0, 1.0 / 6.0,
        3.0 / 16.0, 1.0 / 4.0, 1.0 / 3.0, 3.0 / 8.0, 1.0 / 2.0, 2.0 / 3.0,
        3.0 / 4.0, 1.0, 3.0 / 2.0, 2.0, 3.0, 4.0, 5.0,
    ];

    private static readonly string[] SubdivisionNames =
    [
        "1/64 triplet", "1/64", "1/32 triplet", "dotted 1/64", "1/32", "1/16 triplet",
        "dotted 1/32", "1/16", "1/8 triplet", "dotted 1/16", "1/8", "1/4 triplet",
        "dotted 1/8", "1/4", "1/2 triplet", "dotted 1/4", "1/2", "whole-note triplet",
        "dotted 1/2", "1/1", "dotted 1/1", "2/1", "3/1", "4/1", "5/1",
    ];

    /// <summary>How many entries the table has.</summary>
    internal static int Count => WholeNoteLengths.Length;

    /// <summary>Brings an index into the table's range, clamping at both ends.</summary>
    /// <param name="index">Any index, in range or not.</param>
    /// <returns>An index between 0 and <see cref="HighestIndex"/>.</returns>
    internal static int Clamp(int index) => index < 0 ? 0 : index > HighestIndex ? HighestIndex : index;

    /// <summary>The length of one subdivision in whole notes.</summary>
    /// <param name="index">The subdivision index; out-of-range values clamp.</param>
    /// <returns>The length in whole notes.</returns>
    internal static double WholeNotesAt(int index) => WholeNoteLengths[Clamp(index)];

    /// <summary>The length of one subdivision in beats, a beat being a quarter note.</summary>
    /// <param name="index">The subdivision index; out-of-range values clamp.</param>
    /// <returns>The length in beats.</returns>
    internal static double BeatsAt(int index) => WholeNoteLengths[Clamp(index)] * 4.0;

    /// <summary>A human-readable name for one subdivision, such as <c>dotted 1/8</c>.</summary>
    /// <param name="index">The subdivision index; out-of-range values clamp.</param>
    /// <returns>The note value's name.</returns>
    internal static string NameAt(int index) => SubdivisionNames[Clamp(index)];

    /// <summary>
    /// The subdivision index an <c>arpSyncDivision</c> value stands for. The twenty-one documented
    /// names are the table's first twenty-one entries, in the same order.
    /// </summary>
    /// <param name="division">The sync division.</param>
    /// <returns>The subdivision index.</returns>
    internal static int IndexOf(DecentSamplerSyncDivision division) => Clamp((int)division);
}

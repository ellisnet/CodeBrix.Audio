using System;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Turns a Decent Sampler musical-time subdivision index into a length in beats or seconds.
/// </summary>
/// <remarks>
/// <para>
/// One subdivision list serves the whole format: a delay whose <c>delayTimeFormat</c> is
/// <c>musical_time</c>, an LFO whose <c>frequencyFormat</c> is <c>musical_time</c>, the arpeggiator's
/// <c>arpSyncDivision</c>, and a control whose <c>valueType</c> is <c>musical_time</c> all index the
/// same twenty-five entries. Index 0 is a 1/64 triplet, 10 is an eighth note, 13 a quarter note,
/// 19 a whole note and 24 five whole notes; anything above 24 clamps to that last entry.
/// </para>
/// <para>
/// A beat is a quarter note here, matching <see cref="TempoSource.BeatsPerMinute"/>. With no host to
/// follow, the reference player runs at 120 BPM, which is also this library's
/// <see cref="TempoSource.DefaultBeatsPerMinute"/>.
/// </para>
/// </remarks>
public static class DecentSamplerTempo
{
    /// <summary>How many subdivisions the table holds. Indices run from 0 to this minus one.</summary>
    public static int SubdivisionCount => DecentSamplerMusicalTime.Count;

    /// <summary>The length of one subdivision in beats, a beat being a quarter note.</summary>
    /// <param name="subdivisionIndex">The subdivision index. Out-of-range values clamp.</param>
    /// <returns>The length in beats.</returns>
    public static double BeatsForSubdivision(int subdivisionIndex) =>
        DecentSamplerMusicalTime.BeatsAt(subdivisionIndex);

    /// <summary>The length of one subdivision in whole notes.</summary>
    /// <param name="subdivisionIndex">The subdivision index. Out-of-range values clamp.</param>
    /// <returns>The length in whole notes, where a quarter note is 0.25.</returns>
    public static double WholeNotesForSubdivision(int subdivisionIndex) =>
        DecentSamplerMusicalTime.WholeNotesAt(subdivisionIndex);

    /// <summary>A human-readable name for one subdivision, such as <c>dotted 1/8</c>.</summary>
    /// <param name="subdivisionIndex">The subdivision index. Out-of-range values clamp.</param>
    /// <returns>The note value's name.</returns>
    public static string DescribeSubdivision(int subdivisionIndex) =>
        DecentSamplerMusicalTime.NameAt(subdivisionIndex);

    /// <summary>The length of one subdivision in seconds at a given tempo.</summary>
    /// <param name="subdivisionIndex">The subdivision index. Out-of-range values clamp.</param>
    /// <param name="beatsPerMinute">The tempo in quarter notes per minute. Must be above zero.</param>
    /// <returns>The length in seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="beatsPerMinute"/> is not above zero.</exception>
    public static double SecondsForSubdivision(int subdivisionIndex, double beatsPerMinute)
    {
        if (!(beatsPerMinute > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beatsPerMinute), beatsPerMinute, "The tempo must be above zero.");
        }

        return DecentSamplerMusicalTime.BeatsAt(subdivisionIndex) * 60.0 / beatsPerMinute;
    }

    /// <summary>The length of one subdivision in seconds at a tempo source's current tempo.</summary>
    /// <param name="subdivisionIndex">The subdivision index. Out-of-range values clamp.</param>
    /// <param name="tempo">The tempo source, or null to use 120 BPM.</param>
    /// <returns>The length in seconds.</returns>
    public static double SecondsForSubdivision(int subdivisionIndex, TempoSource tempo) =>
        SecondsForSubdivision(
            subdivisionIndex, tempo?.BeatsPerMinute ?? TempoSource.DefaultBeatsPerMinute);

    /// <summary>The subdivision index an arpeggiator sync division stands for.</summary>
    /// <param name="division">The <c>arpSyncDivision</c> value.</param>
    /// <returns>The subdivision index, 0 to 20.</returns>
    public static int SubdivisionOf(DecentSamplerSyncDivision division) =>
        DecentSamplerMusicalTime.IndexOf(division);
}

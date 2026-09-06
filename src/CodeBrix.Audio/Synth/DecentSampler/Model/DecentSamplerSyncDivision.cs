namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A musical note value, used by the arpeggiator's <c>arpSyncDivision</c>.
/// </summary>
/// <remarks>
/// The members are listed in the order the developer guide gives them, shortest first.
/// </remarks>
public enum DecentSamplerSyncDivision
{
    /// <summary>A 64th-note triplet.</summary>
    NoteOneSixtyFourthTriplet,

    /// <summary>A 64th note.</summary>
    NoteOneSixtyFourth,

    /// <summary>A 32nd-note triplet.</summary>
    NoteOneThirtySecondTriplet,

    /// <summary>A dotted 64th note.</summary>
    NoteOneSixtyFourthDotted,

    /// <summary>A 32nd note.</summary>
    NoteOneThirtySecond,

    /// <summary>A 16th-note triplet.</summary>
    NoteOneSixteenthTriplet,

    /// <summary>A dotted 32nd note.</summary>
    NoteOneThirtySecondDotted,

    /// <summary>A 16th note. The arpeggiator default.</summary>
    NoteOneSixteenth,

    /// <summary>An eighth-note triplet.</summary>
    NoteOneEighthTriplet,

    /// <summary>A dotted 16th note.</summary>
    NoteOneSixteenthDotted,

    /// <summary>An eighth note.</summary>
    NoteOneEighth,

    /// <summary>A quarter-note triplet.</summary>
    NoteOneFourthTriplet,

    /// <summary>A dotted eighth note.</summary>
    NoteOneEighthDotted,

    /// <summary>A quarter note.</summary>
    NoteOneFourth,

    /// <summary>A half-note triplet.</summary>
    NoteOneHalfTriplet,

    /// <summary>A dotted quarter note.</summary>
    NoteOneFourthDotted,

    /// <summary>A half note.</summary>
    NoteOneHalf,

    /// <summary>A whole-note triplet.</summary>
    NoteOneTriplet,

    /// <summary>A dotted half note.</summary>
    NoteOneHalfDotted,

    /// <summary>A whole note.</summary>
    NoteWhole,

    /// <summary>A dotted whole note.</summary>
    NoteWholeDotted,
}

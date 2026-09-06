using System;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// The outcome of estimating how far a MIDI transcription sits from the audio it was
/// transcribed from. Produced by <see cref="MidiAudioAlignment"/>.
/// </summary>
/// <remarks>
/// <para>
/// The sign convention is fixed and is the one thing to get right when applying a result:
/// <c>audioTime = midiTime + OffsetSeconds</c>. A POSITIVE offset means the audio LAGS the
/// MIDI - the recorded note sounds later than the note-on says it should - so a MIDI track
/// played against that audio has to be delayed by <see cref="OffsetSeconds"/> to line up.
/// </para>
/// <para>
/// Always look at <see cref="IsReliable"/> before using <see cref="OffsetSeconds"/>. A stem
/// with too few notes, an audio file that is mostly silence, or material so periodic that the
/// search cannot tell one beat from the next all produce a number that is arithmetically
/// correct and musically meaningless.
/// </para>
/// </remarks>
public sealed class MidiAudioAlignmentResult
{
    /// <summary>
    /// Creates a result. Consumers receive these from <see cref="MidiAudioAlignment"/>; the
    /// constructor is public so a host can substitute a hand-measured or user-entered value.
    /// </summary>
    /// <param name="offsetSeconds">
    /// The estimated offset, where <c>audioTime = midiTime + offsetSeconds</c>.
    /// </param>
    /// <param name="peakCorrelation">The normalised height of the winning correlation peak.</param>
    /// <param name="confidence">How much to trust the estimate, from 0 to 1.</param>
    /// <param name="isReliable">Whether the estimate passed the reliability test.</param>
    /// <param name="noteOnCount">How many note-on impulses the estimate was built from.</param>
    /// <param name="runnerUpCorrelation">
    /// The height of the best competing peak elsewhere in the search window.
    /// </param>
    /// <param name="candidatePeakCount">
    /// How many rival peaks the search found; see <see cref="CandidatePeakCount"/>. One means
    /// the winner had no rival worth considering.
    /// </param>
    public MidiAudioAlignmentResult(double offsetSeconds, double peakCorrelation, double confidence,
        bool isReliable, int noteOnCount, double runnerUpCorrelation, int candidatePeakCount = 1)
    {
        OffsetSeconds = offsetSeconds;
        PeakCorrelation = peakCorrelation;
        Confidence = confidence;
        IsReliable = isReliable;
        NoteOnCount = noteOnCount;
        RunnerUpCorrelation = runnerUpCorrelation;
        CandidatePeakCount = candidatePeakCount;
    }

    /// <summary>
    /// The estimated offset in seconds, where <c>audioTime = midiTime + OffsetSeconds</c>.
    /// Positive means the audio lags the MIDI. Zero when nothing could be estimated.
    /// </summary>
    public double OffsetSeconds { get; }

    /// <summary>
    /// The estimated offset as a <see cref="TimeSpan"/>, ready to assign to a track offset.
    /// Same sign convention as <see cref="OffsetSeconds"/>.
    /// </summary>
    public TimeSpan Offset => TimeSpan.FromSeconds(OffsetSeconds);

    /// <summary>
    /// The height of the winning correlation peak, normalised so that 1.0 would be a perfect
    /// match between the note-on impulse train and the audio's onset envelope. Real music
    /// scores well below that - a good drum stem lands somewhere around 0.1 to 0.4 - so this
    /// is a diagnostic, not a threshold. Use <see cref="Confidence"/> to decide.
    /// </summary>
    public double PeakCorrelation { get; }

    /// <summary>
    /// The height of the best competing peak outside the winner's immediate neighbourhood.
    /// Together with <see cref="PeakCorrelation"/> this is what <see cref="Confidence"/> is
    /// computed from: a peak that only just beats its nearest rival is not a decision.
    /// </summary>
    public double RunnerUpCorrelation { get; }

    /// <summary>
    /// How much to trust <see cref="OffsetSeconds"/>, from 0 (no better than a guess) to 1
    /// (the peak stands far above everything else in the search window).
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// True when the estimate is worth applying without asking: enough note-ons went into it,
    /// the peak is positive, and <see cref="Confidence"/> cleared the threshold. When this is
    /// false, fall back to a per-song offset or to no offset at all rather than to
    /// <see cref="OffsetSeconds"/>.
    /// </summary>
    public bool IsReliable { get; }

    /// <summary>
    /// How many note-on impulses the estimate was built from, after collapsing note-ons that
    /// fall in the same millisecond. Fewer than a handful and no estimate is meaningful.
    /// </summary>
    public int NoteOnCount { get; }

    /// <summary>
    /// How many rival peaks the search found - peaks within
    /// <see cref="MidiAudioAlignment.CandidatePeakFraction"/> of the tallest one. One means the
    /// winner stood alone and the answer is whatever the correlation said. More than one means
    /// the material is periodic enough to offer the search a choice, the choice was made on
    /// coarse envelope agreement rather than on peak height, and the offset may still be a beat
    /// multiple away from the truth. Widening the search window raises this number.
    /// </summary>
    public int CandidatePeakCount { get; }

    /// <summary>
    /// A short human-readable form: the offset in milliseconds, the confidence and the note count.
    /// </summary>
    /// <returns>A diagnostic string; the format is not part of the contract.</returns>
    public override string ToString() =>
        $"{OffsetSeconds * 1000.0:+0.0;-0.0;0.0} ms, confidence {Confidence:0.00}" +
        $"{(IsReliable ? string.Empty : " (not reliable)")}, {NoteOnCount} note-ons";
}

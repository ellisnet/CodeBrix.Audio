using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// What one alignment measurement produced: how far the audio sits from the MIDI, and how much the
/// estimator believes its own answer.
/// </summary>
internal readonly struct SunoAlignmentEstimate
{
    internal SunoAlignmentEstimate(double offsetSeconds, double confidence, bool isReliable)
    {
        OffsetSeconds = offsetSeconds;
        Confidence = confidence;
        IsReliable = isReliable;
    }

    /// <summary>
    /// Where the audio sits relative to the MIDI: audioTime = midiTime + OffsetSeconds. Positive
    /// means the audio lags the MIDI.
    /// </summary>
    internal double OffsetSeconds { get; }

    /// <summary>How strong the match was, 0 to 1.</summary>
    internal double Confidence { get; }

    /// <summary>Whether the estimator considers the answer usable.</summary>
    internal bool IsReliable { get; }
}

/// <summary>
/// Estimates how far a stem's audio sits from its MIDI.
/// </summary>
/// <param name="noteOnTimesSeconds">Every note-on time in the stem's MIDI, in seconds, ascending.</param>
/// <param name="monoAudio">The stem's audio downmixed to mono.</param>
/// <param name="sampleRate">The sample rate of <paramref name="monoAudio"/>.</param>
/// <param name="maximumOffsetSeconds">How far to look in either direction.</param>
/// <returns>The estimate.</returns>
internal delegate SunoAlignmentEstimate SunoAlignmentEstimator(
    IReadOnlyList<double> noteOnTimesSeconds, float[] monoAudio, int sampleRate, double maximumOffsetSeconds);

/// <summary>
/// THE SEAM THE ALIGNMENT ESTIMATOR PLUGS INTO. The loader measures a stem's alignment offset by
/// calling <see cref="Estimator"/>, which starts out as <see cref="Default"/> -
/// <see cref="MidiAudioAlignment"/>. Setting it to null turns measurement off: every stem then
/// keeps an offset of zero and <see cref="SunoStem.AlignmentMeasured"/> stays false.
/// </summary>
/// <remarks>
/// The property is settable so that a test can install a stub and see that the loader asks the
/// right question; such a test must put <see cref="ResetToDefault"/> in a finally block, because
/// this is a process-wide static. It is read once per stem, on a worker thread, during a load.
/// </remarks>
internal static class SunoAlignmentSeam
{
    /// <summary>
    /// The real estimator: <see cref="MidiAudioAlignment.Estimate(IReadOnlyList{double}, ReadOnlySpan{float}, int, double)"/>,
    /// with its result mapped onto this seam's own small shape. Both sides use the same sign
    /// convention, <c>audioTime = midiTime + offset</c>.
    /// </summary>
    internal static SunoAlignmentEstimator Default { get; } = (noteOns, audio, rate, maximum) =>
    {
        var result = MidiAudioAlignment.Estimate(noteOns, audio, rate, maximum);
        return new SunoAlignmentEstimate(result.OffsetSeconds, result.Confidence, result.IsReliable);
    };

    /// <summary>
    /// The installed estimator. <see cref="Default"/> unless something replaced it; null turns
    /// measurement off altogether.
    /// </summary>
    internal static SunoAlignmentEstimator Estimator { get; set; } = Default;

    /// <summary>Puts <see cref="Default"/> back. What a test's finally block calls.</summary>
    internal static void ResetToDefault() => Estimator = Default;
}

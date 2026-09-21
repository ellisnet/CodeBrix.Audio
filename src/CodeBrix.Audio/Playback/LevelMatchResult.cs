using System;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// What a level measurement found: how many tracks were matched, how loud the matched mix will be,
/// and the master <see cref="MultiTrackPlayer.Volume"/> that would make it fit.
/// </summary>
/// <remarks>
/// <para>
/// MATCHING THE LEVELS ROUTINELY PUSHES A MIX PAST FULL SCALE, and that is not a fault in the
/// measurement. A General MIDI drum kit is quiet where a mastered drum recording is loud, so
/// matching the rendition to the recording means a large make-up gain on that track, and several
/// large gains add up. The measurement has already rendered everything, so it also renders the
/// matched mix once and reports what it peaks at.
/// </para>
/// <para>
/// NOTHING IS APPLIED. <see cref="MultiTrackPlayer.Render(int, TimeSpan?)"/> still limits and
/// normalises nothing, and the measurement still writes
/// <see cref="PlayerTrack.MidiSourceGain"/> and nothing else. This is a report; acting on it is one
/// line, and <see cref="MultiTrackPlayer.FitVolumeAfterLevelMatching"/> is that line made automatic.
/// </para>
/// <code>
/// player.MeasureRelativeTrackLevels();
///
/// var match = player.LastLevelMatch;
/// if (match != null &amp;&amp; match.WouldClip)
/// {
///     player.Volume = match.SuggestedVolume;      // the mix now lands exactly at full scale
/// }
/// </code>
/// </remarks>
public sealed class LevelMatchResult
{
    internal LevelMatchResult(int sampleRate, int matchedTrackCount, float mixPeak)
    {
        SampleRate = sampleRate;
        MatchedTrackCount = matchedTrackCount;
        MixPeak = mixPeak;
        SuggestedVolume = mixPeak > 1e-6f ? 1.0f / mixPeak : 1.0f;
    }

    /// <summary>The sample rate the measurement ran at.</summary>
    /// <remarks>
    /// The rate the music will be rendered or played at, because loudness measured below a bright
    /// instrument's energy is not that instrument's loudness.
    /// </remarks>
    public int SampleRate { get; }

    /// <summary>
    /// How many tracks had their <see cref="PlayerTrack.MidiSourceGain"/> written - the tracks that
    /// hold BOTH a recording and a MIDI performance, and where neither was silent.
    /// </summary>
    public int MatchedTrackCount { get; }

    /// <summary>
    /// The largest absolute sample the matched mix reaches, with every track on the source it was
    /// on when the measurement ran and at unity master volume. 1.0 is full scale.
    /// </summary>
    /// <remarks>
    /// This is the peak of the SUMMED mix, measured by rendering it - per-track peaks do not add up
    /// to it, because two tracks peak at different moments and cancel as often as they reinforce.
    /// <see cref="MultiTrackPlayer.Volume"/> is NOT applied, so <see cref="SuggestedVolume"/> is a
    /// volume to set rather than a factor to combine with the one already there. Switching a
    /// track's <see cref="PlayerTrack.ActiveSource"/> afterwards changes what the mix will peak at,
    /// and this figure then describes the arrangement as it was measured.
    /// </remarks>
    public float MixPeak { get; }

    /// <summary>
    /// The <see cref="MultiTrackPlayer.Volume"/> that would put the matched mix exactly at full
    /// scale: below 1 when it would otherwise clip, above 1 when there is headroom going spare, and
    /// 1 when the mix is silent.
    /// </summary>
    public float SuggestedVolume { get; }

    /// <summary>Whether the matched mix goes past full scale at unity master volume.</summary>
    public bool WouldClip => MixPeak > 1.0f;

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>The tracks matched, the peak and the suggested volume.</returns>
    public override string ToString() =>
        $"{MatchedTrackCount} track(s) matched at {SampleRate} Hz; mix peak {MixPeak:0.###}, " +
        $"suggested volume {SuggestedVolume:0.###}";
}

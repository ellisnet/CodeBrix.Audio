namespace CodeBrix.Audio.Playback;

/// <summary>
/// Which of a <see cref="PlayerTrack"/>'s two sources is heard.
/// </summary>
/// <remarks>
/// A track can hold a recording and a MIDI performance of the same part - the shape a stems export
/// takes, where each stem arrives as a WAV and (sometimes) a MIDI file. Both are rendered in step
/// whenever both are present, so switching between them is a short crossfade rather than a restart.
/// </remarks>
public enum TrackSource
{
    /// <summary>The decoded audio file.</summary>
    Audio = 0,

    /// <summary>The MIDI performance, played through the track's own synthesizer.</summary>
    Midi = 1,
}

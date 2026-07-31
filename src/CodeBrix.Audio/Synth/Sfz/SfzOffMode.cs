namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// How a voice is silenced when another region in its <c>off_by</c> group starts, from the
/// <c>off_mode</c> opcode.
/// </summary>
public enum SfzOffMode
{
    /// <summary><c>fast</c> (the default) - a very quick fade, the closed-hi-hat-chokes-open behaviour.</summary>
    Fast = 0,

    /// <summary><c>normal</c> - the voice runs its ordinary envelope release instead of being choked.</summary>
    Normal,

    /// <summary>
    /// <c>time</c> (ARIA) - the voice fades out over the region's <c>off_time</c> seconds, shaped by
    /// <c>off_shape</c>.
    /// </summary>
    Time
}

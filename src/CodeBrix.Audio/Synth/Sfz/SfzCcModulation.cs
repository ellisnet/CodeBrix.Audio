namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One CC modulation attached to a region parameter: the controller number, the modulation depth, and
/// the index of the curve shaping the controller value.
/// </summary>
/// <remarks>
/// This is the parsed form of the <c>_onccN</c> / <c>_ccN</c> opcode family, with any matching
/// <c>_curveccN</c> folded in. How the depth applies is the target parameter's business: additive
/// decibels for <c>volume</c>, a multiplicative gain for <c>amplitude</c>, additive cents for
/// <c>tune</c> and <c>cutoff</c>, additive position for <c>pan</c>, additive seconds for the envelope
/// stages. The modulated contribution is always <c>depth x curve(ccValue / 127)</c>.
/// </remarks>
public readonly struct SfzCcModulation
{
    /// <summary>Creates a modulation entry.</summary>
    /// <param name="ccNumber">The MIDI controller number; 128 and above are the extended sources.</param>
    /// <param name="depth">The modulation depth, in the target parameter's units.</param>
    /// <param name="curveIndex">The curve index shaping the controller value; 0 is linear.</param>
    public SfzCcModulation(int ccNumber, float depth, int curveIndex)
        : this(ccNumber, depth, curveIndex, 0f)
    {
    }

    /// <summary>Creates a modulation entry with a smoothing time.</summary>
    /// <param name="ccNumber">The MIDI controller number; 128 and above are the extended sources.</param>
    /// <param name="depth">The modulation depth, in the target parameter's units.</param>
    /// <param name="curveIndex">The curve index shaping the controller value; 0 is linear.</param>
    /// <param name="smoothMilliseconds">The smoothing time from <c>_smoothccN</c>, in milliseconds; 0 is unsmoothed.</param>
    public SfzCcModulation(int ccNumber, float depth, int curveIndex, float smoothMilliseconds)
    {
        CcNumber = ccNumber;
        Depth = depth;
        CurveIndex = curveIndex;
        SmoothMilliseconds = smoothMilliseconds;
    }

    /// <summary>
    /// The MIDI controller number. Values 128 and above are the ARIA extended modulation sources
    /// (128 pitch bend, 129 channel aftertouch, 131 note-on velocity, 133 note number, 134 key gate,
    /// 135/136 per-voice random, 137 alternate, 140/141 key delta).
    /// </summary>
    public int CcNumber { get; }

    /// <summary>The smoothing time from <c>_smoothccN</c>, in milliseconds; 0 means unsmoothed.</summary>
    public float SmoothMilliseconds { get; }

    /// <summary>The modulation depth, in the target parameter's units.</summary>
    public float Depth { get; }

    /// <summary>The curve index shaping the controller value; 0 is linear.</summary>
    public int CurveIndex { get; }
}

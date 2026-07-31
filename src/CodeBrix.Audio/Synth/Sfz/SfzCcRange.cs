namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// A controller-range condition on a region: the region is eligible only while the controller's value
/// lies inside the range. The parsed form of <c>loccN</c>/<c>hiccN</c> (and, for CC-triggered regions,
/// <c>on_loccN</c>/<c>on_hiccN</c>).
/// </summary>
public readonly struct SfzCcRange
{
    /// <summary>Creates a controller range.</summary>
    /// <param name="ccNumber">The MIDI controller number, 0-127.</param>
    /// <param name="low">The lowest controller value inside the range.</param>
    /// <param name="high">The highest controller value inside the range.</param>
    public SfzCcRange(int ccNumber, int low, int high)
    {
        CcNumber = ccNumber;
        Low = low;
        High = high;
    }

    /// <summary>The MIDI controller number, 0-127.</summary>
    public int CcNumber { get; }

    /// <summary>The lowest controller value inside the range. Defaults to 0 when only <c>hiccN</c> was written.</summary>
    public int Low { get; }

    /// <summary>The highest controller value inside the range. Defaults to 127 when only <c>loccN</c> was written.</summary>
    public int High { get; }

    /// <summary>Whether a controller value lies inside this range.</summary>
    /// <param name="value">The controller value to test.</param>
    /// <returns><see langword="true"/> when inside.</returns>
    public bool Contains(int value) => Low <= value && value <= High;
}

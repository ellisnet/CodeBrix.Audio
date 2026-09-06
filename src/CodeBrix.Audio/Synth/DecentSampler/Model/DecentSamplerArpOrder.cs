namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The order the arpeggiator plays held notes in.
/// </summary>
public enum DecentSamplerArpOrder
{
    /// <summary>Lowest to highest (<c>up</c>). The default.</summary>
    Up,

    /// <summary>Highest to lowest (<c>down</c>).</summary>
    Down,

    /// <summary>Up then back down, playing the turnaround notes once (<c>up_down</c>).</summary>
    UpDown,

    /// <summary>Up then back down, repeating the turnaround notes (<c>up_down_inclusive</c>).</summary>
    UpDownInclusive,

    /// <summary>Down then back up, playing the turnaround notes once (<c>down_up</c>).</summary>
    DownUp,

    /// <summary>Down then back up, repeating the turnaround notes (<c>down_up_inclusive</c>).</summary>
    DownUpInclusive,

    /// <summary>In the order the notes were pressed (<c>as_played</c>).</summary>
    AsPlayed,

    /// <summary>A random note from the pool on every step (<c>random</c>).</summary>
    Random,

    /// <summary>Random, never the same note twice in a row (<c>random_no_repeat</c>).</summary>
    RandomNoRepeat,
}

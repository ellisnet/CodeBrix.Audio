namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a control snaps its value as the user drags it.
/// </summary>
public enum DecentSamplerSnapMode
{
    /// <summary>No snapping (<c>none</c>). The default.</summary>
    None,

    /// <summary>Snap to whole numbers (<c>whole_numbers</c>).</summary>
    WholeNumbers,

    /// <summary>Snap to tenths (<c>tenths</c>).</summary>
    Tenths,

    /// <summary>Snap to hundredths (<c>hundredths</c>).</summary>
    Hundredths,

    /// <summary>Snap to thousandths (<c>thousandths</c>).</summary>
    Thousandths,

    /// <summary>Snap to the values in <c>snapStopPoints</c> (<c>stop_points</c>).</summary>
    StopPoints,
}

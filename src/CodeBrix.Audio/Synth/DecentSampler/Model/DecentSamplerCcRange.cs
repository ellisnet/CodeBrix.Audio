using System.Globalization;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An inclusive range of values for one MIDI continuous controller, written as a
/// <c>loCCN</c>/<c>hiCCN</c> or <c>onLoCCN</c>/<c>onHiCCN</c> pair.
/// </summary>
/// <remarks>
/// A range with only one half written is still kept: the missing half takes the widest value, and
/// the preset's problem list says which half was missing, because the guide requires both.
/// </remarks>
/// <param name="controller">The controller number, 0 to 127.</param>
/// <param name="low">The lowest value in the range.</param>
/// <param name="high">The highest value in the range, inclusive.</param>
public readonly struct DecentSamplerCcRange(int controller, int low, int high)
{
    /// <summary>The controller number, 0 to 127.</summary>
    public int Controller { get; } = controller;

    /// <summary>The lowest value in the range.</summary>
    public int Low { get; } = low;

    /// <summary>The highest value in the range, inclusive.</summary>
    public int High { get; } = high;

    /// <summary>Whether a controller value falls inside the range.</summary>
    /// <param name="value">The last value received for this controller.</param>
    /// <returns><see langword="true"/> when the value is within the range.</returns>
    public bool Contains(int value) => value >= Low && value <= High;

    /// <inheritdoc/>
    public override string ToString() =>
        "CC" + Controller.ToString(CultureInfo.InvariantCulture) + " " +
        Low.ToString(CultureInfo.InvariantCulture) + "-" + High.ToString(CultureInfo.InvariantCulture);
}

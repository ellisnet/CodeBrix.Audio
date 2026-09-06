using System.Globalization;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One point of a binding's piecewise-linear translation table.
/// </summary>
/// <remarks>
/// A table is written as <c>in,out;in,out</c>, so the point <c>0.3,150</c> says "when the source
/// reads 0.3, send 150 to the target". Values between two points are interpolated linearly; values
/// outside the first and last point are clamped to them.
/// </remarks>
/// <param name="input">The source value, normally 0 to 1.</param>
/// <param name="output">The value sent to the target at that source value.</param>
public readonly struct DecentSamplerTranslationPoint(double input, double output)
{
    /// <summary>The source value, normally 0 to 1.</summary>
    public double Input { get; } = input;

    /// <summary>The value sent to the target when the source reads <see cref="Input"/>.</summary>
    public double Output { get; } = output;

    /// <inheritdoc/>
    public override string ToString() =>
        Input.ToString("R", CultureInfo.InvariantCulture) + "," + Output.ToString("R", CultureInfo.InvariantCulture);
}

using System;

namespace CodeBrix.Audio.ModestSynth.Effects.Internal;

/// <summary>
/// One channel's worth of four-times oversampling, built as a pair of half-band stages in each
/// direction: the thing <c>highQuality="true"</c> buys on a wave shaper.
/// </summary>
/// <remarks>
/// <para>
/// A memoryless nonlinearity makes harmonics well above the input's own, and every one of them
/// above half the sample rate folds back into the audible band as an inharmonic alias. Running the
/// shaper at four times the rate moves the fold-back point four times higher, and the decimation
/// filters throw those harmonics away instead of letting them fold.
/// </para>
/// <para>
/// The cost is the guide's own warning: four evaluations of the shaper per sample plus twelve
/// half-band filter runs. Everything is allocated in the constructor.
/// </para>
/// </remarks>
internal sealed class Oversampler4x
{
    /// <summary>How many samples one input sample becomes.</summary>
    internal const int Factor = 4;

    private readonly HalfBandFilter upperUp = new HalfBandFilter();
    private readonly HalfBandFilter lowerUp = new HalfBandFilter();
    private readonly HalfBandFilter lowerDown = new HalfBandFilter();
    private readonly HalfBandFilter upperDown = new HalfBandFilter();

    /// <summary>
    /// The delay the four filters add, in samples of the ORIGINAL rate. A block that is mixed
    /// against an undelayed dry path is that far out of alignment.
    /// </summary>
    internal const int LatencySamples = HalfBandFilter.GroupDelaySamples;

    /// <summary>Clears every filter.</summary>
    internal void Reset()
    {
        upperUp.Reset();
        lowerUp.Reset();
        lowerDown.Reset();
        upperDown.Reset();
    }

    /// <summary>
    /// Turns one sample into <see cref="Factor" /> samples at four times the rate.
    /// </summary>
    /// <param name="value">The input sample.</param>
    /// <param name="destination">Receives the four oversampled values; must hold at least four.</param>
    internal void Up(double value, Span<double> destination)
    {
        // Zero stuffing halves the level at each doubling, so each stage carries a gain of two.
        double first = upperUp.Process(2.0 * value);
        double second = upperUp.Process(0.0);

        destination[0] = lowerUp.Process(2.0 * first);
        destination[1] = lowerUp.Process(0.0);
        destination[2] = lowerUp.Process(2.0 * second);
        destination[3] = lowerUp.Process(0.0);
    }

    /// <summary>
    /// Turns <see cref="Factor" /> oversampled samples back into one.
    /// </summary>
    /// <param name="source">The four values at four times the rate.</param>
    /// <returns>The single sample at the original rate.</returns>
    internal double Down(ReadOnlySpan<double> source)
    {
        // Every sample is filtered; alternate outputs are dropped. Dropping without filtering is
        // exactly the aliasing this class exists to avoid.
        double first = lowerDown.Process(source[0]);
        lowerDown.Process(source[1]);
        double second = lowerDown.Process(source[2]);
        lowerDown.Process(source[3]);

        double result = upperDown.Process(first);
        upperDown.Process(second);

        return result;
    }
}

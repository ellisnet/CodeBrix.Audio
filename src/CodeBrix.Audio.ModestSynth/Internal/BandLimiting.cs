using System;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// The two polynomial corrections that turn a naively-generated waveform into a band-limited one.
/// </summary>
/// <remarks>
/// <para>
/// A waveform generated straight from its phase has a hard corner at every discontinuity, and a
/// hard corner needs infinite bandwidth: sampled, its harmonics fold back below Nyquist as
/// inharmonic aliases. The fix is to add back the difference between the ideal band-limited edge
/// and the hard one, over the two samples that straddle it.
/// </para>
/// <para>
/// PolyBLEP (polynomial band-limited step) corrects a jump in the VALUE, which is what a saw and a
/// square have. PolyBLAMP (band-limited ramp) corrects a jump in the SLOPE, which is what a
/// triangle has; it is the integral of the same polynomial. Both are the standard two-sample
/// residuals, written from the published derivation rather than taken from any implementation.
/// </para>
/// </remarks>
internal static class BandLimiting
{
    /// <summary>
    /// The polyBLEP residual for a step discontinuity at phase zero.
    /// </summary>
    /// <param name="phase">The current phase in cycles, in [0, 1).</param>
    /// <param name="increment">The phase advance per sample, in cycles.</param>
    /// <returns>
    /// The residual, normalised for a step of height 2 (it runs from +1 just before the edge to
    /// -1 just after it) and zero outside the one sample either side of the edge. Add
    /// <c>height / 2</c> times this value to a naive waveform, where <c>height</c> is the signed
    /// size of the jump.
    /// </returns>
    internal static double Blep(double phase, double increment)
    {
        if (increment <= 0.0) { return 0.0; }

        if (phase < increment)
        {
            double x = phase / increment;
            return x + x - (x * x) - 1.0;
        }

        if (phase > 1.0 - increment)
        {
            double x = (phase - 1.0) / increment;
            return (x * x) + x + x + 1.0;
        }

        return 0.0;
    }

    /// <summary>
    /// The polyBLAMP residual for a slope discontinuity - the integral of <see cref="Blep" />.
    /// </summary>
    /// <param name="phaseDistance">
    /// Signed distance from the corner in cycles, already wrapped into (-0.5, 0.5] by
    /// <see cref="WrapSignedPhase" />: positive means the corner is behind us.
    /// </param>
    /// <param name="increment">The phase advance per sample, in cycles.</param>
    /// <returns>
    /// The residual, peaking at one third at the corner itself and zero outside the one sample
    /// either side of it. Add <c>slopeChange / 2</c> times this value to a naive waveform, where
    /// <c>slopeChange</c> is the signed change in slope expressed per SAMPLE.
    /// </returns>
    internal static double Blamp(double phaseDistance, double increment)
    {
        if (increment <= 0.0) { return 0.0; }

        double x = phaseDistance / increment;

        if (x >= 0.0 && x < 1.0)
        {
            double u = x - 1.0;
            return -(u * u * u) / 3.0;
        }

        if (x > -1.0 && x < 0.0)
        {
            double u = x + 1.0;
            return (u * u * u) / 3.0;
        }

        return 0.0;
    }

    /// <summary>
    /// Reduces a phase difference to the equivalent signed distance in (-0.5, 0.5].
    /// </summary>
    /// <param name="phaseDistance">A difference of two phases, in cycles.</param>
    /// <returns>The same distance measured the short way round the cycle.</returns>
    internal static double WrapSignedPhase(double phaseDistance)
        => phaseDistance - Math.Floor(phaseDistance + 0.5);
}

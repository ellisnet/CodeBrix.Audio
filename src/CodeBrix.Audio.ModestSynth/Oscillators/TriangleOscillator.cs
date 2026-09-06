using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// A band-limited triangle wave - the <c>triangle</c> waveform. Odd harmonics only, falling away
/// far faster than a square's, which is what makes it the mellow shape.
/// </summary>
/// <remarks>
/// A triangle has no jump in value, only in SLOPE, so the correction is a polyBLAMP at each of the
/// two corners rather than the polyBLEP a saw or square needs. Doing it that way keeps the shape
/// and the level correct at every pitch, which the more common "integrate a square" trick does not:
/// its leaky integrator droops at low frequencies and rounds towards a square at high ones.
/// Output is [-1, 1] apart from the rounding at each corner.
/// </remarks>
public sealed class TriangleOscillator : ModestOscillatorBase
{
    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Triangle;

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        double phase = Phase;
        double increment = PhaseIncrement;

        // Slope is +4 per cycle on the way up and -4 on the way down, so each corner changes the
        // slope by 8 per cycle - which is 8 * increment per SAMPLE, and the correction takes half.
        double slopeHalf = 4.0 * increment;

        for (int i = 0; i < buffer.Length; i++)
        {
            // -1 at phase 0, +1 at phase 0.5, back to -1 at phase 1.
            double value = phase < 0.5 ? ((4.0 * phase) - 1.0) : (3.0 - (4.0 * phase));

            // Bottom corner at phase 0 turns the slope upwards, top corner at 0.5 turns it down.
            value += slopeHalf * BandLimiting.Blamp(BandLimiting.WrapSignedPhase(phase), increment);
            value -= slopeHalf * BandLimiting.Blamp(BandLimiting.WrapSignedPhase(phase - 0.5), increment);

            buffer[i] = (float)value;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
    }
}

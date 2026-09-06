using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// A band-limited sawtooth - the <c>saw</c> waveform. Rich in both odd and even harmonics, which
/// is what makes it the bright, cutting shape a subtractive patch usually starts from.
/// </summary>
/// <remarks>
/// The falling edge once per cycle is rounded off with a polyBLEP correction, so the aliases a
/// naive saw folds back below Nyquist are pushed far down instead. Output is [-1, 1] apart from a
/// small overshoot at the corrected edge.
/// </remarks>
public sealed class SawOscillator : ModestOscillatorBase
{
    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Saw;

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        double phase = Phase;
        double increment = PhaseIncrement;

        for (int i = 0; i < buffer.Length; i++)
        {
            // Rises from -1 to +1 over the cycle, then jumps back by -2 at phase zero.
            double value = (2.0 * phase) - 1.0;
            value -= BandLimiting.Blep(phase, increment);

            buffer[i] = (float)value;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
    }
}

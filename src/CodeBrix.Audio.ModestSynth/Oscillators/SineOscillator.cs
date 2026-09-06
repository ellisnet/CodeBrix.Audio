using System;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// A pure sine wave - the <c>sine</c> waveform, and the default an oscillator with no waveform
/// attribute gets.
/// </summary>
/// <remarks>
/// A sine has exactly one partial, so there is nothing to band-limit: it cannot alias at any pitch
/// below Nyquist. Output is [-1, 1].
/// </remarks>
public sealed class SineOscillator : ModestOscillatorBase
{
    private const double TwoPi = 2.0 * Math.PI;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Sine;

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        double phase = Phase;
        double increment = PhaseIncrement;

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)Math.Sin(TwoPi * phase);

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
    }
}

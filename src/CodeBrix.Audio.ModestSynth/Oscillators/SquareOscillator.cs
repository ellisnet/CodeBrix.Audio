using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// A band-limited square wave - the <c>square</c> waveform. At the default width it is the hollow,
/// reed-like shape with odd harmonics only.
/// </summary>
/// <remarks>
/// <para>
/// Both edges are rounded off with a polyBLEP correction, so the aliases a naive square folds back
/// below Nyquist are pushed far down instead. Output is [-1, 1] apart from a small overshoot at
/// each corrected edge.
/// </para>
/// <para>
/// <see cref="PulseWidth" /> is a standalone extra: the Decent Sampler <c>&lt;oscillator&gt;</c>
/// element has no pulse-width attribute, so an oscillator built from a preset always runs at the
/// documented 50%.
/// </para>
/// </remarks>
public sealed class SquareOscillator : ModestOscillatorBase
{
    /// <summary>The pulse width a new oscillator starts at: 50%, a true square.</summary>
    public const double DefaultPulseWidth = 0.5;

    private const double MinimumPulseWidth = 0.01;
    private const double MaximumPulseWidth = 0.99;

    private double pulseWidth = DefaultPulseWidth;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Square;

    /// <summary>
    /// The fraction of each cycle spent at +1, from 0.01 to 0.99. Defaults to
    /// <see cref="DefaultPulseWidth" />; values outside the range are clamped into it, because an
    /// edge cannot be band-limited once the two edges collide.
    /// </summary>
    public double PulseWidth
    {
        get => pulseWidth;
        set
        {
            if (double.IsNaN(value)) { return; }
            pulseWidth = value < MinimumPulseWidth ? MinimumPulseWidth
                : value > MaximumPulseWidth ? MaximumPulseWidth
                : value;
        }
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        double phase = Phase;
        double increment = PhaseIncrement;
        double width = pulseWidth;

        for (int i = 0; i < buffer.Length; i++)
        {
            double value = phase < width ? 1.0 : -1.0;

            // Rising edge at phase zero (a jump of +2) ...
            value += BandLimiting.Blep(phase, increment);

            // ... and the falling edge at phase = width (a jump of -2), reached by shifting the
            // phase so that the edge lands at zero.
            double shifted = phase + 1.0 - width;
            if (shifted >= 1.0) { shifted -= 1.0; }
            value -= BandLimiting.Blep(shifted, increment);

            buffer[i] = (float)value;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
    }
}

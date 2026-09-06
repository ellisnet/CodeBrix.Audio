using System;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal;

// MIDI note to frequency, and the one waveform whose tuning range is narrower than the keyboard.
internal static class ModestPitch
{
    // Concert A is MIDI 69 at 440 Hz - the MIDI tuning standard, and the tuning the reference Decent
    // Sampler player was measured to use for its oscillators.
    internal static double ToHertz(double note) => 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);

    // A waveguide string is a delay line, so it can only be tuned inside the range its line covers.
    // Everything else takes any non-negative frequency.
    internal static double Clamp(IModestOscillator oscillator, int sampleRate, double hertz)
    {
        double frequency = hertz;

        if (double.IsNaN(frequency) || double.IsInfinity(frequency) || frequency < 0.0) { frequency = 0.0; }

        if (oscillator is Pluck1Oscillator)
        {
            double highest = sampleRate * Pluck1Oscillator.MaximumFrequencyFraction;
            if (frequency < Pluck1Oscillator.MinimumFrequency) { frequency = Pluck1Oscillator.MinimumFrequency; }
            else if (frequency > highest) { frequency = highest; }
        }

        return frequency;
    }
}

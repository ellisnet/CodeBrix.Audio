using System;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The <c>formant</c> waveform written the naive way - one <see cref="Math.Sin" /> per partial per
/// sample, straight from the master phase - so a test can show that the shipped oscillator's
/// rotating-vector render is the same waveform to the last bit.
/// </summary>
/// <remarks>
/// <para>
/// This is the ORIGINAL <c>FormantOscillator.Render</c>, kept here on purpose and complete: the
/// measured envelope table, the log-frequency interpolation, the -18 dB per octave extrapolation,
/// the Nyquist cut, the root-mean-square normalisation onto the reference's measured level, the
/// Schroeder phases and the per-sample sum
/// <c>sum over k of gains[k] * sin(2*pi*phase*(k+1) + phases[k])</c>. Nothing here is shared with
/// the shipped class, so a change to any part of the waveform - not just to the summation - shows
/// up as a sample that no longer matches.
/// </para>
/// <para>
/// It is the same idea as <see cref="NaiveWaveforms" />, which renders the classic shapes without
/// band limiting so a test can measure what the corrections are worth. The difference is what it
/// is used for: those show a DIFFERENCE, this one has to show NONE.
/// </para>
/// <para>
/// A MAINTAINER RE-SYNCING THE OSCILLATOR WITH ITS MEASURED REFERENCE CHANGES THIS FILE TOO. The
/// spectrum, the level and the phases live in both places by design - that is what makes the fence
/// a fence - so a new measurement is written here as well, and the fence then proves the fast
/// render still reproduces it.
/// </para>
/// </remarks>
public sealed class NaiveFormant
{
    /// <summary>The most partials that can ever sound, at the lowest pitch a note can carry.</summary>
    public const int MaximumPartials = 512;

    /// <summary>How many partials the fixed spectrum was measured over.</summary>
    public const int MeasuredPartialCount = 20;

    /// <summary>The pitch the reference's spectrum was measured at, in Hz.</summary>
    public const double MeasuredFundamentalHz = 261.626;

    /// <summary>How fast the spectrum falls above the measured range, in decibels per octave.</summary>
    public const double RolloffDecibelsPerOctave = -18.0;

    /// <summary>The measured level of this waveform against a full-amplitude sine, as a ratio.</summary>
    public const double ReferenceGain = 1.9588;

    private const double TwoPi = 2.0 * Math.PI;

    // The measured envelope, in decibels relative to the fundamental, at 1..20 times 261.626 Hz.
    private static readonly double[] EnvelopeDecibels =
    {
        0.0, -15.4, -22.8, -26.3, -28.1, -28.6, -27.3, -21.2, -10.6, -22.6,
        -21.9, -28.9, -41.1, -48.7, -54.8, -60.3, -65.4, -68.9, -72.0, -75.3,
    };

    private readonly double[] gains = new double[MaximumPartials];
    private readonly double[] phases = new double[MaximumPartials];

    private int sampleRate = 48000;
    private double frequency = 440.0;
    private double increment = 440.0 / 48000.0;
    private double phase;
    private int activeCount;
    private double builtForFrequency = -1.0;
    private double builtForSampleRate = -1.0;

    /// <summary>The current sample rate in Hz. It starts where a real oscillator's does.</summary>
    public int SampleRate => sampleRate;

    /// <summary>The current pitch in Hz. It starts where a real oscillator's does.</summary>
    public double Frequency => frequency;

    /// <summary>The current phase in cycles, always in [0, 1).</summary>
    public double Phase => phase;

    /// <summary>How many partials are sounding at the current pitch and sample rate.</summary>
    public int ActivePartialCount
    {
        get
        {
            Rebuild();
            return activeCount;
        }
    }

    /// <summary>
    /// The gain the fixed spectral envelope gives a partial at a frequency, relative to the
    /// envelope's own peak at the fundamental of the pitch it was measured at.
    /// </summary>
    /// <param name="frequencyHz">The partial's frequency in Hz.</param>
    /// <returns>A linear gain.</returns>
    public static double EnvelopeAt(double frequencyHz)
    {
        if (double.IsNaN(frequencyHz) || frequencyHz <= 0.0) { return 0.0; }

        double index = frequencyHz / MeasuredFundamentalHz;

        if (index <= 1.0) { return 1.0; }

        if (index >= MeasuredPartialCount)
        {
            double octaves = Math.Log2(index / MeasuredPartialCount);
            double decibels = EnvelopeDecibels[MeasuredPartialCount - 1] +
                (RolloffDecibelsPerOctave * octaves);
            return Math.Pow(10.0, decibels / 20.0);
        }

        int low = (int)index;
        double fraction = index - low;
        double blended = EnvelopeDecibels[low - 1] +
            (fraction * (EnvelopeDecibels[low] - EnvelopeDecibels[low - 1]));

        return Math.Pow(10.0, blended / 20.0);
    }

    /// <summary>
    /// Sets the sample rate, the way <c>ModestOscillatorBase.SetSampleRate</c> does.
    /// </summary>
    /// <param name="rate">The sample rate in Hz.</param>
    public void SetSampleRate(int rate)
    {
        sampleRate = rate;
        increment = frequency / rate;
    }

    /// <summary>
    /// Sets the pitch, the way <c>ModestOscillatorBase.SetFrequency</c> does.
    /// </summary>
    /// <param name="frequencyHz">The pitch in Hz.</param>
    public void SetFrequency(double frequencyHz)
    {
        frequency = frequencyHz;
        increment = frequencyHz / sampleRate;
    }

    /// <summary>
    /// Sets the phase, the way <c>ModestOscillatorBase.Reset</c> does.
    /// </summary>
    /// <param name="startPhase">The phase in cycles; it is wrapped into [0, 1).</param>
    public void Reset(double startPhase)
    {
        if (double.IsNaN(startPhase) || double.IsInfinity(startPhase)) { phase = 0.0; return; }

        double wrapped = startPhase - Math.Floor(startPhase);
        if (wrapped < 0.0 || wrapped >= 1.0) { wrapped = 0.0; }
        phase = wrapped;
    }

    /// <summary>
    /// Renders the waveform, one <see cref="Math.Sin" /> per partial per sample.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    public void Render(Span<float> buffer)
    {
        Rebuild();

        int count = activeCount;

        for (int i = 0; i < buffer.Length; i++)
        {
            double sum = 0.0;

            for (int k = 0; k < count; k++)
            {
                sum += gains[k] * Math.Sin((TwoPi * phase * (k + 1)) + phases[k]);
            }

            buffer[i] = (float)sum;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }
    }

    // Works out the partial gains for the current pitch. Only pitch and rate change them.
    private void Rebuild()
    {
        if (frequency == builtForFrequency && sampleRate == builtForSampleRate) { return; }

        builtForFrequency = frequency;
        builtForSampleRate = sampleRate;
        activeCount = 0;

        double nyquist = sampleRate * 0.5;
        double power = 0.0;

        for (int k = 1; k <= MaximumPartials; k++)
        {
            double partial = k * frequency;
            if (partial <= 0.0 || partial >= nyquist) { break; }

            double gain = EnvelopeAt(partial);
            if (gain <= 1.0e-6) { continue; }

            gains[activeCount++] = gain;
            power += gain * gain;
        }

        if (power <= 0.0) { return; }

        // The measured level: the root-mean-square of the summed partials, which is
        // sqrt(sum of squares / 2), put where the reference's own tone sits relative to a sine.
        double scale = ReferenceGain / Math.Sqrt(power);

        for (int k = 0; k < activeCount; k++)
        {
            gains[k] *= scale;

            // Schroeder's phase set: the lowest crest factor a given magnitude spectrum can have.
            phases[k] = -Math.PI * k * (k + 1) / activeCount;
        }
    }
}

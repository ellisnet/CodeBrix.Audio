using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// One stereo two-pole filter section in the RBJ Audio-EQ-Cookbook forms, with its own state per
// channel and a state-preserving retune so a knob can sweep the cut-off without clicking.
//
// MEASURED (plan section 7 item 5): every filter effect in the reference player is one of these,
// matched to within 0.15 dB from 125 Hz to 16 kHz, with `resonance` used directly as Q. This is the
// cookbook's own arithmetic; nothing here is scaled or fudged.
//
// The Dsp.BiQuadFilter in this package computes the same coefficients but throws on a non-positive
// cut-off and offers no constant-skirt band-pass, and the format allows frequency="0". A filter that
// throws on the audio thread is worse than one that clamps, so the coefficients live here.
internal sealed class DecentSamplerBiquad
{
    // The cookbook is undefined at 0 Hz and at Nyquist; the reference player clamps rather than
    // failing, and so does this.
    private const double MinimumFrequency = 1.0;
    private const double MinimumQ = 0.01;

    private double _b0 = 1.0;
    private double _b1;
    private double _b2;
    private double _a1;
    private double _a2;

    private double _x1Left;
    private double _x2Left;
    private double _y1Left;
    private double _y2Left;

    private double _x1Right;
    private double _x2Right;
    private double _y1Right;
    private double _y2Right;

    // Clears the delay line without touching the coefficients.
    public void Reset()
    {
        _x1Left = _x2Left = _y1Left = _y2Left = 0.0;
        _x1Right = _x2Right = _y1Right = _y2Right = 0.0;
    }

    // Makes the section a pass-through.
    public void SetBypass()
    {
        _b0 = 1.0;
        _b1 = _b2 = _a1 = _a2 = 0.0;
    }

    public void SetLowpass(double sampleRate, double frequency, double q)
    {
        Prepare(sampleRate, frequency, q, out var cos, out var alpha);

        var b0 = (1.0 - cos) / 2.0;
        Normalise(b0, 1.0 - cos, b0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
    }

    public void SetHighpass(double sampleRate, double frequency, double q)
    {
        Prepare(sampleRate, frequency, q, out var cos, out var alpha);

        var b0 = (1.0 + cos) / 2.0;
        Normalise(b0, -(1.0 + cos), b0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
    }

    // The CONSTANT-SKIRT-GAIN band-pass, whose peak gain is Q. Measured: a bandpass at resonance 0.7
    // reads -3.09 dB at its centre and one at resonance 3 reads +9.59 dB, which is this form and not
    // the constant-0 dB one.
    public void SetBandpass(double sampleRate, double frequency, double q)
    {
        Prepare(sampleRate, frequency, q, out var cos, out var alpha);

        var b0 = q * alpha;
        Normalise(b0, 0.0, -b0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
    }

    public void SetNotch(double sampleRate, double frequency, double q)
    {
        Prepare(sampleRate, frequency, q, out var cos, out var alpha);

        Normalise(1.0, -2.0 * cos, 1.0, 1.0 + alpha, -2.0 * cos, 1.0 - alpha);
    }

    // The peaking EQ, where `gain` is the LINEAR peak gain: gain="2" measured +6.02 dB and gain="0.5"
    // measured -6.02 dB, so the cookbook's A is the square root of the attribute.
    public void SetPeak(double sampleRate, double frequency, double q, double gain)
    {
        Prepare(sampleRate, frequency, q, out var cos, out var alpha);

        var a = Math.Sqrt(Math.Max(1.0e-6, gain));

        Normalise(
            1.0 + alpha * a, -2.0 * cos, 1.0 - alpha * a,
            1.0 + alpha / a, -2.0 * cos, 1.0 - alpha / a);
    }

    public void Process(float[] left, float[] right, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            double x = left[i];
            var y = _b0 * x + _b1 * _x1Left + _b2 * _x2Left - _a1 * _y1Left - _a2 * _y2Left;
            _x2Left = _x1Left;
            _x1Left = x;
            _y2Left = _y1Left;
            _y1Left = y;
            left[i] = (float)y;

            x = right[i];
            y = _b0 * x + _b1 * _x1Right + _b2 * _x2Right - _a1 * _y1Right - _a2 * _y2Right;
            _x2Right = _x1Right;
            _x1Right = x;
            _y2Right = _y1Right;
            _y1Right = y;
            right[i] = (float)y;
        }
    }

    private static void Prepare(
        double sampleRate, double frequency, double q, out double cos, out double alpha)
    {
        var rate = Math.Max(1.0, sampleRate);
        var hertz = Math.Clamp(frequency, MinimumFrequency, rate * 0.49);
        var quality = Math.Max(MinimumQ, q);

        var w0 = 2.0 * Math.PI * hertz / rate;
        cos = Math.Cos(w0);
        alpha = Math.Sin(w0) / (2.0 * quality);
    }

    private void Normalise(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }
}

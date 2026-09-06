using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// lowpass_1pl: the one-pole smoother the guide describes as "a single pole version of the lowpass
// filter ... without a resonance parameter".
//
// MEASURED (plan section 7 item 5): y += a * (x - y) with a = 1 - exp(-2*pi*fc/sr). At fc it reads
// -3.04 dB and it flattens out to a floor of (1-p)/(1+p) rather than rolling off for ever, which is
// exactly what that difference equation does and is why its slope is not a clean 6 dB per octave near
// Nyquist.
internal sealed class DecentSamplerOnePoleLowpassEffect : DecentSamplerEffectBase
{
    private double _frequency = double.NaN;
    private float _coefficient = 1f;
    private float _left;
    private float _right;

    public DecentSamplerOnePoleLowpassEffect(EffectContext context)
        : base(context, "FX_FILTER_FREQUENCY")
    {
    }

    protected override void OnPrepare() => _frequency = double.NaN;

    protected override void OnReset()
    {
        _left = 0f;
        _right = 0f;
    }

    protected override void Refresh(bool force)
    {
        var frequency = Effect.Frequency ?? 22000.0;

        if (!force && frequency == _frequency)
        {
            return;
        }

        _frequency = frequency;

        var hertz = Math.Clamp(frequency, 0.0, SampleRate * 0.5);
        _coefficient = (float)(1.0 - Math.Exp(-2.0 * Math.PI * hertz / SampleRate));
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var a = _coefficient;
        var l = _left;
        var r = _right;

        for (var i = 0; i < frames; i++)
        {
            l += a * (left[i] - l);
            r += a * (right[i] - r);
            left[i] = l;
            right[i] = r;
        }

        _left = l;
        _right = r;
    }
}

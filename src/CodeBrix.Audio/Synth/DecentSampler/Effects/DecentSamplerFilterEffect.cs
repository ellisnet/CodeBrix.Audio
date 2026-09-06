using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The two-pole filter family: lowpass (and its legacy alias lowpass_4pl), bandpass, highpass, notch
// and peak. One class covers all five because they differ only in which cookbook form they load.
//
// MEASURED (plan section 7 item 5):
//   - "lowpass" and "lowpass_4pl" are the SAME two-pole filter. There is no four-pole filter.
//   - `resonance` IS the biquad Q, used directly and unscaled, with a floor near 0.01 and a default
//     of 0.7.
//   - bandpass is the CONSTANT-SKIRT-GAIN form, whose peak gain equals Q.
//   - notch takes `q`; peak takes `q` and a LINEAR `gain` that works above the documented 1.0.
internal sealed class DecentSamplerFilterEffect : DecentSamplerEffectBase
{
    private readonly DecentSamplerBiquad _biquad = new DecentSamplerBiquad();
    private readonly DecentSamplerEffectType _type;

    private double _frequency = double.NaN;
    private double _quality = double.NaN;
    private double _gain = double.NaN;

    public DecentSamplerFilterEffect(EffectContext context, DecentSamplerEffectType type)
        : base(context, "FX_FILTER_FREQUENCY", "FX_FILTER_RESONANCE", "FX_FILTER_Q", "FX_FILTER_GAIN")
    {
        _type = type;
    }

    protected override void OnPrepare()
    {
        _frequency = double.NaN;
        _quality = double.NaN;
        _gain = double.NaN;
    }

    protected override void OnReset() => _biquad.Reset();

    protected override void Refresh(bool force)
    {
        // The guide gives `resonance` to the lowpass/bandpass/highpass and `q` to the notch and peak.
        // A preset that writes the other spelling still means the same thing, so whichever one it
        // wrote is honoured, with the type's own default when it wrote neither.
        var quality = UsesResonance
            ? Effect.Resonance ?? Effect.Q ?? 0.7
            : Effect.Q ?? Effect.Resonance ?? 0.7;

        var frequency = Effect.Frequency ?? DefaultFrequency;
        var gain = Effect.Gain ?? 1.0;

        if (!force && frequency == _frequency && quality == _quality && gain == _gain)
        {
            return;
        }

        _frequency = frequency;
        _quality = quality;
        _gain = gain;

        switch (_type)
        {
            case DecentSamplerEffectType.Bandpass:
                _biquad.SetBandpass(SampleRate, frequency, quality);
                break;

            case DecentSamplerEffectType.Highpass:
                _biquad.SetHighpass(SampleRate, frequency, quality);
                break;

            case DecentSamplerEffectType.Notch:
                _biquad.SetNotch(SampleRate, frequency, quality);
                break;

            case DecentSamplerEffectType.Peak:
                _biquad.SetPeak(SampleRate, frequency, quality, gain);
                break;

            default:
                _biquad.SetLowpass(SampleRate, frequency, quality);
                break;
        }
    }

    protected override void ProcessCore(float[] left, float[] right, int frames) =>
        _biquad.Process(left, right, frames);

    private bool UsesResonance =>
        _type == DecentSamplerEffectType.Lowpass ||
        _type == DecentSamplerEffectType.Bandpass ||
        _type == DecentSamplerEffectType.Highpass;

    // The guide documents 22000 for the lowpass family and 10000 for the notch and peak.
    private double DefaultFrequency =>
        _type == DecentSamplerEffectType.Notch || _type == DecentSamplerEffectType.Peak ? 10000.0 : 22000.0;
}

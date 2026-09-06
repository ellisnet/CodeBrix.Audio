using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The compressor: a stereo-linked feed-forward dynamics processor with the guide's own parameters.
//
// FROM THE GUIDE: "Detection is stereo-linked (both channels are measured together, so the stereo
// image doesn't shift under compression)." threshold -60 to 0 dB (default -12), ratio 1 to 20
// (default 4), attack 0.1 to 200 ms (default 5), release 5 to 2000 ms (default 100), inputGain and
// outputGain -24 to 24 dB (default 0), autoBypass false by default: "the compressor fades itself out
// (click-free) whenever the input signal stays below threshold, and fades back in as soon as the
// signal re-engages".
//
// MEASURED (round 2, item 20) - 101 cases over two presets, and the law fits every one of them:
//
//     e  = one-pole peak follower on max(|L|,|R|), rise 1 - exp(-1/(fs*attack_s)),
//          fall 1 - exp(-1/(fs*release_s))
//     T  = 2*sqrt(2) * 10^(threshold_dB/20)
//     g  = e > T ? (T/e)*(e/T)^(1/ratio) : 1
//     out = (in * 10^(inputGain/20)) processed as above, then * 10^(outputGain/20)
//
// THE ONE SURPRISE IS THE THRESHOLD SCALE: it is offset by exactly 20*log10(2*sqrt(2)) = 9.031 dB, so
// threshold="-30" is a -20.97 dBFS PEAK threshold. That single constant was the whole of the 8 dB the
// first measurement round could not explain, and it means the DOCUMENTED DEFAULT threshold="-12"
// (a -2.97 dBFS peak) almost never engages.
//
// Three other readings the model depends on. The knee is HARD - a cell 0.8 dB below it showed exactly
// 0.000 dB of reduction. The detector is a PEAK follower, not an RMS one: three sources at the same
// RMS and crest factors of 0, 3.01 and 8.45 dB back-solve to the same threshold within 0.004 dB. And
// the GAIN IS NOT SMOOTHED - only the detector is; an implementation that smooths the gain instead
// misses the eleven attack/release settings, whose steady reduction depends on the ATTACK time
// because a slow follower cannot reach the peaks of a sine.
//
// Nothing is clamped except the ratio, which is held at or above 1 and is NOT capped above: ratio="100"
// works, threshold="-80" and "+12" work, and so do attack="0.001" and release="5000".
internal sealed class DecentSamplerCompressorEffect : DecentSamplerEffectBase
{
    // The threshold scale: a Decent Sampler threshold is 20*log10(2*sqrt(2)) = 9.031 dB below the peak
    // level the detector actually compares against. MEASURED twice, independently, to four decimals.
    private const double ThresholdScale = 2.8284271247461903;

    // The auto-bypass crossfade. MEASURED as a click-free fade that is 85 % in by 30 ms, 95 % by 40 ms
    // and complete by 60-80 ms; a one-pole with a 15 ms time constant fits that table within its own
    // noise, and the summary's "about 50 ms" is where it passes 96 %.
    private const double BypassTimeConstantSeconds = 0.015;

    private float _thresholdLinear = 0.7096f;
    private float _ratio = 4f;
    private float _inputGain = 1f;
    private float _outputGain = 1f;
    private float _attackCoefficient;
    private float _releaseCoefficient;
    private bool _autoBypass;

    private double _attackMilliseconds = double.NaN;
    private double _releaseMilliseconds = double.NaN;

    private float _envelope;
    private float _gainReductionDb;
    private float _bypassBlend = 1f;
    private float _bypassCoefficient;

    public DecentSamplerCompressorEffect(EffectContext context)
        : base(
            context,
            "FX_THRESHOLD", "FX_RATIO", "FX_ATTACK", "FX_RELEASE", "FX_INPUT_GAIN", "FX_OUTPUT_GAIN")
    {
    }

    // The gain reduction the compressor is applying right now, in decibels (a positive number means
    // the signal is being turned down). The tests read it.
    public double GainReductionDecibels => -_gainReductionDb;

    // How far the auto-bypass crossfade has travelled: 1 is fully engaged, 0 fully bypassed.
    public double BypassBlend => _bypassBlend;

    protected override void OnPrepare()
    {
        _attackMilliseconds = double.NaN;
        _releaseMilliseconds = double.NaN;
        _bypassCoefficient = Coefficient(BypassTimeConstantSeconds);
    }

    protected override void OnReset()
    {
        _envelope = 0f;
        _gainReductionDb = 0f;
        _bypassBlend = 1f;
    }

    protected override void Refresh(bool force)
    {
        // MEASURED: the threshold is not clamped at all (the documented -60..0 range is not enforced)
        // and the ratio is held at or above 1 and never capped above. The decibel clamps here are
        // numeric guards well outside anything a preset can mean, not the format's ranges.
        _thresholdLinear =
            (float)(ThresholdScale * Math.Pow(10.0, Math.Clamp(Effect.Threshold ?? -12.0, -300.0, 300.0) / 20.0));
        _ratio = (float)Math.Max(1.0, Effect.Ratio ?? 4.0);
        _inputGain = (float)Math.Pow(10.0, Math.Clamp(Effect.InputGain ?? 0.0, -300.0, 300.0) / 20.0);
        _outputGain = (float)Math.Pow(10.0, Math.Clamp(Effect.OutputGain ?? 0.0, -300.0, 300.0) / 20.0);
        _autoBypass = Effect.AutoBypass ?? false;

        var attack = Math.Max(0.0, Effect.Attack ?? 5.0);
        var release = Math.Max(0.0, Effect.Release ?? 100.0);

        if (!force && attack == _attackMilliseconds && release == _releaseMilliseconds)
        {
            return;
        }

        _attackMilliseconds = attack;
        _releaseMilliseconds = release;

        _attackCoefficient = Coefficient(attack / 1000.0);
        _releaseCoefficient = Coefficient(release / 1000.0);
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var threshold = _thresholdLinear;
        var exponent = 1f / _ratio;
        var makeup = _outputGain;
        var input = _inputGain;

        for (var i = 0; i < frames; i++)
        {
            var dryLeft = left[i];
            var dryRight = right[i];

            var wetLeft = dryLeft * input;
            var wetRight = dryRight * input;

            // Stereo-linked detection: one PEAK follower reads the louder of the two channels, so both
            // are turned down by the same amount and the image does not shift. MEASURED: a stereo file
            // with only its left channel loud produced the same reduction as one loud in both, which
            // rules out an averaged or summed detector.
            var peak = MathF.Max(MathF.Abs(wetLeft), MathF.Abs(wetRight));

            _envelope = peak > _envelope
                ? _envelope + ((peak - _envelope) * _attackCoefficient)
                : _envelope + ((peak - _envelope) * _releaseCoefficient);

            float gain;

            if (_envelope > threshold)
            {
                // g = (T/e) * (e/T)^(1/ratio), the plain hard-knee law with no smoothing of its own.
                var over = _envelope / threshold;
                gain = MathF.Pow(over, exponent) / over;
                _gainReductionDb = 20f * MathF.Log10(gain);
            }
            else
            {
                gain = 1f;
                _gainReductionDb = 0f;
            }

            var compressedLeft = wetLeft * gain * makeup;
            var compressedRight = wetRight * gain * makeup;

            if (_autoBypass)
            {
                AdvanceBypass(_envelope > threshold);

                var blend = _bypassBlend;
                left[i] = dryLeft + ((compressedLeft - dryLeft) * blend);
                right[i] = dryRight + ((compressedRight - dryRight) * blend);
            }
            else
            {
                left[i] = compressedLeft;
                right[i] = compressedRight;
            }
        }
    }

    // autoBypass. MEASURED: it takes the WHOLE effect out of the signal path, output gain included -
    // threshold="0" with outputGain="6" gave the reference level plus 6 dB with autoBypass off and the
    // reference level exactly with it on - and it does so whenever the DETECTOR sits below the
    // threshold, with no waiting period first. The default is false.
    private void AdvanceBypass(bool engaged)
    {
        var target = engaged ? 1f : 0f;
        _bypassBlend += (target - _bypassBlend) * _bypassCoefficient;
    }

    // The one-pole coefficient that reaches 1 - 1/e of a step in the given number of SECONDS. Written
    // as 1 - exp(-1/(fs*t)) because that is the form the measurement fitted; a time of zero gives a
    // coefficient of one, which is an instantaneous follower rather than a frozen one.
    private float Coefficient(double seconds) =>
        seconds <= 0.0 ? 1f : (float)(1.0 - Math.Exp(-1.0 / (seconds * SampleRate)));
}

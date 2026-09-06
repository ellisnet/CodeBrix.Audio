using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The chorus effect: a delay line whose read point is swept by a low-frequency oscillator, blended
// against the dry signal.
//
// MEASURED over three rounds, and the structure is now read directly off a click train (round 2,
// item 22) with the two channels' oscillators settled in round 3 (item 41):
//
//   - ONE modulated tap per channel; nothing else appears above -60 dB.
//   - The base delays are 14.15 ms (624 samples) LEFT and 10.00 ms (441) RIGHT - a fixed 4.15 ms
//     stereo offset, and 624 = 441*sqrt(2) to within half a sample.
//   - delay = baseDelay * (1 + modDepth * lfo) with lfo in [-1, +1]. The centre never moves and the
//     excursion is proportional to the base, so modDepth="1" sweeps each channel from ZERO to twice
//     its base delay - far wider than the classic 5-to-10 ms range this used to assume.
//   - THE TWO CHANNELS RUN AT DIFFERENT RATES: the right oscillator runs at (10/9) * modRate. Seven
//     independent fits gave 1.10992 to 1.11156 against 10/9 = 1.11111, so the stereo image drifts
//     with a beat period of 9/modRate seconds rather than sitting at a fixed offset.
//   - The LEFT oscillator is reset to phase 0 at every note-on (seven fits, 269.6 to 272.1 degrees on
//     a cosine, which is a sine from zero). The RIGHT one is NOT reproducible - four identical notes
//     gave four different phases - so its phase is implementation-defined; this engine starts it at
//     zero too, which is the only reproducible choice a seeded engine can make.
//   - modRate is the rate in HERTZ, used directly and unscaled.
//   - `mix` is the plain linear crossfade out = (1-mix)*dry + mix*wet, measured to three decimals.
internal sealed class DecentSamplerChorusEffect : DecentSamplerEffectBase
{
    // MEASURED: 624 and 441 samples at 44.1 kHz, expressed in seconds so that the effect keeps its
    // timbre at any rate.
    private const double BaseDelayLeftSeconds = 624.0 / 44100.0;
    private const double BaseDelayRightSeconds = 441.0 / 44100.0;

    // MEASURED: the right channel's oscillator runs ten ninths as fast as the left's.
    private const double RightRateRatio = 10.0 / 9.0;

    private float[] _lineLeft;
    private float[] _lineRight;
    private int _capacity;
    private int _writeIndex;
    private double _phase;
    private double _phaseRight;

    private float _mix;
    private double _modDepth;
    private double _phaseIncrement;
    private double _modRate;

    public DecentSamplerChorusEffect(EffectContext context)
        : base(context, "FX_MIX", "FX_MOD_DEPTH", "FX_MOD_RATE")
    {
    }

    // Where the left sweep is, 0 to 1 through one cycle. The rate tests read it.
    public double Phase => _phase;

    // Where the right sweep is. It runs ten ninths as fast, so the two drift apart.
    public double PhaseRight => _phaseRight;

    // The left channel's delay right now, in frames.
    public double DelayLeftFrames => DelayFrames(BaseDelayLeftSeconds, _phase);

    // The right channel's delay right now, in frames.
    public double DelayRightFrames => DelayFrames(BaseDelayRightSeconds, _phaseRight);

    protected override void OnPrepare()
    {
        // At modDepth 1 the left tap reaches twice its base delay, which is the deepest read there is.
        _capacity = Math.Max(4, (int)Math.Ceiling(2.0 * BaseDelayLeftSeconds * SampleRate) + 4);
        _lineLeft = new float[_capacity];
        _lineRight = new float[_capacity];
        _writeIndex = 0;
        _phase = 0.0;
        _phaseRight = 0.0;
        _modRate = double.NaN;
    }

    protected override void OnReset()
    {
        if (_lineLeft != null)
        {
            Array.Clear(_lineLeft, 0, _lineLeft.Length);
            Array.Clear(_lineRight, 0, _lineRight.Length);
        }

        _writeIndex = 0;
        _phase = 0.0;
        _phaseRight = 0.0;
    }

    protected override void Refresh(bool force)
    {
        _mix = (float)Math.Clamp(Effect.Mix ?? 0.5, 0.0, 1.0);
        _modDepth = Math.Clamp(Effect.ModDepth ?? 0.2, 0.0, 1.0);

        var rate = Math.Max(0.0, Effect.ModRate ?? 0.2);

        if (!force && rate == _modRate)
        {
            return;
        }

        _modRate = rate;
        _phaseIncrement = rate / SampleRate;
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var mix = _mix;
        var dry = 1f - mix;

        for (var i = 0; i < frames; i++)
        {
            var dryLeft = left[i];
            var dryRight = right[i];

            _lineLeft[_writeIndex] = dryLeft;
            _lineRight[_writeIndex] = dryRight;

            var wetLeft = Read(_lineLeft, DelayFrames(BaseDelayLeftSeconds, _phase));
            var wetRight = Read(_lineRight, DelayFrames(BaseDelayRightSeconds, _phaseRight));

            left[i] = (dryLeft * dry) + (wetLeft * mix);
            right[i] = (dryRight * dry) + (wetRight * mix);

            _writeIndex++;
            if (_writeIndex == _capacity)
            {
                _writeIndex = 0;
            }

            _phase += _phaseIncrement;
            if (_phase >= 1.0)
            {
                _phase -= 1.0;
            }

            _phaseRight += _phaseIncrement * RightRateRatio;
            if (_phaseRight >= 1.0)
            {
                _phaseRight -= 1.0;
            }
        }
    }

    // MEASURED: delay = base * (1 + modDepth * sin(2*pi*rate*t)), the sine running -1 to +1 so the
    // centre stays on the base delay and the excursion is proportional to it.
    private double DelayFrames(double baseSeconds, double phase)
    {
        var sine = Math.Sin(2.0 * Math.PI * phase);
        return baseSeconds * (1.0 + (_modDepth * sine)) * SampleRate;
    }

    private float Read(float[] line, double delayFrames)
    {
        var position = _writeIndex - delayFrames;
        while (position < 0.0)
        {
            position += _capacity;
        }

        var index = (int)position;
        var fraction = (float)(position - index);

        if (index >= _capacity)
        {
            index -= _capacity;
        }

        var next = index + 1;
        if (next >= _capacity)
        {
            next -= _capacity;
        }

        return line[index] + (line[next] - line[index]) * fraction;
    }
}

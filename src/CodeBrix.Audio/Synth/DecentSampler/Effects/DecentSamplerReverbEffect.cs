using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The reverb effect, over the Freeverb already in this package (Synth/Reverb.cs, the public-domain
// Jezar algorithm the SoundFont synthesizer uses).
//
// MEASURED, twice. Round 1 (plan section 7 item 6) established that the reference reverb IS a
// Freeverb and recovered the roomSize mapping from RT60. Round 2 item 21 then read the STRUCTURE
// straight off a single-sample impulse response and found stock Freeverb, to the sample:
//
//   - eight parallel combs at 1116 1188 1277 1356 1422 1491 1557 1617 feeding four series allpasses
//     at 556 441 341 225 with allpass feedback 0.5 - every one of the twelve identified in the
//     impulse, along with thirteen comb-plus-allpass sums;
//   - the RIGHT channel adds exactly 23 samples to ALL TWELVE delays, and no cross-channel term
//     appears in either channel, so width is 1;
//   - NO pre-delay, no early reflections and no diffusion: nothing whatever arrives between the dry
//     signal and 1116 samples (25.3 ms);
//   - comb feedback g = 0.7 + 0.28 * roomSize (confirmed a second time);
//   - the damping one-pole in each comb's feedback is damp1 = 0.375 * damping, fitted over 36
//     band/setting cells to 3-6 % - close to but NOT Freeverb's stock 0.4, which is what made this
//     engine's tail darker than the reference's;
//   - the input scale is 0.015 and the wet scale is 3, so ONE COMB ECHO AT wetLevel="1" IS 0.045 OF
//     THE INPUT (measured -26.9 dB against a centred mono source);
//   - wetLevel is a PLAIN LINEAR GAIN ON THE WET PATH. The dry path is never attenuated, so
//     wetLevel="0" bypasses the reverb and leaves the dry signal untouched, and there is no
//     dry/wet crossfade. That is why this effect adds rather than blends.
//
// The shared Freeverb (Synth/Reverb.cs, the public-domain Jezar algorithm the SoundFont synthesizer
// also uses) already carries every tuning, the spread and the allpass feedback. Its two scaling
// constants are the SoundFont path's and must not move, so the two corrections above are applied by
// what this effect hands it: 0.375/0.4 of the damping, and a wet of 1 rather than a third.
//
// Freeverb is mono in and stereo out, so the two channels are AVERAGED into it - the average, not the
// sum, is what puts one comb echo at 0.045 of a centred source - and its own stereo spread produces
// the two outputs.
internal sealed class DecentSamplerReverbEffect : DecentSamplerEffectBase
{
    // MEASURED: the reference keeps Freeverb's stock wet scale of 3, so one comb echo at wetLevel="1"
    // lands on 0.015 * 3 = 0.045 of the input. The shared Freeverb's own Wet property SKIPS its
    // stereo-mix pass whenever the width is 1 and so silently drops any wet scaling written there;
    // the combs are linear, so the scale is applied on the way out of the effect instead, where it is
    // also visible next to wetLevel.
    private const float WetScale = 3f;

    // What the shared Freeverb's Wet property must be set to for that skip to be the identity it is
    // meant to be: a third of its own 3x scale.
    private const float UnityWet = 1f / 3f;

    // MEASURED: damp1 = 0.375 * damping. The shared Freeverb multiplies by its own 0.4, which the
    // SoundFont path depends on, so the ratio is applied on the way in instead.
    private const double DampingScale = 0.375 / 0.4;

    private Reverb _reverb;
    private float[] _mono;
    private float[] _wetLeft;
    private float[] _wetRight;

    private double _roomSize = double.NaN;
    private double _damping = double.NaN;
    private float _wetLevel;

    public DecentSamplerReverbEffect(EffectContext context)
        : base(context, "FX_REVERB_ROOM_SIZE", "FX_REVERB_DAMPING", "FX_REVERB_WET_LEVEL", "FX_WET_LEVEL")
    {
    }

    protected override void OnPrepare()
    {
        _reverb = new Reverb(SampleRate);
        _reverb.Width = 1f;
        _reverb.Wet = UnityWet;

        var capacity = Math.Max(BlockSize, 1);
        _mono = new float[capacity];
        _wetLeft = new float[capacity];
        _wetRight = new float[capacity];

        _roomSize = double.NaN;
        _damping = double.NaN;
    }

    protected override void OnReset() => _reverb?.Mute();

    protected override void Refresh(bool force)
    {
        var roomSize = Math.Clamp(Effect.RoomSize ?? 0.7, 0.0, 1.0);
        var damping = Math.Clamp(Effect.Damping ?? 0.3, 0.0, 1.0);

        _wetLevel = (float)Math.Max(0.0, Effect.WetLevel ?? 0.0);

        if (!force && roomSize == _roomSize && damping == _damping)
        {
            return;
        }

        _roomSize = roomSize;
        _damping = damping;

        _reverb.RoomSize = (float)roomSize;
        _reverb.Damp = (float)(damping * DampingScale);
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        if (_wetLevel <= 0f)
        {
            return;
        }

        var count = Math.Min(frames, _mono.Length);
        var gain = _reverb.InputGain;

        for (var i = 0; i < count; i++)
        {
            _mono[i] = (left[i] + right[i]) * 0.5f * gain;
        }

        _reverb.Process(_mono, _wetLeft, _wetRight, count);

        var wet = _wetLevel * WetScale;
        for (var i = 0; i < count; i++)
        {
            left[i] += _wetLeft[i] * wet;
            right[i] += _wetRight[i] * wet;
        }
    }
}

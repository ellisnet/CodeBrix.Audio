using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The gain effect: a flat level change, in decibels by default and linear when the element writes
// levelUnit="linear".
//
// MEASURED (plan section 7 item 5): level="-6" gave -5.99 dB flat and levelUnit="linear" level="0.5"
// gave -6.01 dB flat, confirming both the default unit and the linear one. The guide's own note that
// levelUnit must be written BEFORE level is an artefact of the reference player's parser; this one
// reads attributes in any order.
internal sealed class DecentSamplerGainEffect : DecentSamplerEffectBase
{
    private double _level = double.NaN;
    private DecentSamplerGainLevelUnit _unit = DecentSamplerGainLevelUnit.Decibels;
    private float _gain = 1f;

    public DecentSamplerGainEffect(EffectContext context)
        : base(context, "LEVEL")
    {
    }

    // The linear factor the effect currently applies.
    public float Gain => _gain;

    protected override void OnPrepare() => _level = double.NaN;

    protected override void Refresh(bool force)
    {
        var level = Effect.Level ?? 0.0;
        var unit = Effect.LevelUnit ?? DecentSamplerGainLevelUnit.Decibels;

        if (!force && level == _level && unit == _unit)
        {
            return;
        }

        _level = level;
        _unit = unit;

        _gain = unit == DecentSamplerGainLevelUnit.Linear
            ? (float)Math.Max(0.0, level)
            : (float)Math.Pow(10.0, level / 20.0);
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var gain = _gain;

        if (gain == 1f)
        {
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            left[i] *= gain;
            right[i] *= gain;
        }
    }
}

using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.ModestSynth.Effects.Internal;

/// <summary>
/// Turns one parsed <c>&lt;effect&gt;</c> element into a configured effect: the factories
/// <c>ModestSynth.Register()</c> hands to the Decent Sampler engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine calls a factory once per chain position, and once per VOICE for a group-level chain,
/// so these methods do nothing expensive. Every attribute is read off the live
/// <see cref="DecentSamplerEffect" />, which is where the binding engine writes effective values, so
/// an effect built after a knob has moved starts at the value the knob is showing. Later changes
/// arrive through <c>TrySetParameter</c>.
/// </para>
/// <para>
/// An attribute the preset did not write is left at the effect's own documented default rather than
/// being pushed as a value, which keeps one list of defaults instead of two.
/// </para>
/// </remarks>
internal static class ModestEffectBuilder
{
    /// <summary>Builds a phaser from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreatePhaser(EffectContext context)
    {
        PhaserEffect effect = new PhaserEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.ModDepth.HasValue) { effect.ModDepth = source.ModDepth.Value; }
        if (source.ModRate.HasValue) { effect.ModRate = source.ModRate.Value; }
        if (source.CenterFrequency.HasValue) { effect.CenterFrequency = source.CenterFrequency.Value; }
        if (source.Feedback.HasValue) { effect.Feedback = source.Feedback.Value; }

        return Finish(effect, context);
    }

    /// <summary>Builds a pitch shifter from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreatePitchShift(EffectContext context)
    {
        PitchShiftEffect effect = new PitchShiftEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.PitchShift.HasValue) { effect.PitchShift = source.PitchShift.Value; }

        return Finish(effect, context);
    }

    /// <summary>Builds a wave folder from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreateWaveFolder(EffectContext context)
    {
        WaveFolderEffect effect = new WaveFolderEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.Drive.HasValue) { effect.Drive = source.Drive.Value; }
        if (source.Threshold.HasValue) { effect.Threshold = source.Threshold.Value; }

        return Finish(effect, context);
    }

    /// <summary>Builds a wave shaper from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreateWaveShaper(EffectContext context)
    {
        WaveShaperEffect effect = new WaveShaperEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.Drive.HasValue) { effect.Drive = source.Drive.Value; }
        if (source.DriveBoost.HasValue) { effect.DriveBoost = source.DriveBoost.Value; }
        if (source.OutputLevel.HasValue) { effect.OutputLevel = source.OutputLevel.Value; }
        if (source.HighQuality.HasValue) { effect.HighQuality = source.HighQuality.Value; }

        return Finish(effect, context);
    }

    /// <summary>Builds a stereo simulator from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreateStereoSimulator(EffectContext context)
    {
        StereoSimulatorEffect effect = new StereoSimulatorEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Algorithm.HasValue)
        {
            switch (source.Algorithm.Value)
            {
                case DecentSamplerStereoSimulatorAlgorithm.Lauridsen:
                    effect.Algorithm = ModestStereoAlgorithm.Lauridsen;
                    break;

                case DecentSamplerStereoSimulatorAlgorithm.Schroeder:
                    effect.Algorithm = ModestStereoAlgorithm.Schroeder;
                    break;

                default:
                    effect.Algorithm = ModestStereoAlgorithm.Adt;
                    break;
            }
        }

        if (source.Width.HasValue) { effect.Width = source.Width.Value; }
        if (source.DelayTime.HasValue) { effect.DelayTime = source.DelayTime.Value; }
        if (source.ModRate.HasValue) { effect.ModRate = source.ModRate.Value; }
        if (source.ModDepth.HasValue) { effect.ModDepth = source.ModDepth.Value; }

        return Finish(effect, context);
    }

    /// <summary>Builds a bit crusher from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreateBitCrusher(EffectContext context)
    {
        BitCrusherEffect effect = new BitCrusherEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.BitDepth.HasValue) { effect.BitDepth = source.BitDepth.Value; }

        if (source.SampleRateReduction.HasValue)
        {
            effect.SampleRateReduction = source.SampleRateReduction.Value;
        }

        return Finish(effect, context);
    }

    /// <summary>Builds a gate from a preset's effect element.</summary>
    /// <param name="context">What the engine knows about the chain position.</param>
    /// <returns>The effect.</returns>
    internal static IInstrumentEffect CreateGate(EffectContext context)
    {
        GateEffect effect = new GateEffect();
        DecentSamplerEffect source = context.Effect;

        if (source.Mix.HasValue) { effect.Mix = source.Mix.Value; }
        if (source.Amount.HasValue) { effect.Amount = source.Amount.Value; }

        // Two gates in one instrument should not stutter in lock step, and the same instrument
        // rendered twice should stutter identically: the position in the chain gives both.
        effect.Seed = GateEffect.DefaultSeed
            ^ (uint)((context.ChainIndex + 1) * 2654435761u)
            ^ (uint)(source.Index * 40503);

        return Finish(effect, context);
    }

    private static IInstrumentEffect Finish(ModestEffectBase effect, EffectContext context)
    {
        DecentSamplerEffect source = context.Effect;

        effect.Tags = source.Tags;
        effect.Enabled = source.Enabled;

        if (context.SampleRate >= ModestEffectBase.MinimumSampleRate
            && context.SampleRate <= ModestEffectBase.MaximumSampleRate)
        {
            effect.Prepare(context.SampleRate);
        }

        return effect;
    }
}

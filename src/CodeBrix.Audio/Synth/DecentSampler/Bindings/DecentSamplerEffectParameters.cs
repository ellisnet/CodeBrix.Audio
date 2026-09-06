using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The <c>FX_*</c> parameter targets. The format writes every effect with one element and tells them
/// apart by <c>type</c>, so one map covers them all; a parameter that means nothing to the effect's
/// type still resolves and still stores its value, which is what the guide's own shared table implies.
/// </summary>
/// <remarks>
/// There is no digital signal processing behind these yet. The targets write the live values onto the
/// parsed <c>&lt;effect&gt;</c>, which is where the effects phase reads them, so a reverb knob moved at
/// load time is already in the right place when the reverb learns to make a sound.
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    /// <summary>The target of an effect parameter, or null when the parameter is not one.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="chain">A name for the chain the effect lives in, used in messages.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForEffect(
        DecentSamplerEffect effect, string chain, string parameter)
    {
        var name = chain + "effect[" + Ordinal(effect.Index) + "]." + parameter;

        switch (Fold(parameter))
        {
            case "enabled":
                return Switch(effect, name, parameter, effect.Enabled, value => effect.Enabled = value);

            case "fxmix":
            case "mix":
                return Number(effect, name, parameter, effect.Mix ?? 0.5, value => effect.Mix = value);

            case "fxirfile":
                return Words(effect, name, parameter, effect.IrFile, value => effect.IrFile = value);

            case "fxfilterfrequency":
                return Number(
                    effect, name, parameter, effect.Frequency ?? 22000.0, value => effect.Frequency = value);

            case "fxfilterq":
                return Number(effect, name, parameter, effect.Q ?? 0.7, value => effect.Q = value);

            case "fxfiltergain":
                return Number(effect, name, parameter, effect.Gain ?? 1.0, value => effect.Gain = value);

            case "fxfilterresonance":
                return Number(
                    effect, name, parameter, effect.Resonance ?? 0.7, value => effect.Resonance = value);

            case "fxreverbwetlevel":
                return Number(effect, name, parameter, effect.WetLevel ?? 0.0, value => effect.WetLevel = value);

            case "fxreverbroomsize":
                return Number(effect, name, parameter, effect.RoomSize ?? 0.7, value => effect.RoomSize = value);

            case "fxreverbdamping":
                return Number(effect, name, parameter, effect.Damping ?? 0.3, value => effect.Damping = value);

            case "fxmoddepth":
                return Number(effect, name, parameter, effect.ModDepth ?? 0.2, value => effect.ModDepth = value);

            case "fxmodrate":
                return Number(effect, name, parameter, effect.ModRate ?? 0.2, value => effect.ModRate = value);

            case "fxcenterfrequency":
                return Number(
                    effect, name, parameter, effect.CenterFrequency ?? 400.0,
                    value => effect.CenterFrequency = value);

            case "fxfeedback":
                return Number(effect, name, parameter, effect.Feedback ?? 0.2, value => effect.Feedback = value);

            case "fxdelaytime":
                return Number(
                    effect, name, parameter, effect.DelayTime ?? 0.7, value => effect.DelayTime = value);

            case "fxdelaytimeformat":
                return Choice<DecentSamplerDelayTimeFormat>(
                    effect, name, parameter, effect.DelayTimeFormat ?? DecentSamplerDelayTimeFormat.Seconds,
                    value => effect.DelayTimeFormat = value);

            case "fxstereooffset":
                return Number(
                    effect, name, parameter, effect.StereoOffset ?? 0.0, value => effect.StereoOffset = value);

            case "fxwetlevel":
                return Number(effect, name, parameter, effect.WetLevel ?? 0.5, value => effect.WetLevel = value);

            case "level":
                return Number(effect, name, parameter, effect.Level ?? 0.0, value => effect.Level = value);

            case "fxpitchshift":
                return Number(
                    effect, name, parameter, effect.PitchShift ?? 0.0, value => effect.PitchShift = value);

            case "fxdrive":
                return Number(effect, name, parameter, effect.Drive ?? 1.0, value => effect.Drive = value);

            case "fxthreshold":
                return Number(
                    effect, name, parameter,
                    effect.Threshold ?? (effect.EffectType == DecentSamplerEffectType.Compressor ? -12.0 : 0.25),
                    value => effect.Threshold = value);

            case "fxoutputlevel":
                return Number(
                    effect, name, parameter, effect.OutputLevel ?? 0.1, value => effect.OutputLevel = value);

            case "fxdriveboost":
                return Number(
                    effect, name, parameter, effect.DriveBoost ?? 1.0, value => effect.DriveBoost = value);

            case "fxbitdepth":
                return Number(effect, name, parameter, effect.BitDepth ?? 24.0, value => effect.BitDepth = value);

            case "fxsampleratereduction":
                return Number(
                    effect, name, parameter, effect.SampleRateReduction ?? 1.0,
                    value => effect.SampleRateReduction = value);

            case "fxgateamount":
                return Number(effect, name, parameter, effect.Amount ?? 0.5, value => effect.Amount = value);

            case "fxwidth":
                return Number(effect, name, parameter, effect.Width ?? 0.5, value => effect.Width = value);

            case "fxratio":
                return Number(effect, name, parameter, effect.Ratio ?? 4.0, value => effect.Ratio = value);

            case "fxattack":
                return Number(effect, name, parameter, effect.Attack ?? 5.0, value => effect.Attack = value);

            case "fxrelease":
                return Number(effect, name, parameter, effect.Release ?? 100.0, value => effect.Release = value);

            case "fxinputgain":
                return Number(effect, name, parameter, effect.InputGain ?? 0.0, value => effect.InputGain = value);

            case "fxoutputgain":
                return Number(
                    effect, name, parameter, effect.OutputGain ?? 0.0, value => effect.OutputGain = value);

            default:
                return null;
        }
    }
}

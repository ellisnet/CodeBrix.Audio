using System;
using CodeBrix.Audio.Synth.DecentSampler.Internal;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// Reading and writing an <effect> element's parameters by their FX_* binding names.
//
// The parsed element is the SINGLE SOURCE OF TRUTH for every effect parameter: the binding engine
// writes the live value there (see DecentSamplerParameterFactory.ForEffect) and the running effect
// reads it back on every block. TrySetParameter on a running effect therefore writes the same place a
// binding would, so the two can never disagree.
//
// Names fold the way the rest of the format folds enumerated values: case, underscores and hyphens are
// all ignored, so FX_WET_LEVEL, fxWetLevel and fx-wet-level are one parameter.
internal static class DecentSamplerEffectValues
{
    public static string Fold(string name) => DecentSamplerEnumNames.Fold(name);

    // The default the guide documents for a parameter, used when the preset did not write it.
    public static double Get(DecentSamplerEffect effect, string folded)
    {
        switch (folded)
        {
            case "enabled": return effect.Enabled ? 1.0 : 0.0;
            case "fxmix":
            case "mix": return effect.Mix ?? 0.5;
            case "fxfilterfrequency": return effect.Frequency ?? 22000.0;
            case "fxfilterq": return effect.Q ?? 0.7;
            case "fxfiltergain": return effect.Gain ?? 1.0;
            case "fxfilterresonance": return effect.Resonance ?? 0.7;
            case "level": return effect.Level ?? 0.0;
            case "fxreverbwetlevel": return effect.WetLevel ?? 0.0;
            case "fxreverbroomsize": return effect.RoomSize ?? 0.7;
            case "fxreverbdamping": return effect.Damping ?? 0.3;
            case "fxwetlevel": return effect.WetLevel ?? 0.5;
            case "fxdelaytime": return effect.DelayTime ?? 0.7;
            case "fxdelaytimeformat":
                return (double)(effect.DelayTimeFormat ?? DecentSamplerDelayTimeFormat.Seconds);
            case "fxfeedback": return effect.Feedback ?? 0.2;
            case "fxstereooffset": return effect.StereoOffset ?? 0.0;
            case "fxmoddepth": return effect.ModDepth ?? 0.2;
            case "fxmodrate": return effect.ModRate ?? 0.2;
            case "fxthreshold": return effect.Threshold ?? -12.0;
            case "fxratio": return effect.Ratio ?? 4.0;
            case "fxattack": return effect.Attack ?? 5.0;
            case "fxrelease": return effect.Release ?? 100.0;
            case "fxinputgain": return effect.InputGain ?? 0.0;
            case "fxoutputgain": return effect.OutputGain ?? 0.0;
            default: return 0.0;
        }
    }

    public static bool Set(DecentSamplerEffect effect, string folded, double value)
    {
        switch (folded)
        {
            case "enabled": effect.Enabled = value >= 0.5; return true;
            case "fxmix":
            case "mix": effect.Mix = value; return true;
            case "fxfilterfrequency": effect.Frequency = value; return true;
            case "fxfilterq": effect.Q = value; return true;
            case "fxfiltergain": effect.Gain = value; return true;
            case "fxfilterresonance": effect.Resonance = value; return true;
            case "level": effect.Level = value; return true;
            case "fxreverbwetlevel":
            case "fxwetlevel": effect.WetLevel = value; return true;
            case "fxreverbroomsize": effect.RoomSize = value; return true;
            case "fxreverbdamping": effect.Damping = value; return true;
            case "fxdelaytime": effect.DelayTime = value; return true;
            case "fxdelaytimeformat":
                effect.DelayTimeFormat = value >= 0.5
                    ? DecentSamplerDelayTimeFormat.MusicalTime
                    : DecentSamplerDelayTimeFormat.Seconds;
                return true;
            case "fxfeedback": effect.Feedback = value; return true;
            case "fxstereooffset": effect.StereoOffset = value; return true;
            case "fxmoddepth": effect.ModDepth = value; return true;
            case "fxmodrate": effect.ModRate = value; return true;
            case "fxthreshold": effect.Threshold = value; return true;
            case "fxratio": effect.Ratio = value; return true;
            case "fxattack": effect.Attack = value; return true;
            case "fxrelease": effect.Release = value; return true;
            case "fxinputgain": effect.InputGain = value; return true;
            case "fxoutputgain": effect.OutputGain = value; return true;
            default: return false;
        }
    }

    public static bool SetText(DecentSamplerEffect effect, string folded, string value)
    {
        if (string.Equals(folded, "fxirfile", StringComparison.Ordinal))
        {
            effect.IrFile = value;
            return true;
        }

        return false;
    }
}

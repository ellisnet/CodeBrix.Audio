using System;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The three documented translation modes, which turn a binding's source value into the value its
/// target receives.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>linear</c> maps the source's own range onto
/// <c>translationOutputMin</c>..<c>translationOutputMax</c>, backwards when
/// <c>translationReversed</c> is set. When neither output bound is written the source value passes
/// through unchanged, which is what a bare <c>&lt;binding parameter="AMP_VOLUME"/&gt;</c> on a 0-to-1
/// knob relies on.</description></item>
/// <item><description><c>table</c> reads the RAW source value along the table's input axis and
/// interpolates between the neighbouring points, clamping at both ends. The inputs are raw, not
/// normalised: presets in the wild write tables with inputs 0 to 128 for a MIDI controller and 0 to 1
/// for a knob.</description></item>
/// <item><description><c>fixed_value</c> ignores the source and always sends
/// <c>translationValue</c>, which the target coerces to a number, a switch, an enumeration name, a
/// colour or plain text.</description></item>
/// </list>
/// </remarks>
internal static class DecentSamplerTranslator
{
    /// <summary>Translates a source value into the value a target should receive.</summary>
    /// <param name="binding">The binding, which carries the translation.</param>
    /// <param name="input">What the source is offering.</param>
    /// <returns>The translated value.</returns>
    public static DecentSamplerParameterValue Translate(
        DecentSamplerBinding binding, DecentSamplerBindingInput input)
    {
        if (binding.Translation == DecentSamplerTranslation.FixedValue)
        {
            return DecentSamplerParameterValue.FromText(binding.TranslationValue);
        }

        return DecentSamplerParameterValue.FromNumber(TranslateNumber(binding, input));
    }

    /// <summary>
    /// The output the modulator's RESTING raw value translates to, which is what the <c>modulate</c>
    /// behaviour takes back out so that a modulator sitting at rest contributes nothing.
    /// </summary>
    /// <param name="binding">The binding, which carries the translation.</param>
    /// <param name="input">A source input; only its range is used.</param>
    /// <param name="restingRawOutput">
    /// The raw value the modulator produces at rest: measured as zero for a <c>&lt;midiCC&gt;</c> and
    /// 0.5 for an <c>&lt;lfo&gt;</c>. It is read against the same range the live value is read against.
    /// </param>
    /// <returns>The neutral output value.</returns>
    public static double NeutralOutput(
        DecentSamplerBinding binding, DecentSamplerBindingInput input, double restingRawOutput)
    {
        if (binding.Translation == DecentSamplerTranslation.FixedValue)
        {
            return DecentSamplerParameterValue.FromText(binding.TranslationValue).AsNumber;
        }

        return TranslateNumber(
            binding, new DecentSamplerBindingInput(restingRawOutput, input.Minimum, input.Maximum));
    }

    private static double TranslateNumber(DecentSamplerBinding binding, DecentSamplerBindingInput input)
    {
        if (binding.Translation == DecentSamplerTranslation.Table)
        {
            return FromTable(binding, input.Value);
        }

        var reversed = binding.TranslationReversed ?? false;

        if (binding.TranslationOutputMin == null && binding.TranslationOutputMax == null)
        {
            // No output range: the source value passes straight through, mirrored within its own range
            // when the binding is reversed.
            return reversed ? input.Minimum + input.Maximum - input.Value : input.Value;
        }

        var outputMin = binding.TranslationOutputMin ?? 0.0;
        var outputMax = binding.TranslationOutputMax ?? 1.0;
        var span = input.Maximum - input.Minimum;
        var position = span == 0.0 ? 0.0 : (input.Value - input.Minimum) / span;

        if (reversed)
        {
            position = 1.0 - position;
        }

        return outputMin + (position * (outputMax - outputMin));
    }

    private static double FromTable(DecentSamplerBinding binding, double value)
    {
        var points = binding.TranslationTable;

        if (points == null || points.Count == 0)
        {
            // The documented default table is 0,0;1,1, which is the identity.
            return value;
        }

        if (points.Count == 1 || value <= points[0].Input)
        {
            return points[0].Output;
        }

        var last = points[points.Count - 1];

        if (value >= last.Input)
        {
            return last.Output;
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            var low = points[index];
            var high = points[index + 1];

            if (value < low.Input || value > high.Input)
            {
                continue;
            }

            var span = high.Input - low.Input;

            if (Math.Abs(span) < double.Epsilon)
            {
                return high.Output;
            }

            var position = (value - low.Input) / span;
            return low.Output + (position * (high.Output - low.Output));
        }

        return last.Output;
    }
}

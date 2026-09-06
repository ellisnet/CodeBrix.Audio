using System;
using System.Globalization;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// One value on its way into a parameter target: a number, a switch, or text. A binding may produce
/// any of the three and the target coerces it, because the format writes <c>translationValue="true"</c>
/// and <c>translationValue="fast"</c> in the same attribute a knob writes a number into.
/// </summary>
internal readonly struct DecentSamplerParameterValue
{
    private DecentSamplerParameterValue(DecentSamplerParameterValueKind kind, double number, string text)
    {
        Kind = kind;
        Number = number;
        Text = text;
    }

    /// <summary>What the value is.</summary>
    public DecentSamplerParameterValueKind Kind { get; }

    /// <summary>The number, meaningful when <see cref="Kind"/> is a number or a switch.</summary>
    public double Number { get; }

    /// <summary>The text, meaningful when <see cref="Kind"/> is text.</summary>
    public string Text { get; }

    /// <summary>A numeric value.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The value.</returns>
    public static DecentSamplerParameterValue FromNumber(double number) =>
        new DecentSamplerParameterValue(DecentSamplerParameterValueKind.Number, number, null);

    /// <summary>A true/false value.</summary>
    /// <param name="value">The switch position.</param>
    /// <returns>The value.</returns>
    public static DecentSamplerParameterValue FromBoolean(bool value) =>
        new DecentSamplerParameterValue(
            DecentSamplerParameterValueKind.Boolean, value ? 1.0 : 0.0, null);

    /// <summary>A textual value.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static DecentSamplerParameterValue FromText(string text) =>
        new DecentSamplerParameterValue(DecentSamplerParameterValueKind.Text, 0.0, text);

    /// <summary>An action, which carries nothing but the fact that it happened.</summary>
    /// <returns>The value.</returns>
    public static DecentSamplerParameterValue Action() =>
        new DecentSamplerParameterValue(DecentSamplerParameterValueKind.Action, 1.0, null);

    /// <summary>
    /// The value as a number. Text is read as a number when it is one, as 1 for <c>true</c> and 0 for
    /// <c>false</c>, and as zero otherwise.
    /// </summary>
    public double AsNumber
    {
        get
        {
            if (Kind != DecentSamplerParameterValueKind.Text)
            {
                return Number;
            }

            if (Text == null)
            {
                return 0.0;
            }

            if (double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return AsBoolean ? 1.0 : 0.0;
        }
    }

    /// <summary>
    /// The value as a switch. A number is true when it is not zero; text is true for <c>true</c>,
    /// <c>yes</c>, <c>on</c> and any non-zero number.
    /// </summary>
    public bool AsBoolean
    {
        get
        {
            if (Kind != DecentSamplerParameterValueKind.Text)
            {
                return Number != 0.0;
            }

            if (string.IsNullOrWhiteSpace(Text))
            {
                return false;
            }

            var trimmed = Text.Trim();

            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                   parsed != 0.0;
        }
    }

    /// <summary>The value as text. A number is written with the invariant culture.</summary>
    public string AsText =>
        Kind == DecentSamplerParameterValueKind.Text
            ? Text
            : Kind == DecentSamplerParameterValueKind.Boolean
                ? Number != 0.0 ? "true" : "false"
                : Number.ToString("R", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}:{AsText}";
}

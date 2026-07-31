using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One modulation curve: a mapping from a normalized controller value (0 to 1) to an output value,
/// referenced from CC modulations by curve index.
/// </summary>
/// <remarks>
/// <para>
/// Indices 0 through 6 are the built-in curves every SFZ player provides: linear, bipolar, inverted,
/// bipolar inverted, concave, convex power, and inverse power. Files define further curves with
/// <c>&lt;curve&gt;</c> headers carrying <c>curve_index</c> and <c>v0</c>..<c>v127</c> vertex opcodes;
/// vertices not given explicitly are interpolated linearly between the ones that are, which is what the
/// SFZ specification prescribes.
/// </para>
/// <para>
/// The specification gives no formulas for the three non-linear built-ins. Curve 4 is implemented as the
/// square (the concave shape of the SFZ default velocity response), and curves 5 and 6 as the equal-power
/// crossfade pair (square root in, square root out), which is the behaviour libraries written for ARIA
/// expect of the "power" curves.
/// </para>
/// </remarks>
public sealed class SfzCurve
{
    private readonly float[] _table;

    private SfzCurve(float[] table)
    {
        _table = table;
    }

    /// <summary>Curve 0 - linear, 0 to 1. The default for every CC modulation.</summary>
    public static SfzCurve Linear { get; } = FromFunction(x => x);

    /// <summary>Curve 1 - bipolar linear, -1 to 1, neutral at center.</summary>
    public static SfzCurve Bipolar { get; } = FromFunction(x => 2f * x - 1f);

    /// <summary>Curve 2 - inverted linear, 1 to 0.</summary>
    public static SfzCurve Inverted { get; } = FromFunction(x => 1f - x);

    /// <summary>Curve 3 - bipolar inverted, 1 to -1.</summary>
    public static SfzCurve BipolarInverted { get; } = FromFunction(x => 1f - 2f * x);

    /// <summary>Curve 4 - concave, 0 to 1 (implemented as the square).</summary>
    public static SfzCurve Concave { get; } = FromFunction(x => x * x);

    /// <summary>Curve 5 - crossfade-in power curve, 0 to 1 (implemented as the square root).</summary>
    public static SfzCurve PowerIn { get; } = FromFunction(x => MathF.Sqrt(x));

    /// <summary>Curve 6 - crossfade-out power curve, 1 to 0 (implemented as the square root of the remainder).</summary>
    public static SfzCurve PowerOut { get; } = FromFunction(x => MathF.Sqrt(1f - x));

    /// <summary>
    /// Builds a curve from the vertex opcodes of a <c>&lt;curve&gt;</c> section. Missing vertices are
    /// interpolated linearly; a curve with no vertices at all comes out linear.
    /// </summary>
    /// <param name="vertices">The defined vertices: controller value (0-127) to output value.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> is null.</exception>
    public static SfzCurve FromVertices(IReadOnlyDictionary<int, float> vertices)
    {
        if (vertices == null)
        {
            throw new ArgumentNullException(nameof(vertices));
        }

        var table = new float[128];
        var defined = new bool[128];

        foreach (var pair in vertices)
        {
            if (0 <= pair.Key && pair.Key <= 127)
            {
                table[pair.Key] = pair.Value;
                defined[pair.Key] = true;
            }
        }

        // Before the first defined vertex the curve holds its value; same after the last. In between,
        // interpolate linearly - and a curve defining nothing at all falls back to linear 0..1.
        var firstDefined = Array.IndexOf(defined, true);
        if (firstDefined < 0)
        {
            return Linear;
        }

        for (var i = 0; i < firstDefined; i++)
        {
            table[i] = table[firstDefined];
        }

        var previous = firstDefined;
        for (var i = firstDefined + 1; i < 128; i++)
        {
            if (!defined[i])
            {
                continue;
            }

            var span = i - previous;
            for (var j = previous + 1; j < i; j++)
            {
                var t = (float)(j - previous) / span;
                table[j] = table[previous] + t * (table[i] - table[previous]);
            }

            previous = i;
        }

        for (var i = previous + 1; i < 128; i++)
        {
            table[i] = table[previous];
        }

        return new SfzCurve(table);
    }

    /// <summary>
    /// Evaluates the curve for a normalized controller value.
    /// </summary>
    /// <param name="normalizedValue">The controller value scaled to 0..1. Values outside are clamped.</param>
    /// <returns>The curve output.</returns>
    public float Evaluate(float normalizedValue)
    {
        var position = Math.Clamp(normalizedValue, 0f, 1f) * 127f;
        var index = (int)position;
        if (index >= 127)
        {
            return _table[127];
        }

        var fraction = position - index;
        return _table[index] + fraction * (_table[index + 1] - _table[index]);
    }

    private static SfzCurve FromFunction(Func<float, float> function)
    {
        var table = new float[128];
        for (var i = 0; i < 128; i++)
        {
            table[i] = function(i / 127f);
        }

        return new SfzCurve(table);
    }
}

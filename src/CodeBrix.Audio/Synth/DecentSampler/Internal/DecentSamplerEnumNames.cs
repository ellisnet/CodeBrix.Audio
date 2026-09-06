using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Internal;

/// <summary>
/// Matches the enumeration values written in a <c>.dspreset</c> to the enum members this engine uses.
/// </summary>
/// <remarks>
/// <para>
/// The format writes its enumerations in lower snake case (<c>round_robin</c>, <c>disk_streaming</c>,
/// <c>AUX_STEREO_OUTPUT_3</c>) and occasionally in camel case (<c>replayPerOctave</c>,
/// <c>noteOneSixteenth</c>). Folding both the written value and the member name to lower case with
/// every underscore removed makes one comparison serve all of them, which is why there is no
/// hand-written name table per enum.
/// </para>
/// <para>
/// The few names that do not fold onto their member - the legacy filter spellings, mostly - are
/// listed in <see cref="Aliases"/>.
/// </para>
/// </remarks>
internal static class DecentSamplerEnumNames
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        // The legacy name of the two-pole low-pass filter. The "4pl" is historical; the guide
        // describes the filter itself as two-pole.
        ["lowpass4pl"] = "lowpass",
        ["lowpass1pl"] = "lowpass1pole",
        ["lowpass2pl"] = "lowpass",
        ["hipass"] = "highpass",
        ["highpass2pl"] = "highpass",
        ["bandpass2pl"] = "bandpass",
        // <midi><note eventType="..."> is written both ways in the wild.
        ["noteon"] = "noteon",
        // <binding level="groups"> means the <groups> element, which is what level="instrument"
        // addresses: the instrument-wide volume, tuning, pan and envelope all live on it. Six presets
        // in the nine-library corpus write it, so accept it rather than report it.
        ["groups"] = "instrument",
        // The <lfo> and <control> elements both use "hz".
        ["hertz"] = "hz",
    };

    private static readonly object Gate = new object();

    private static readonly Dictionary<Type, Dictionary<string, object>> Tables = [];

    /// <summary>
    /// Matches the text of an attribute to a member of <typeparamref name="TEnum"/>.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration to match against.</typeparam>
    /// <param name="text">The attribute text.</param>
    /// <param name="value">The matched member.</param>
    /// <returns><see langword="true"/> when the text names a member.</returns>
    public static bool TryParse<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = Fold(text);
        if (Aliases.TryGetValue(key, out var alias))
        {
            key = alias;
        }

        var table = TableFor<TEnum>();
        if (!table.TryGetValue(key, out var member))
        {
            return false;
        }

        value = (TEnum)member;
        return true;
    }

    /// <summary>Whether the text names a member of <typeparamref name="TEnum"/>.</summary>
    /// <typeparam name="TEnum">The enumeration to match against.</typeparam>
    /// <param name="text">The attribute text.</param>
    /// <returns><see langword="true"/> when the text names a member.</returns>
    public static bool IsKnown<TEnum>(string text) where TEnum : struct, Enum =>
        TryParse<TEnum>(text, out _);

    /// <summary>Lower-cases a name and removes its underscores, hyphens and spaces.</summary>
    /// <param name="text">The name to fold.</param>
    /// <returns>The folded name.</returns>
    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        Span<char> buffer = text.Length <= 128 ? stackalloc char[text.Length] : new char[text.Length];
        var length = 0;

        foreach (var character in text)
        {
            if (character == '_' || character == '-' || character == ' ')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..length]);
    }

    private static Dictionary<string, object> TableFor<TEnum>() where TEnum : struct, Enum
    {
        lock (Gate)
        {
            if (Tables.TryGetValue(typeof(TEnum), out var existing))
            {
                return existing;
            }

            var table = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var name in Enum.GetNames<TEnum>())
            {
                table[Fold(name)] = Enum.Parse<TEnum>(name);
            }

            Tables[typeof(TEnum)] = table;
            return table;
        }
    }
}

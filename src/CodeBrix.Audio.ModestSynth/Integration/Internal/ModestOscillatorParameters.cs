using System;

namespace CodeBrix.Audio.ModestSynth.Integration.Internal;

// Folds an OSCILLATOR_* binding parameter name onto the short form the adapter switches on, and reads
// the index out of the two families that carry one.
//
// The rule matches the core engine's: lower case, with underscores, hyphens and spaces removed, and a
// leading "oscillator" dropped. So OSCILLATOR_WAVETABLE_POSITION, oscillatorWavetablePosition and
// "wavetable position" are one parameter. Folding allocates a string, so it belongs to the control
// path and never to a render callback.
internal static class ModestOscillatorParameters
{
    private const string Prefix = "oscillator";

    internal static string Fold(string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) { return string.Empty; }

        Span<char> buffer = parameter.Length <= 128
            ? stackalloc char[parameter.Length]
            : new char[parameter.Length];
        int length = 0;

        foreach (char character in parameter)
        {
            if (character == '_' || character == '-' || character == ' ') { continue; }
            buffer[length++] = char.ToLowerInvariant(character);
        }

        ReadOnlySpan<char> folded = buffer[..length];

        if (folded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            folded = folded[Prefix.Length..];
        }

        return new string(folded);
    }

    // "harmonicpartial12level" -> 12. Returns false for anything else, and for an index outside 1..count.
    internal static bool TryIndexed(
        string folded, string prefix, string suffix, int count, out int index, out string tail)
    {
        index = 0;
        tail = null;

        if (folded == null ||
            !folded.StartsWith(prefix, StringComparison.Ordinal) ||
            folded.Length <= prefix.Length)
        {
            return false;
        }

        int digits = prefix.Length;
        while (digits < folded.Length && folded[digits] >= '0' && folded[digits] <= '9') { digits++; }

        if (digits == prefix.Length) { return false; }

        string number = folded.Substring(prefix.Length, digits - prefix.Length);

        if (!int.TryParse(number, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out index) ||
            index < 1 || index > count)
        {
            return false;
        }

        tail = folded.Substring(digits);

        if (suffix == null) { return true; }

        return string.Equals(tail, suffix, StringComparison.Ordinal);
    }
}

using System;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// The waveform names exactly as a Decent Sampler <c>&lt;oscillator&gt;</c> element spells them,
/// plus conversion between those names and <see cref="ModestWaveform" />.
/// </summary>
/// <remarks>
/// Parsing is case-insensitive and trims surrounding whitespace, which is how the rest of the
/// family reads attribute values. An unrecognised name is never an exception: <see cref="TryParse" />
/// returns <see langword="false" /> so the caller can report it and carry on.
/// </remarks>
public static class ModestWaveforms
{
    /// <summary>The <c>sine</c> waveform name.</summary>
    public const string Sine = "sine";

    /// <summary>The <c>saw</c> waveform name.</summary>
    public const string Saw = "saw";

    /// <summary>The <c>square</c> waveform name.</summary>
    public const string Square = "square";

    /// <summary>The <c>triangle</c> waveform name.</summary>
    public const string Triangle = "triangle";

    /// <summary>The <c>noise</c> waveform name.</summary>
    public const string Noise = "noise";

    /// <summary>The <c>white_noise</c> waveform name, a synonym for <see cref="Noise" />.</summary>
    public const string WhiteNoise = "white_noise";

    /// <summary>The <c>pluck1</c> waveform name.</summary>
    public const string Pluck1 = "pluck1";

    /// <summary>The <c>wavetable</c> waveform name.</summary>
    public const string Wavetable = "wavetable";

    /// <summary>The <c>harmonic</c> waveform name.</summary>
    public const string Harmonic = "harmonic";

    /// <summary>The <c>fm6op</c> waveform name.</summary>
    public const string Fm6Op = "fm6op";

    /// <summary>The <c>formant</c> waveform name, which the developer guide does not list.</summary>
    public const string Formant = "formant";

    /// <summary>
    /// Converts a waveform to the name a preset spells it with.
    /// </summary>
    /// <param name="waveform">The waveform.</param>
    /// <returns>
    /// The lower-case name, for example <c>"pluck1"</c>. <see cref="ModestWaveform.Noise" />
    /// returns <see cref="Noise" />, never the <see cref="WhiteNoise" /> synonym.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="waveform" /> is not a defined value.</exception>
    public static string ToName(ModestWaveform waveform)
    {
        switch (waveform)
        {
            case ModestWaveform.Sine: return Sine;
            case ModestWaveform.Saw: return Saw;
            case ModestWaveform.Square: return Square;
            case ModestWaveform.Triangle: return Triangle;
            case ModestWaveform.Noise: return Noise;
            case ModestWaveform.Pluck1: return Pluck1;
            case ModestWaveform.Wavetable: return Wavetable;
            case ModestWaveform.Harmonic: return Harmonic;
            case ModestWaveform.Fm6Op: return Fm6Op;
            case ModestWaveform.Formant: return Formant;
            default:
                throw new ArgumentOutOfRangeException(nameof(waveform), waveform, "Unknown waveform.");
        }
    }

    /// <summary>
    /// Parses a waveform name as a preset spells it.
    /// </summary>
    /// <param name="name">The name, for example <c>"square"</c>. Case and surrounding whitespace do not matter.</param>
    /// <param name="waveform">
    /// On success, the parsed waveform; otherwise <see cref="ModestWaveform.Sine" />, which is the
    /// documented default for an <c>&lt;oscillator&gt;</c> with no waveform attribute.
    /// </param>
    /// <returns><see langword="true" /> when the name was recognised.</returns>
    public static bool TryParse(string name, out ModestWaveform waveform)
    {
        waveform = ModestWaveform.Sine;
        if (string.IsNullOrWhiteSpace(name)) { return false; }

        string trimmed = name.Trim();

        if (string.Equals(trimmed, Sine, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Sine; return true; }
        if (string.Equals(trimmed, Saw, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Saw; return true; }
        if (string.Equals(trimmed, Square, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Square; return true; }
        if (string.Equals(trimmed, Triangle, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Triangle; return true; }
        if (string.Equals(trimmed, Noise, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Noise; return true; }
        if (string.Equals(trimmed, WhiteNoise, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Noise; return true; }
        if (string.Equals(trimmed, Pluck1, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Pluck1; return true; }
        if (string.Equals(trimmed, Wavetable, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Wavetable; return true; }
        if (string.Equals(trimmed, Harmonic, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Harmonic; return true; }
        if (string.Equals(trimmed, Fm6Op, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Fm6Op; return true; }
        if (string.Equals(trimmed, Formant, StringComparison.OrdinalIgnoreCase)) { waveform = ModestWaveform.Formant; return true; }

        return false;
    }
}

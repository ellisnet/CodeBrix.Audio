using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Wavetable;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// Creates an oscillator from a waveform, and says which waveforms this release can actually
/// generate.
/// </summary>
/// <remarks>
/// <para>
/// The parameter model in <c>ModestPatch</c> covers every waveform the format documents, plus the
/// undocumented <c>formant</c> the reference player turns out to accept, and this release generates
/// all of them. <see cref="IsSupported(ModestWaveform)" /> is the honest answer to "can it be played
/// yet", and it stays because the enumeration may grow before the generators do.
/// </para>
/// <para>
/// A <c>wavetable</c> built here has no table loaded, so it renders a sine until one is given to
/// it - which is exactly what the reference player does with a wavetable oscillator that names no
/// file.
/// </para>
/// <para>
/// A freshly created oscillator carries its own defaults; it has not been given the patch's
/// parameters. Use <c>ModestPatch.CreateOscillator()</c> to get a configured one.
/// </para>
/// </remarks>
public static class ModestOscillatorFactory
{
    private static readonly ModestWaveform[] Supported =
    {
        ModestWaveform.Sine,
        ModestWaveform.Saw,
        ModestWaveform.Square,
        ModestWaveform.Triangle,
        ModestWaveform.Noise,
        ModestWaveform.Pluck1,
        ModestWaveform.Fm6Op,
        ModestWaveform.Wavetable,
        ModestWaveform.Harmonic,
        ModestWaveform.Formant,
    };

    /// <summary>
    /// Every waveform this release can generate, in the order they are documented.
    /// </summary>
    public static IReadOnlyList<ModestWaveform> SupportedWaveforms => Supported;

    /// <summary>
    /// Whether <see cref="Create(ModestWaveform)" /> can build this waveform.
    /// </summary>
    /// <param name="waveform">The waveform to ask about.</param>
    /// <returns><see langword="true" /> when a sound generator exists for it.</returns>
    public static bool IsSupported(ModestWaveform waveform)
    {
        for (int i = 0; i < Supported.Length; i++)
        {
            if (Supported[i] == waveform) { return true; }
        }

        return false;
    }

    /// <summary>
    /// Whether a waveform NAME is both recognised and generatable.
    /// </summary>
    /// <param name="name">The name as a preset spells it, for example <c>"pluck1"</c>.</param>
    /// <returns>
    /// <see langword="true" /> when the name parses and a sound generator exists for it. An
    /// unrecognised name returns <see langword="false" /> rather than throwing.
    /// </returns>
    public static bool IsSupported(string name)
        => ModestWaveforms.TryParse(name, out ModestWaveform waveform) && IsSupported(waveform);

    /// <summary>
    /// Creates an oscillator for a waveform, with that oscillator's own defaults.
    /// </summary>
    /// <param name="waveform">The waveform to build.</param>
    /// <returns>A new oscillator; never null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="waveform" /> is not a defined value.</exception>
    public static IModestOscillator Create(ModestWaveform waveform)
    {
        switch (waveform)
        {
            case ModestWaveform.Sine: return new SineOscillator();
            case ModestWaveform.Saw: return new SawOscillator();
            case ModestWaveform.Square: return new SquareOscillator();
            case ModestWaveform.Triangle: return new TriangleOscillator();
            case ModestWaveform.Noise: return new NoiseOscillator();
            case ModestWaveform.Pluck1: return new Pluck1Oscillator();
            case ModestWaveform.Fm6Op: return new Fm6OpOscillator();
            case ModestWaveform.Wavetable: return new WavetableOscillator();
            case ModestWaveform.Harmonic: return new HarmonicOscillator();

            case ModestWaveform.Formant: return new FormantOscillator();

            default:
                throw new ArgumentOutOfRangeException(nameof(waveform), waveform, "Unknown waveform.");
        }
    }

    /// <summary>
    /// Creates an oscillator from a waveform NAME, without throwing when the name is unknown or
    /// the generator has not shipped.
    /// </summary>
    /// <param name="name">The name as a preset spells it, for example <c>"square"</c>.</param>
    /// <param name="oscillator">On success, the new oscillator; otherwise null.</param>
    /// <returns><see langword="true" /> when an oscillator was created.</returns>
    public static bool TryCreate(string name, out IModestOscillator oscillator)
    {
        oscillator = null;

        if (!ModestWaveforms.TryParse(name, out ModestWaveform waveform)) { return false; }
        if (!IsSupported(waveform)) { return false; }

        oscillator = Create(waveform);
        return true;
    }
}

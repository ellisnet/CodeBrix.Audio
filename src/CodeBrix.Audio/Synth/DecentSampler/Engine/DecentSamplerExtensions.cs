using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Effects;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// The process-wide registry of oscillator waveforms and effect types that add-on packages plug into.
/// </summary>
/// <remarks>
/// <para>
/// CodeBrix.Audio cannot reference its own add-ons, so an add-on registers itself: reference the
/// package and call its <c>Register()</c> method once at start-up, the same pattern the Opus codec and
/// the Dav1d video codec follow. Nothing is discovered by reflection, which keeps trimming and
/// ahead-of-time compilation working.
/// </para>
/// <para>
/// RESOLUTION HAPPENS WHEN AN INSTRUMENT IS BUILT INTO A SYNTHESIZER, not when the file is parsed.
/// Register before constructing a <see cref="DecentSamplerSynthesizer"/>. Registering afterwards does
/// not retrofit a synthesizer that already exists - build it again.
/// </para>
/// <para>
/// Without the add-on a preset still loads and every sampled group still plays: an oscillator zone
/// whose waveform is unregistered stays silent, and the synthesizer's <c>Problems</c> and
/// <c>UnsupportedFeatures</c> name the package and the call that would bring it to life.
/// </para>
/// <para>
/// The core registers no oscillator waveforms at all; every waveform in the format lives in the add-on.
/// It DOES register its own effect set - see <see cref="DecentSamplerCoreEffects"/> - into
/// <see cref="Shared"/> when this class is first touched, so filters, gain, reverb, delay, chorus,
/// convolution and the compressor work with nothing called.
/// </para>
/// </remarks>
public static class DecentSamplerExtensions
{
    // The core effect set goes into the shared registry once, before anything can look a type up. A
    // registry built by hand starts empty; call DecentSamplerCoreEffects.RegisterInto to fill one.
    static DecentSamplerExtensions() => DecentSamplerCoreEffects.RegisterInto(Shared);

    /// <summary>The registry every synthesizer uses unless its settings name another one.</summary>
    public static DecentSamplerExtensionRegistry Shared { get; } = new DecentSamplerExtensionRegistry();

    /// <summary>The waveform names registered in <see cref="Shared"/>.</summary>
    public static IReadOnlyList<string> RegisteredOscillators => Shared.RegisteredOscillators;

    /// <summary>The effect type names registered in <see cref="Shared"/>.</summary>
    public static IReadOnlyList<string> RegisteredEffects => Shared.RegisteredEffects;

    /// <summary>
    /// Registers a factory for an oscillator waveform in <see cref="Shared"/>.
    /// </summary>
    /// <param name="waveform">The waveform name, for example <c>fm6op</c>.</param>
    /// <param name="factory">Builds one voice source per voice.</param>
    /// <exception cref="ArgumentNullException"><paramref name="waveform"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="waveform"/> is blank.</exception>
    public static void RegisterOscillator(string waveform, Func<OscillatorContext, IVoiceSource> factory) =>
        Shared.RegisterOscillator(waveform, factory);

    /// <summary>
    /// Registers a factory for an effect type in <see cref="Shared"/>.
    /// </summary>
    /// <param name="type">The effect type name, for example <c>bit_crusher</c>.</param>
    /// <param name="factory">Builds one effect instance per chain position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="type"/> is blank.</exception>
    public static void RegisterEffect(string type, Func<EffectContext, IInstrumentEffect> factory) =>
        Shared.RegisterEffect(type, factory);

    /// <summary>Whether <see cref="Shared"/> has a factory for a waveform.</summary>
    /// <param name="waveform">The waveform name.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public static bool IsOscillatorRegistered(string waveform) => Shared.IsOscillatorRegistered(waveform);

    /// <summary>Whether <see cref="Shared"/> has a factory for an effect type.</summary>
    /// <param name="type">The effect type name.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public static bool IsEffectRegistered(string type) => Shared.IsEffectRegistered(type);

    /// <summary>
    /// The line reported for a zone whose waveform no registered factory covers. Every waveform in the
    /// format lives in the ModestSynth add-on, so the message always names that package.
    /// </summary>
    /// <param name="waveform">The waveform name the preset asked for.</param>
    /// <returns>The problem text.</returns>
    public static string MissingOscillatorMessage(string waveform) =>
        "oscillator waveform '" + (waveform ?? "(unnamed)") +
        "' needs CodeBrix.Audio.ModestSynth: reference it and call ModestSynth.Register() before loading";

    /// <summary>
    /// The line reported for an effect whose type no registered factory covers.
    /// </summary>
    /// <param name="type">The effect type name the preset asked for.</param>
    /// <returns>The problem text.</returns>
    public static string MissingEffectMessage(string type) =>
        "effect type '" + (type ?? "(unnamed)") +
        "' needs CodeBrix.Audio.ModestSynth: reference it and call ModestSynth.Register() before loading";
}

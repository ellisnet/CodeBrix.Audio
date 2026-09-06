using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Integration;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// The one call that turns on synthesis inside a Decent Sampler instrument:
/// <c>ModestSynth.Register();</c>
/// </summary>
/// <remarks>
/// <para>
/// Call it once, early, and always BEFORE loading the instrument that needs it. Waveforms are
/// resolved while an instrument is being built, not while its file is being parsed, so registering
/// afterwards does not retrofit an instrument that is already loaded - reload it.
/// </para>
/// <para>
/// There is deliberately NO module initializer doing this for you. A module initializer only runs
/// once something in the assembly is touched, which trimming and lazy assembly loading make
/// unreliable: the package would work in a debug build and silently fail to register in a trimmed
/// publish. An explicit call is the contract, and it is also what keeps this package free of
/// reflection and assembly probing.
/// </para>
/// <para>
/// The standalone API - <c>ModestPatch</c>, the oscillators in
/// <c>CodeBrix.Audio.ModestSynth.Oscillators</c> and the effects in
/// <c>CodeBrix.Audio.ModestSynth.Effects</c> - needs no registration at all. Registration only
/// wires this package into Decent Sampler preset loading.
/// </para>
/// </remarks>
public static class ModestSynth
{
    private static readonly object Gate = new object();

    private static bool registered;

    /// <summary>
    /// Registers every waveform this package generates, and every creative effect it supplies, with
    /// the Decent Sampler engine in CodeBrix.Audio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Waveforms are resolved when an instrument is BUILT into a synthesizer, not when its file is
    /// parsed, so this has to run before that. Registering afterwards does not retrofit a synthesizer
    /// that already exists - build it again.
    /// </para>
    /// <para>
    /// Idempotent and safe to call from any thread; calling it more than once does nothing and
    /// registers nothing twice. Registration is process-wide: it writes into
    /// <c>DecentSamplerExtensions.Shared</c>, which is the registry a synthesizer uses unless its
    /// settings name another one.
    /// </para>
    /// </remarks>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) { return; }

            RegisterOscillators(DecentSamplerExtensions.Shared);
            RegisteredOscillatorWaveforms = BuildRegisteredWaveforms();

            RegisterEffects(DecentSamplerExtensions.Shared);
            RegisteredEffectTypes = BuildRegisteredEffectTypes();

            registered = true;
        }
    }

    /// <summary>
    /// Registers this package's waveforms and creative effects with a registry of your own instead of
    /// the process-wide one.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to - a <c>DecentSamplerSynthesizerSettings.Extensions</c> value, or an
    /// isolated one built for a test.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="registry" /> is null.</exception>
    /// <remarks>
    /// This form leaves <see cref="IsRegistered" />, <see cref="RegisteredOscillatorWaveforms" /> and
    /// <see cref="RegisteredEffectTypes" /> alone: they describe the process-wide registration that
    /// <see cref="Register()" /> performs. Calling it twice on the same registry is harmless - a
    /// registration replaces the one before it.
    /// </remarks>
    public static void Register(DecentSamplerExtensionRegistry registry)
    {
        if (registry == null) { throw new ArgumentNullException(nameof(registry)); }

        RegisterOscillators(registry);
        RegisterEffects(registry);
    }

    /// <summary>Whether <see cref="Register()" /> has run.</summary>
    public static bool IsRegistered
    {
        get { lock (Gate) { return registered; } }
    }

    /// <summary>
    /// The waveform names <see cref="Register()" /> offered the engine, spelled as a preset spells
    /// them - empty until <see cref="Register()" /> has run.
    /// </summary>
    /// <remarks>
    /// Useful in start-up diagnostics ("which oscillators does this build know?") and in tests. The
    /// list includes every accepted synonym, so white noise appears under both of its names.
    /// </remarks>
    public static IReadOnlyList<string> RegisteredOscillatorWaveforms { get; private set; }
        = new string[0];

    /// <summary>
    /// The effect type names <see cref="Register()" /> offered the engine, spelled as a preset
    /// spells them - empty until <see cref="Register()" /> has run.
    /// </summary>
    /// <remarks>
    /// These are the CREATIVE effects only. The mixing and room effects - filters, gain, reverb,
    /// delay, chorus, convolution and the compressor - belong to CodeBrix.Audio itself and are
    /// registered by it whether this package is present or not.
    /// </remarks>
    public static IReadOnlyList<string> RegisteredEffectTypes { get; private set; }
        = new string[0];

    private static void RegisterOscillators(DecentSamplerExtensionRegistry registry)
    {
        IReadOnlyList<ModestOscillatorRegistration> entries = ModestSynthRegistry.OscillatorRegistrations;

        for (int i = 0; i < entries.Count; i++)
        {
            ModestWaveform waveform = entries[i].Waveform;
            registry.RegisterOscillator(
                entries[i].WaveformName, context => new ModestVoiceSource(context, waveform));
        }
    }

    private static void RegisterEffects(DecentSamplerExtensionRegistry registry)
    {
        IReadOnlyList<ModestEffectRegistration> entries = ModestSynthRegistry.EffectRegistrations;

        for (int i = 0; i < entries.Count; i++)
        {
            registry.RegisterEffect(entries[i].TypeName, entries[i].Factory);
        }
    }

    private static string[] BuildRegisteredEffectTypes()
    {
        IReadOnlyList<ModestEffectRegistration> entries = ModestSynthRegistry.EffectRegistrations;
        string[] names = new string[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            names[i] = entries[i].TypeName;
        }

        return names;
    }

    private static string[] BuildRegisteredWaveforms()
    {
        IReadOnlyList<ModestOscillatorRegistration> entries = ModestSynthRegistry.OscillatorRegistrations;
        string[] names = new string[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            names[i] = entries[i].WaveformName;
        }

        return names;
    }
}

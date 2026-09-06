using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Effects.Internal;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// Creates a creative effect from its type name, and says which type names this package covers.
/// </summary>
/// <remarks>
/// A freshly created effect carries the developer guide's documented defaults and has not been
/// prepared; give it a sample rate before the first block. Nothing here reads a Decent Sampler
/// preset - that is what <c>ModestSynth.Register()</c> arranges - so this factory is equally useful
/// in an application that has never seen a <c>.dspreset</c> file.
/// </remarks>
public static class ModestEffectFactory
{
    /// <summary>Every effect type this package can build, in the order the guide documents them.</summary>
    public static IReadOnlyList<string> SupportedTypes => ModestEffectTypes.All;

    /// <summary>
    /// Whether <see cref="Create" /> can build this effect type.
    /// </summary>
    /// <param name="type">The type name as a preset spells it, for example <c>"bit_crusher"</c>.</param>
    /// <returns><see langword="true" /> when this package supplies the effect.</returns>
    public static bool IsSupported(string type) => Normalize(type) != null;

    /// <summary>
    /// Creates an effect carrying its documented defaults.
    /// </summary>
    /// <param name="type">The type name as a preset spells it.</param>
    /// <returns>The effect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type" /> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The core package, not this one, supplies that effect type - or nothing does.
    /// </exception>
    public static IInstrumentEffect Create(string type)
    {
        if (type == null) { throw new ArgumentNullException(nameof(type)); }

        IInstrumentEffect effect;
        if (TryCreate(type, out effect)) { return effect; }

        throw new NotSupportedException(
            "CodeBrix.Audio.ModestSynth does not supply an effect of type '" + type +
            "'. The mixing and room effects - filters, gain, reverb, delay, chorus, convolution and " +
            "the compressor - belong to CodeBrix.Audio itself.");
    }

    /// <summary>
    /// Creates an effect when the type name is one this package supplies.
    /// </summary>
    /// <param name="type">The type name as a preset spells it. Matching ignores case and punctuation.</param>
    /// <param name="effect">The new effect, or null when the name is not one of this package's.</param>
    /// <returns><see langword="true" /> when an effect was created.</returns>
    public static bool TryCreate(string type, out IInstrumentEffect effect)
    {
        effect = null;

        string name = Normalize(type);
        if (name == null) { return false; }

        switch (name)
        {
            case ModestEffectTypes.Phaser: effect = new PhaserEffect(); return true;
            case ModestEffectTypes.PitchShift: effect = new PitchShiftEffect(); return true;
            case ModestEffectTypes.WaveFolder: effect = new WaveFolderEffect(); return true;
            case ModestEffectTypes.WaveShaper: effect = new WaveShaperEffect(); return true;
            case ModestEffectTypes.StereoSimulator: effect = new StereoSimulatorEffect(); return true;
            case ModestEffectTypes.BitCrusher: effect = new BitCrusherEffect(); return true;
            case ModestEffectTypes.Gate: effect = new GateEffect(); return true;
            default: return false;
        }
    }

    /// <summary>
    /// The canonical spelling of an effect type name, or null when this package does not supply it.
    /// </summary>
    /// <param name="type">The name as written.</param>
    /// <returns>One of the <see cref="ModestEffectTypes" /> constants, or null.</returns>
    private static string Normalize(string type)
    {
        string folded = ModestEffectParameters.Fold(type);
        if (folded == null) { return null; }

        switch (folded)
        {
            case "phaser": return ModestEffectTypes.Phaser;
            case "pitchshift": return ModestEffectTypes.PitchShift;
            case "wavefolder": return ModestEffectTypes.WaveFolder;
            case "waveshaper": return ModestEffectTypes.WaveShaper;
            case "stereosimulator": return ModestEffectTypes.StereoSimulator;
            case "bitcrusher": return ModestEffectTypes.BitCrusher;
            case "gate": return ModestEffectTypes.Gate;
            default: return null;
        }
    }
}

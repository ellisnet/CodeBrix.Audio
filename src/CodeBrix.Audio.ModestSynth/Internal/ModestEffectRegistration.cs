using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// One entry in the list of creative effects this package hands to the Decent Sampler engine: the
/// type name a preset writes, and the factory that builds the effect for it.
/// </summary>
internal sealed class ModestEffectRegistration
{
    /// <summary>Creates a registration.</summary>
    /// <param name="typeName">The type name a preset writes, for example <c>bit_crusher</c>.</param>
    /// <param name="factory">The factory that builds one configured effect per chain position.</param>
    internal ModestEffectRegistration(string typeName, Func<EffectContext, IInstrumentEffect> factory)
    {
        TypeName = typeName;
        Factory = factory;
    }

    /// <summary>The effect type name a preset spells, for example <c>"stereo_simulator"</c>.</summary>
    internal string TypeName { get; }

    /// <summary>Builds one configured effect from a chain position.</summary>
    internal Func<EffectContext, IInstrumentEffect> Factory { get; }
}

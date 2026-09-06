using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;effects&gt;</c> element: one effect chain, at instrument, group or bus level.
/// </summary>
/// <remarks>
/// The level decides how the chain is used. An instrument chain processes the whole mix once; a bus
/// chain processes that bus; a group chain is a template the engine instantiates per voice, which is
/// why the guide limits it to filters, gain and chorus - a reverb or delay would be destroyed before
/// its tail finished.
/// </remarks>
public sealed class DecentSamplerEffectsElement : DecentSamplerElement
{
    private readonly List<DecentSamplerEffect> _effects = [];

    /// <summary>The effects in the chain, in document order. The first is effect 0 for bindings.</summary>
    public IReadOnlyList<DecentSamplerEffect> Effects => _effects;

    internal void Add(DecentSamplerEffect effect)
    {
        effect.Index = _effects.Count;
        _effects.Add(effect);
        AddChild(effect);
    }
}

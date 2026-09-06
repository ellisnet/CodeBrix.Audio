using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;modulators&gt;</c> element: every modulation source in the instrument, in the order a
/// binding's <c>modulatorIndex</c> counts them.
/// </summary>
public sealed class DecentSamplerModulatorsElement : DecentSamplerElement
{
    private readonly List<DecentSamplerModulator> _modulators = [];

    /// <summary>The modulators, in document order. The first is modulator 0 for bindings.</summary>
    public IReadOnlyList<DecentSamplerModulator> Modulators => _modulators;

    internal void Add(DecentSamplerModulator modulator)
    {
        modulator.Index = _modulators.Count;
        _modulators.Add(modulator);
        AddChild(modulator);
    }
}

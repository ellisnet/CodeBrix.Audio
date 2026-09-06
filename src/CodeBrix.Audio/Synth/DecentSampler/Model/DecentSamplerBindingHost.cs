using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An element that owns <c>&lt;binding&gt;</c> children: a control, a button or menu state, a MIDI
/// handler, a modulator, or one axis of an XY pad.
/// </summary>
public abstract class DecentSamplerBindingHost : DecentSamplerElement
{
    private readonly List<DecentSamplerBinding> _bindings = [];

    /// <summary>The bindings beneath this element, in document order.</summary>
    public IReadOnlyList<DecentSamplerBinding> Bindings => _bindings;

    internal void Add(DecentSamplerBinding binding)
    {
        _bindings.Add(binding);
        AddChild(binding);
    }
}

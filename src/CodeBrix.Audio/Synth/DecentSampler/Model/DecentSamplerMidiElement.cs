using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;midi&gt;</c> element: the controller, note and velocity handlers an instrument responds
/// to, each holding its own bindings.
/// </summary>
/// <remarks>
/// A binding that targets a handler addresses it by <c>midiElementIndex</c>, which counts every
/// handler - <c>&lt;cc&gt;</c>, <c>&lt;note&gt;</c> and <c>&lt;velocity&gt;</c> alike - in document
/// order. <see cref="Handlers"/> is that list; the three typed lists are views onto it.
/// </remarks>
public sealed class DecentSamplerMidiElement : DecentSamplerElement
{
    private readonly List<DecentSamplerMidiHandler> _handlers = [];
    private readonly List<DecentSamplerMidiCc> _ccHandlers = [];
    private readonly List<DecentSamplerMidiNote> _noteHandlers = [];
    private readonly List<DecentSamplerMidiVelocity> _velocityHandlers = [];

    /// <summary>Every handler, in document order. Its position is the binding's <c>midiElementIndex</c>.</summary>
    public IReadOnlyList<DecentSamplerMidiHandler> Handlers => _handlers;

    /// <summary>The <c>&lt;cc&gt;</c> handlers, in document order.</summary>
    public IReadOnlyList<DecentSamplerMidiCc> CcHandlers => _ccHandlers;

    /// <summary>The <c>&lt;note&gt;</c> handlers, in document order.</summary>
    public IReadOnlyList<DecentSamplerMidiNote> NoteHandlers => _noteHandlers;

    /// <summary>The <c>&lt;velocity&gt;</c> handlers, in document order.</summary>
    public IReadOnlyList<DecentSamplerMidiVelocity> VelocityHandlers => _velocityHandlers;

    internal void Add(DecentSamplerMidiHandler handler)
    {
        handler.Index = _handlers.Count;
        _handlers.Add(handler);
        AddChild(handler);

        switch (handler)
        {
            case DecentSamplerMidiCc cc:
                _ccHandlers.Add(cc);
                break;
            case DecentSamplerMidiNote note:
                _noteHandlers.Add(note);
                break;
            case DecentSamplerMidiVelocity velocity:
                _velocityHandlers.Add(velocity);
                break;
        }
    }
}

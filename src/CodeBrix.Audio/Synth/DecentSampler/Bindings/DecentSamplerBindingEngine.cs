using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Sequencing;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The parameter and binding engine: it builds the live control surface, finds what every binding
/// addresses, fires the initial state at load, and turns later control moves and MIDI messages into
/// parameter changes.
/// </summary>
/// <remarks>
/// <para>
/// The initial state is the part that matters most. A Pianobook preset writes its balance into its
/// knobs - a dry knob at 1, a pad knob at 0.35, an attack knob at 1.6 seconds - and expects those
/// values to have been applied before the first note. The engine therefore fires every control's
/// bindings once at load, in document order, honouring <c>triggerOnLoad="false"</c>.
/// </para>
/// <para>
/// Writing a control's <c>VALUE</c> fires that control's own bindings, so one control can be driven
/// from a knob, a MIDI controller and a key switch at once. That can be made circular by a preset, so
/// firing is depth-limited; the limit is generous and no real preset comes near it.
/// </para>
/// </remarks>
internal sealed class DecentSamplerBindingEngine
{
    private const int MaximumDepth = 8;

    private readonly DecentSamplerInstrument _instrument;
    private readonly List<DecentSamplerControl> _controls = [];
    private readonly List<DecentSamplerTagState> _tags = [];
    private readonly Dictionary<string, DecentSamplerTagState> _tagsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DecentSamplerBinding, DecentSamplerBindingRoute> _routes = [];
    private readonly List<string> _problems = [];
    private readonly int[] _controllerValues = new int[128];
    private readonly DecentSamplerBindingResolver _resolver;

    // Note-sequence triggers that fired before a synthesizer attached its sequencer - the initial
    // state of a button that starts a sequence, for instance. They are replayed in order the moment
    // one does, so a preset whose sequence button defaults to "on" still starts playing.
    private readonly List<DeferredTrigger> _deferredTriggers = [];

    private DecentSamplerSequenceTriggerHandler _sequenceTrigger;
    private DecentSamplerSequenceTrigger _trigger = DecentSamplerSequenceTrigger.Control;
    private int _depth;
    private int _version;

    internal DecentSamplerBindingEngine(DecentSamplerInstrument instrument)
    {
        _instrument = instrument;
        Factory = new DecentSamplerParameterFactory(this);
        _resolver = new DecentSamplerBindingResolver(this);
        BackgroundImage = instrument.Ui?.BgImage;
    }

    /// <summary>Raised after a binding has changed a parameter.</summary>
    internal event EventHandler<DecentSamplerParameterChangedEventArgs> ParameterChanged;

    /// <summary>Raised when an <c>ALL_NOTES_OFF</c> binding fires.</summary>
    internal event EventHandler AllNotesOff;

    /// <summary>The instrument the engine belongs to.</summary>
    internal DecentSamplerInstrument Instrument => _instrument;

    /// <summary>The target factory.</summary>
    internal DecentSamplerParameterFactory Factory { get; }

    /// <summary>
    /// The resolver, which turns a binding into the targets it addresses. Exposed so that a caller can
    /// ask what an arbitrary binding would reach without adding it to the preset.
    /// </summary>
    internal DecentSamplerBindingResolver Resolver => _resolver;

    /// <summary>The live controls, in the order a binding's index counts them.</summary>
    internal IReadOnlyList<DecentSamplerControl> Controls => _controls;

    /// <summary>The live tag states, one for every tag named anywhere in the preset.</summary>
    internal IReadOnlyList<DecentSamplerTagState> TagStates => _tags;

    /// <summary>Everything that could not be resolved, one line each.</summary>
    internal IReadOnlyList<string> Problems => _problems;

    /// <summary>The live background image path, which a <c>BG_IMAGE</c> binding changes.</summary>
    internal string BackgroundImage { get; set; }

    /// <summary>
    /// Where a <c>&lt;binding type="note_sequence"&gt;</c> that names no parameter is dispatched. The
    /// note sequencer sets this; until something does, such a binding is remembered and replayed when
    /// one arrives.
    /// </summary>
    internal DecentSamplerSequenceTriggerHandler SequenceTrigger
    {
        get => _sequenceTrigger;

        set
        {
            _sequenceTrigger = value;

            if (value == null || _deferredTriggers.Count == 0)
            {
                return;
            }

            var deferred = _deferredTriggers.ToArray();
            _deferredTriggers.Clear();

            foreach (var entry in deferred)
            {
                value(entry.Binding, entry.Trigger);
            }
        }
    }

    /// <summary>
    /// Whether a binding is the ACTION form of a note-sequence binding - the one that starts or stops
    /// a sequence rather than changing a parameter of it. Appendix B gives the type exactly one
    /// parameter, <c>RATE</c>; every example that triggers a sequence writes no parameter at all.
    /// </summary>
    /// <param name="binding">The binding.</param>
    /// <returns><see langword="true"/> when the binding is a trigger.</returns>
    internal static bool IsSequenceTrigger(DecentSamplerBinding binding) =>
        binding != null &&
        binding.BindingType == DecentSamplerBindingType.NoteSequence &&
        string.IsNullOrWhiteSpace(binding.Parameter);

    /// <summary>Counts every parameter change, so a render loop can notice one without an event.</summary>
    internal int Version => Volatile.Read(ref _version);

    /// <summary>Builds the control surface and the tag states, then resolves every binding.</summary>
    internal void Build()
    {
        BuildControls();
        BuildTags();
        ResolveAll();
    }

    /// <summary>
    /// Fires every control's bindings with the value the preset was written with, which is what makes
    /// an instrument play at its intended balance before anything is touched.
    /// </summary>
    internal void ApplyInitialState()
    {
        foreach (var control in _controls)
        {
            FireControlBindings(control, atLoad: true);
        }
    }

    /// <summary>The live state of one tag, created on first use so that undeclared tags still work.</summary>
    /// <param name="name">The tag's name.</param>
    /// <returns>The state, or null when the name is empty.</returns>
    internal DecentSamplerTagState TagState(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        if (_tagsByName.TryGetValue(trimmed, out var existing))
        {
            return existing;
        }

        var state = new DecentSamplerTagState(trimmed, null);
        _tagsByName[trimmed] = state;
        _tags.Add(state);
        return state;
    }

    /// <summary>
    /// The arpeggiator, created with its documented defaults when a binding needs one and the preset
    /// declares none. A preset that drives the arpeggiator entirely from its interface is otherwise
    /// unreachable.
    /// </summary>
    /// <returns>The arpeggiator.</returns>
    internal DecentSamplerArpeggiator Arpeggiator()
    {
        _instrument.Preset.Arpeggiator ??= new DecentSamplerArpeggiator { ElementName = "arpeggiator" };
        return _instrument.Preset.Arpeggiator;
    }

    /// <summary>What a binding resolved to, for tests and for the modulator runtime.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The route, or null when the binding resolved to nothing.</returns>
    internal DecentSamplerBindingRoute RouteFor(DecentSamplerBinding binding) =>
        binding != null && _routes.TryGetValue(binding, out var route) ? route : null;

    /// <summary>Sets a control's value and fires its bindings.</summary>
    /// <param name="control">The control.</param>
    /// <param name="value">The new value.</param>
    internal void SetControlValue(DecentSamplerControl control, double value)
    {
        var target = Factory.ForControl(control, "VALUE");
        target.SetBaseValue(DecentSamplerParameterValue.FromNumber(value));
    }

    /// <summary>Sets an X-Y pad's horizontal value and fires its x-axis bindings.</summary>
    /// <param name="control">The pad.</param>
    /// <param name="value">The new value, 0 to 1.</param>
    internal void SetControlXValue(DecentSamplerControl control, double value) =>
        Factory.ForControl(control, "X_VALUE").SetBaseValue(DecentSamplerParameterValue.FromNumber(value));

    /// <summary>Sets an X-Y pad's vertical value and fires its y-axis bindings.</summary>
    /// <param name="control">The pad.</param>
    /// <param name="value">The new value, 0 to 1.</param>
    internal void SetControlYValue(DecentSamplerControl control, double value) =>
        Factory.ForControl(control, "Y_VALUE").SetBaseValue(DecentSamplerParameterValue.FromNumber(value));

    /// <summary>Fires a control's own bindings and those of whichever state or option is selected.</summary>
    /// <param name="control">The control.</param>
    internal void FireControlBindings(DecentSamplerControl control) =>
        FireControlBindings(control, atLoad: false);

    /// <summary>Fires one axis of an X-Y pad.</summary>
    /// <param name="control">The pad.</param>
    /// <param name="horizontal">Whether to fire the x axis rather than the y axis.</param>
    internal void FireControlAxis(DecentSamplerControl control, bool horizontal) =>
        FireControlAxis(control, horizontal, atLoad: false);

    /// <summary>
    /// Handles an incoming continuous-controller message, firing every <c>&lt;cc&gt;</c> handler that
    /// listens on that controller.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15. Handlers are not channel-specific.</param>
    /// <param name="controller">The controller number, 0 to 127.</param>
    /// <param name="value">The controller value, 0 to 127.</param>
    /// <returns><see langword="true"/> when the message changed anything.</returns>
    /// <remarks>
    /// MEASURED against the reference player: a <c>&lt;cc&gt;</c> binding fires on a CHANGE of the
    /// controller's value, not on every message, and every controller's stored value starts at 0 - so a
    /// preset that sends 0 first sees nothing happen.
    /// </remarks>
    internal bool ProcessControlChange(int channel, int controller, int value)
    {
        _ = channel;

        if (controller < 0 || controller > 127)
        {
            return false;
        }

        var clamped = value < 0 ? 0 : value > 127 ? 127 : value;

        if (_controllerValues[controller] == clamped)
        {
            return false;
        }

        _controllerValues[controller] = clamped;
        var fired = false;

        foreach (var handler in _instrument.MidiHandlers)
        {
            if (handler is not DecentSamplerMidiCc cc || cc.Number != controller)
            {
                continue;
            }

            Fire(cc.Bindings, DecentSamplerBindingInput.Controller(clamped), atLoad: false);
            fired = true;
        }

        return fired;
    }

    /// <summary>
    /// Handles an incoming note-on, firing every matching <c>&lt;note&gt;</c> handler and every
    /// <c>&lt;velocity&gt;</c> handler.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="note">The note number, 0 to 127.</param>
    /// <param name="velocity">The velocity, 1 to 127.</param>
    /// <returns>
    /// <see langword="true"/> when a handler asked to swallow the note, meaning the sampler must not
    /// play it.
    /// </returns>
    internal bool ProcessNoteOn(int channel, int note, int velocity)
    {
        var swallow = false;
        var input = new DecentSamplerBindingInput(velocity, 0.0, 127.0);
        var previous = _trigger;
        _trigger = DecentSamplerSequenceTrigger.NoteOn(channel, note, velocity);

        try
        {
            foreach (var handler in _instrument.MidiHandlers)
            {
                switch (handler)
                {
                    case DecentSamplerMidiNote listener when Listens(listener, note, noteOn: true):
                        Fire(listener.Bindings, input, atLoad: false);
                        swallow |= listener.SwallowNotes ?? false;
                        break;

                    case DecentSamplerMidiVelocity velocityHandler:
                        Fire(velocityHandler.Bindings, input, atLoad: false);
                        break;
                }
            }
        }
        finally
        {
            _trigger = previous;
        }

        return swallow;
    }

    /// <summary>Handles an incoming note-off, firing every matching <c>&lt;note&gt;</c> handler.</summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="note">The note number, 0 to 127.</param>
    /// <param name="velocity">The release velocity, 0 to 127.</param>
    /// <returns><see langword="true"/> when a handler asked to swallow the note.</returns>
    internal bool ProcessNoteOff(int channel, int note, int velocity)
    {
        var swallow = false;
        var input = new DecentSamplerBindingInput(velocity, 0.0, 127.0);
        var previous = _trigger;
        _trigger = DecentSamplerSequenceTrigger.NoteOff(channel, note);

        try
        {
            foreach (var handler in _instrument.MidiHandlers)
            {
                if (handler is not DecentSamplerMidiNote listener ||
                    !Listens(listener, note, noteOn: false))
                {
                    continue;
                }

                Fire(listener.Bindings, input, atLoad: false);
                swallow |= listener.SwallowNotes ?? false;
            }
        }
        finally
        {
            _trigger = previous;
        }

        return swallow;
    }

    /// <summary>Applies a modulator's current output through one of its bindings.</summary>
    /// <param name="binding">The modulator's binding.</param>
    /// <param name="sourceId">Which modulator this is, so its contribution can be replaced.</param>
    /// <param name="voiceId">The voice, or -1 for a global-scope modulator.</param>
    /// <param name="value">The modulator's output, normally 0 to 1.</param>
    internal void ApplyModulation(DecentSamplerBinding binding, int sourceId, int voiceId, double value) =>
        ApplyModulation(
            binding, sourceId, voiceId, DecentSamplerBindingInput.Normalised(value),
            RouteFor(binding)?.Amount ?? 1.0, restingRawOutput: 0.0);

    /// <summary>
    /// Applies a modulator's current output with an explicit source range and depth.
    /// </summary>
    /// <param name="binding">The modulator's binding.</param>
    /// <param name="sourceId">Which modulator this is, so its contribution can be replaced.</param>
    /// <param name="voiceId">The voice, or -1 for a global-scope modulator.</param>
    /// <param name="input">The modulator's output and the range it moves in.</param>
    /// <param name="amount">
    /// The depth to apply, read live so that a <c>MOD_AMOUNT</c> binding on the modulator itself
    /// takes effect at once. The route's own depth is a snapshot of the value the preset was written
    /// with and goes stale the moment a knob moves it.
    /// </param>
    /// <param name="restingRawOutput">
    /// The raw value this modulator produces at rest, which the <c>modulate</c> behaviour subtracts
    /// after translation. Zero for a <c>&lt;midiCC&gt;</c>, 0.5 for an <c>&lt;lfo&gt;</c>.
    /// </param>
    internal void ApplyModulation(
        DecentSamplerBinding binding,
        int sourceId,
        int voiceId,
        DecentSamplerBindingInput input,
        double amount,
        double restingRawOutput)
    {
        var route = RouteFor(binding);

        if (route == null || !route.IsTemporary)
        {
            return;
        }

        var translated = DecentSamplerTranslator.Translate(binding, input).AsNumber;
        var neutral = DecentSamplerTranslator.NeutralOutput(binding, input, restingRawOutput);
        var contribution = new DecentSamplerModulationContribution(
            route.Behavior, amount, translated, neutral);

        for (var index = 0; index < route.Targets.Count; index++)
        {
            route.Targets[index].SetModulation(sourceId, voiceId, contribution);
        }
    }

    /// <summary>Removes a modulator's contribution from everything one of its bindings reaches.</summary>
    /// <param name="binding">The modulator's binding.</param>
    /// <param name="sourceId">Which modulator this is.</param>
    /// <param name="voiceId">The voice, or -1 for a global-scope modulator.</param>
    internal void ClearModulation(DecentSamplerBinding binding, int sourceId, int voiceId)
    {
        var route = RouteFor(binding);

        if (route == null)
        {
            return;
        }

        foreach (var target in route.Targets)
        {
            target.ClearModulation(sourceId, voiceId);
        }
    }

    /// <summary>Removes every contribution belonging to one voice, from every target.</summary>
    /// <param name="voiceId">The voice that has finished.</param>
    internal void ClearVoiceModulation(int voiceId)
    {
        foreach (var target in Factory.Targets)
        {
            target.ClearVoice(voiceId);
        }
    }

    /// <summary>Raises the all-notes-off request.</summary>
    internal void RequestAllNotesOff() => AllNotesOff?.Invoke(this, EventArgs.Empty);

    private struct DeferredTrigger
    {
        public DecentSamplerBinding Binding;
        public DecentSamplerSequenceTrigger Trigger;
    }

    internal void OnParameterChanged(DecentSamplerParameter parameter, DecentSamplerParameterValue value)
    {
        Interlocked.Increment(ref _version);

        ParameterChanged?.Invoke(
            this,
            new DecentSamplerParameterChangedEventArgs(
                parameter.Name, parameter.Parameter, value.AsNumber, value.AsText));
    }

    private void DispatchSequenceTrigger(DecentSamplerBinding binding)
    {
        if (_sequenceTrigger != null)
        {
            _sequenceTrigger(binding, _trigger);
            return;
        }

        _deferredTriggers.Add(new DeferredTrigger { Binding = binding, Trigger = _trigger });
    }

    private static bool Listens(DecentSamplerMidiNote listener, int note, bool noteOn)
    {
        if (listener.Enabled == false || listener.LowNote == null)
        {
            return false;
        }

        var low = listener.LowNote.Value;
        var high = listener.HighNote ?? low;

        if (note < low || note > high)
        {
            return false;
        }

        return (listener.EventType ?? DecentSamplerMidiEventType.Any) switch
        {
            DecentSamplerMidiEventType.NoteOn => noteOn,
            DecentSamplerMidiEventType.NoteOff => !noteOn,
            _ => true,
        };
    }

    private void BuildControls()
    {
        var elements = _instrument.Ui?.Controls;

        if (elements == null)
        {
            return;
        }

        foreach (var element in elements)
        {
            _controls.Add(new DecentSamplerControl(this, element, _controls.Count));
        }
    }

    private void BuildTags()
    {
        foreach (var declared in _instrument.Tags)
        {
            if (string.IsNullOrWhiteSpace(declared.Name) || _tagsByName.ContainsKey(declared.Name))
            {
                continue;
            }

            var state = new DecentSamplerTagState(declared.Name.Trim(), declared);
            _tagsByName[state.Name] = state;
            _tags.Add(state);
        }

        foreach (var group in _instrument.Groups)
        {
            foreach (var tag in group.Tags)
            {
                TagState(tag);
            }
        }

        foreach (var zone in _instrument.Zones)
        {
            foreach (var tag in zone.Tags)
            {
                TagState(tag);
            }

            foreach (var tag in zone.SilencedByTags)
            {
                TagState(tag);
            }
        }
    }

    private void ResolveAll()
    {
        foreach (var host in Hosts())
        {
            foreach (var binding in host.Bindings)
            {
                Resolve(binding, host as DecentSamplerModulator);
            }
        }
    }

    private IEnumerable<DecentSamplerBindingHost> Hosts()
    {
        var ui = _instrument.Ui;

        if (ui != null)
        {
            foreach (var element in ui.Controls)
            {
                yield return element;

                switch (element)
                {
                    case DecentSamplerUiControl control:
                        foreach (var state in control.States)
                        {
                            yield return state;
                        }

                        break;

                    case DecentSamplerUiButton button:
                        foreach (var state in button.States)
                        {
                            yield return state;
                        }

                        break;

                    case DecentSamplerUiMenu menu:
                        foreach (var option in menu.Options)
                        {
                            yield return option;
                        }

                        break;

                    case DecentSamplerUiXyPad pad:
                        if (pad.XAxis != null)
                        {
                            yield return pad.XAxis;
                        }

                        if (pad.YAxis != null)
                        {
                            yield return pad.YAxis;
                        }

                        break;
                }
            }
        }

        foreach (var handler in _instrument.MidiHandlers)
        {
            yield return handler;
        }

        foreach (var modulator in _instrument.Modulators)
        {
            yield return modulator;
        }
    }

    private void Resolve(DecentSamplerBinding binding, DecentSamplerModulator modulator)
    {
        if (_routes.ContainsKey(binding))
        {
            return;
        }

        var targets = _resolver.Resolve(binding, out var problem);

        if (problem != null)
        {
            _problems.Add(problem);
        }

        if (targets.Count == 0)
        {
            return;
        }

        var behavior = binding.ModBehavior ?? modulator?.ModBehavior ?? DecentSamplerModBehavior.Set;
        var amount = binding.ModAmount ?? modulator?.ModAmount ?? 1.0;
        var voiceScope = modulator?.Scope == DecentSamplerModulatorScope.Voice;

        var route = new DecentSamplerBindingRoute(
            binding, targets, modulator != null, behavior, amount, voiceScope);

        // MEASURED (round 2, item 23, finding 4): modAmount on a PERMANENT binding does NOTHING. A
        // <velocity> binding to a filter cutoff moves it identically at modAmount="1.0" and
        // modAmount="0.3", so the attribute is read only by a modulator's own bindings. The blend
        // machinery (DecentSamplerBindingRoute.Anchors) stays in place unused, because the guide
        // still describes the attribute as a depth and a later version of the player may honour it.

        _routes[binding] = route;
    }

    private void FireControlBindings(DecentSamplerControl control, bool atLoad)
    {
        if (_depth >= MaximumDepth)
        {
            return;
        }

        var source = control.Source;

        if (control.Kind == DecentSamplerControlKind.XyPad)
        {
            FireControlAxis(control, horizontal: true, atLoad);
            FireControlAxis(control, horizontal: false, atLoad);
            return;
        }

        var input = new DecentSamplerBindingInput(control.Value, control.MinValue, control.MaxValue);
        Fire(source.Bindings, input, atLoad);

        switch (source)
        {
            case DecentSamplerUiControl knob when knob.States.Count > 0:
                FireState(knob.States, control.SelectedIndex, atLoad);
                break;

            case DecentSamplerUiButton button when button.States.Count > 0:
                FireState(button.States, control.SelectedIndex, atLoad);
                break;

            case DecentSamplerUiMenu menu when menu.Options.Count > 0:
                var selected = control.SelectedIndex;

                if (selected >= 0 && selected < menu.Options.Count)
                {
                    Fire(
                        menu.Options[selected].Bindings,
                        new DecentSamplerBindingInput(selected + 1, 1.0, menu.Options.Count),
                        atLoad);
                }

                break;
        }
    }

    private void FireState(IReadOnlyList<DecentSamplerUiState> states, int selected, bool atLoad)
    {
        if (selected < 0 || selected >= states.Count)
        {
            return;
        }

        Fire(
            states[selected].Bindings,
            new DecentSamplerBindingInput(selected, 0.0, Math.Max(1, states.Count - 1)),
            atLoad);
    }

    private void FireControlAxis(DecentSamplerControl control, bool horizontal, bool atLoad)
    {
        if (control.Source is not DecentSamplerUiXyPad pad)
        {
            return;
        }

        var axis = horizontal ? pad.XAxis : pad.YAxis;

        if (axis == null)
        {
            return;
        }

        var value = horizontal ? control.XValue : control.YValue;
        Fire(axis.Bindings, DecentSamplerBindingInput.Normalised(value), atLoad);
    }

    private void Fire(
        IReadOnlyList<DecentSamplerBinding> bindings, DecentSamplerBindingInput input, bool atLoad)
    {
        if (bindings.Count == 0 || _depth >= MaximumDepth)
        {
            return;
        }

        _depth++;

        try
        {
            foreach (var binding in bindings)
            {
                if (binding.Enabled == false || (atLoad && binding.TriggerOnLoad == false))
                {
                    continue;
                }

                if (IsSequenceTrigger(binding))
                {
                    DispatchSequenceTrigger(binding);
                    continue;
                }

                var route = RouteFor(binding);

                if (route == null || route.IsTemporary)
                {
                    continue;
                }

                // MEASURED: modAmount is IGNORED on a permanent binding. The reference gave a
                // <velocity> binding at modAmount="1.0" and at "0.3" identical cutoffs at every
                // velocity, so the translated value is written straight through.
                var value = DecentSamplerTranslator.Translate(binding, input);

                for (var index = 0; index < route.Targets.Count; index++)
                {
                    route.Targets[index].SetBaseValue(value);
                }
            }
        }
        finally
        {
            _depth--;
        }
    }
}

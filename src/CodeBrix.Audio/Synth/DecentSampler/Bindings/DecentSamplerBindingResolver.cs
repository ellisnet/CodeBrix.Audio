using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// Finds what a <c>&lt;binding&gt;</c> addresses, honouring every index rule Appendix B documents.
/// </summary>
/// <remarks>
/// <para>
/// The rules, in one place:
/// </para>
/// <list type="bullet">
/// <item><description>A user-interface index counts EVERY element under every tab, labels and images
/// included.</description></item>
/// <item><description>An effect index is per chain: the instrument chain, one group's chain, or one
/// bus's chain, chosen by the binding's level. At group and bus level the group or bus comes from
/// <c>groupIndex</c>/<c>controlIndex</c>/<c>position</c> and the effect from
/// <c>effectIndex</c>.</description></item>
/// <item><description>A MIDI element index counts <c>&lt;cc&gt;</c>, <c>&lt;note&gt;</c> and
/// <c>&lt;velocity&gt;</c> handlers together, in document order. The attribute used to be called
/// <c>noteIndex</c>.</description></item>
/// <item><description>The typed index attributes and <c>position</c> mean the same thing; the typed one
/// wins when both are written.</description></item>
/// <item><description>A bare <c>tags</c> list is promoted into the typed list that suits the binding -
/// <c>controlTags</c> at the interface, <c>effectTags</c> for an effect, <c>modulatorTags</c> for a
/// modulator, and <c>groupTags</c>, <c>sampleTags</c> or <c>oscillatorTags</c> by level - when the typed
/// one is not written.</description></item>
/// <item><description>A tag-level binding names its tag in <c>identifier</c>.</description></item>
/// </list>
/// <para>
/// A binding that addresses nothing is a problem, never an exception: the instrument still loads and
/// everything else in it still works.
/// </para>
/// </remarks>
internal sealed class DecentSamplerBindingResolver
{
    private readonly DecentSamplerBindingEngine _engine;

    internal DecentSamplerBindingResolver(DecentSamplerBindingEngine engine) => _engine = engine;

    /// <summary>Finds everything a binding changes.</summary>
    /// <param name="binding">The binding.</param>
    /// <param name="problem">Why nothing was found, or null when something was.</param>
    /// <returns>The targets, empty when none were found.</returns>
    internal IReadOnlyList<IParameterTarget> Resolve(DecentSamplerBinding binding, out string problem)
    {
        problem = null;

        var parameter = binding.Parameter;

        if (string.IsNullOrWhiteSpace(parameter))
        {
            if (DecentSamplerBindingEngine.IsSequenceTrigger(binding))
            {
                // The ACTION form of a note-sequence binding: it starts or stops a sequence rather
                // than writing a parameter, so it has no target and is not a problem. Every example
                // in the guide that triggers a sequence is written this way.
                return [];
            }

            problem = Describe(binding) + " names no parameter.";
            return [];
        }

        var targets = new List<IParameterTarget>();

        switch (binding.BindingType)
        {
            case DecentSamplerBindingType.Control:
            case DecentSamplerBindingType.LabeledKnob:
                AddControls(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.KeyboardColor:
                AddKeyboardColors(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.ButtonStateBinding:
                AddStateBinding(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.Note:
                AddMidiHandler(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.NoteBinding:
            case DecentSamplerBindingType.CcBinding:
            case DecentSamplerBindingType.VelocityBinding:
                AddMidiHandlerBinding(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.Modulator:
                AddModulators(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.NoteSequence:
                AddSequence(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.Arpeggiator:
                AddArpeggiator(binding, parameter, targets);
                break;

            case DecentSamplerBindingType.Effect:
                AddEffects(binding, parameter, targets);
                break;

            default:
                AddByLevel(binding, parameter, targets);
                break;
        }

        if (targets.Count == 0)
        {
            problem = "Unresolved binding: " + Describe(binding) + " addresses nothing in this preset.";
        }

        return targets;
    }

    private static string Describe(DecentSamplerBinding binding) =>
        $"<binding type=\"{binding.TypeName}\" level=\"{binding.LevelName}\" " +
        $"parameter=\"{binding.Parameter}\"> on line " +
        binding.LineNumber.ToString(CultureInfo.InvariantCulture);

    private static bool Matches(IReadOnlyList<string> wanted, IReadOnlyList<string> carried)
    {
        for (var index = 0; index < wanted.Count; index++)
        {
            for (var other = 0; other < carried.Count; other++)
            {
                if (string.Equals(wanted[index], carried[other], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Promote(IReadOnlyList<string> typed, IReadOnlyList<string> generic) =>
        typed.Count > 0 ? typed : generic;

    private static void Add(List<IParameterTarget> targets, IParameterTarget target)
    {
        if (target != null)
        {
            targets.Add(target);
        }
    }

    private void AddByLevel(DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        switch (binding.Level)
        {
            case DecentSamplerBindingLevel.Ui:
                if (Promote(binding.ControlTags, binding.Tags).Count > 0 ||
                    binding.ControlIndex != null || binding.Position != null)
                {
                    AddControls(binding, parameter, targets);
                }

                Add(targets, _engine.Factory.ForUi(parameter));
                break;

            case DecentSamplerBindingLevel.Instrument:
                Add(targets, _engine.Factory.ForInstrument(parameter));
                break;

            case DecentSamplerBindingLevel.Group:
                foreach (var group in SelectGroups(binding))
                {
                    Add(targets, _engine.Factory.ForGroup(group, parameter));
                }

                break;

            case DecentSamplerBindingLevel.Sample:
            case DecentSamplerBindingLevel.Oscillator:
                foreach (var zone in SelectZones(binding))
                {
                    Add(targets, _engine.Factory.ForZone(zone, parameter));
                }

                break;

            case DecentSamplerBindingLevel.Tag:
                var tag = _engine.TagState(binding.Identifier);

                if (tag != null)
                {
                    Add(targets, _engine.Factory.ForTag(tag, parameter));
                }

                break;

            case DecentSamplerBindingLevel.Bus:
                foreach (var bus in SelectBuses(binding))
                {
                    Add(targets, _engine.Factory.ForBus(bus, parameter));
                }

                break;

            case DecentSamplerBindingLevel.Midi:
                AddMidiHandlerBinding(binding, parameter, targets);
                break;

            default:
                // No usable level: fall back to the instrument, which is where the ambiguous
                // parameters (AMP_VOLUME, ENV_ATTACK) belong anyway.
                Add(targets, _engine.Factory.ForInstrument(parameter));
                break;
        }
    }

    private IEnumerable<DecentSamplerGroup> SelectGroups(DecentSamplerBinding binding)
    {
        var tags = Promote(binding.GroupTags, binding.Tags);
        var groups = _engine.Instrument.Groups;

        if (tags.Count > 0)
        {
            foreach (var group in groups)
            {
                if (Matches(tags, group.Tags))
                {
                    yield return group;
                }
            }

            yield break;
        }

        var index = binding.GroupIndex ?? binding.Position ?? binding.ControlIndex;

        if (index != null && index >= 0 && index < groups.Count)
        {
            yield return groups[index.Value];
        }
    }

    private IEnumerable<DecentSamplerZone> SelectZones(DecentSamplerBinding binding)
    {
        var oscillators = binding.Level == DecentSamplerBindingLevel.Oscillator;
        var tags = Promote(
            oscillators ? binding.OscillatorTags : binding.SampleTags, binding.Tags);
        var wanted = oscillators ? DecentSamplerZoneKind.Oscillator : DecentSamplerZoneKind.Sample;
        var index = binding.GroupIndex ?? binding.ControlIndex;
        var groups = _engine.Instrument.Groups;

        foreach (var group in groups)
        {
            if (index != null && group.Index != index.Value)
            {
                continue;
            }

            foreach (var zone in group.Zones)
            {
                if (zone.Kind != wanted)
                {
                    continue;
                }

                if (tags.Count == 0 || Matches(tags, zone.Tags))
                {
                    yield return zone;
                }
            }
        }
    }

    private IEnumerable<DecentSamplerBus> SelectBuses(DecentSamplerBinding binding)
    {
        var buses = _engine.Instrument.Buses;
        var index = binding.BusIndex ?? binding.Position;

        if (index != null && index >= 0 && index < buses.Count)
        {
            yield return buses[index.Value];
        }
    }

    private IEnumerable<DecentSamplerControl> SelectControls(DecentSamplerBinding binding)
    {
        var tags = Promote(binding.ControlTags, binding.Tags);
        var controls = _engine.Controls;

        if (tags.Count > 0)
        {
            foreach (var control in controls)
            {
                if (Matches(tags, control.Tags))
                {
                    yield return control;
                }
            }

            yield break;
        }

        var index = binding.ControlIndex ?? binding.Position;

        if (index != null && index >= 0 && index < controls.Count)
        {
            yield return controls[index.Value];
        }
    }

    private void AddControls(DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        foreach (var control in SelectControls(binding))
        {
            Add(targets, _engine.Factory.ForControl(control, parameter));
        }
    }

    private void AddKeyboardColors(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var colors = _engine.Instrument.Ui?.Keyboard?.Colors;

        if (colors == null || colors.Count == 0)
        {
            return;
        }

        var index = binding.ColorIndex ?? binding.Position;

        if (index != null && index >= 0 && index < colors.Count)
        {
            Add(targets, _engine.Factory.ForKeyboardColor(colors[index.Value], parameter));
        }
    }

    private void AddStateBinding(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        foreach (var control in SelectControls(binding))
        {
            IReadOnlyList<DecentSamplerUiState> states = control.Source switch
            {
                DecentSamplerUiButton button => button.States,
                DecentSamplerUiControl knob => knob.States,
                _ => [],
            };

            var stateIndex = binding.StateIndex ?? 0;

            if (stateIndex < 0 || stateIndex >= states.Count)
            {
                continue;
            }

            var bindings = states[stateIndex].Bindings;
            var bindingIndex = binding.BindingIndex ?? 0;

            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                continue;
            }

            var owner = "control[" + control.Index.ToString(CultureInfo.InvariantCulture) + "].state[" +
                        stateIndex.ToString(CultureInfo.InvariantCulture) + "].";
            Add(targets, _engine.Factory.ForBinding(bindings[bindingIndex], owner, parameter));
        }
    }

    private DecentSamplerMidiHandler SelectMidiHandler(DecentSamplerBinding binding)
    {
        var handlers = _engine.Instrument.MidiHandlers;
        var index = binding.MidiElementIndex ?? binding.Position;

        return index != null && index >= 0 && index < handlers.Count ? handlers[index.Value] : null;
    }

    private void AddMidiHandler(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var handler = SelectMidiHandler(binding);

        if (handler != null)
        {
            Add(targets, _engine.Factory.ForMidiHandler(handler, parameter));
        }
    }

    private void AddMidiHandlerBinding(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var handler = SelectMidiHandler(binding);

        if (handler == null)
        {
            return;
        }

        var bindings = handler.Bindings;
        var index = binding.BindingIndex ?? 0;

        if (index < 0 || index >= bindings.Count)
        {
            return;
        }

        var owner = "midi[" + handler.Index.ToString(CultureInfo.InvariantCulture) + "].";
        Add(targets, _engine.Factory.ForBinding(bindings[index], owner, parameter));
    }

    private void AddModulators(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var modulators = _engine.Instrument.Modulators;
        var tags = Promote(binding.ModulatorTags, binding.Tags);

        if (tags.Count > 0)
        {
            foreach (var modulator in modulators)
            {
                if (Matches(tags, modulator.Tags))
                {
                    Add(targets, _engine.Factory.ForModulator(modulator, parameter));
                }
            }

            return;
        }

        var index = binding.ModulatorIndex ?? binding.Position;

        if (index != null && index >= 0 && index < modulators.Count)
        {
            Add(targets, _engine.Factory.ForModulator(modulators[index.Value], parameter));
        }
    }

    private void AddSequence(DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var sequences = _engine.Instrument.Sequences;
        var index = binding.SeqIndex ?? binding.Position;

        if (index != null && index >= 0 && index < sequences.Count)
        {
            Add(targets, _engine.Factory.ForSequence(sequences[index.Value], parameter));
        }
    }

    private void AddArpeggiator(
        DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var arpeggiator = _engine.Arpeggiator();

        if (arpeggiator != null)
        {
            Add(targets, _engine.Factory.ForArpeggiator(arpeggiator, parameter));
        }
    }

    private void AddEffects(DecentSamplerBinding binding, string parameter, List<IParameterTarget> targets)
    {
        var tags = Promote(binding.EffectTags, binding.Tags);

        if (tags.Count > 0)
        {
            foreach (var chain in AllChains())
            {
                foreach (var effect in chain.Effects.Effects)
                {
                    if (Matches(tags, effect.Tags))
                    {
                        Add(targets, _engine.Factory.ForEffect(effect, chain.Name, parameter));
                    }
                }
            }

            return;
        }

        var (name, element) = ChainFor(binding);

        if (element == null)
        {
            return;
        }

        var index = binding.EffectIndex ??
                    (binding.Level is DecentSamplerBindingLevel.Group or DecentSamplerBindingLevel.Bus
                        ? 0
                        : binding.Position ?? 0);

        if (index >= 0 && index < element.Effects.Count)
        {
            Add(targets, _engine.Factory.ForEffect(element.Effects[index], name, parameter));
        }
    }

    private (string Name, DecentSamplerEffectsElement Effects) ChainFor(DecentSamplerBinding binding)
    {
        switch (binding.Level)
        {
            case DecentSamplerBindingLevel.Group:
                foreach (var group in SelectGroups(binding))
                {
                    if (group.Effects != null)
                    {
                        return ("group[" + group.Index.ToString(CultureInfo.InvariantCulture) + "].",
                            group.Effects);
                    }
                }

                return (null, null);

            case DecentSamplerBindingLevel.Bus:
                foreach (var bus in SelectBuses(binding))
                {
                    if (bus.Effects != null)
                    {
                        return ("bus[" + bus.Index.ToString(CultureInfo.InvariantCulture) + "].",
                            bus.Effects);
                    }
                }

                return (null, null);

            default:
                return (string.Empty, _engine.Instrument.Effects);
        }
    }

    private IEnumerable<(string Name, DecentSamplerEffectsElement Effects)> AllChains()
    {
        if (_engine.Instrument.Effects != null)
        {
            yield return (string.Empty, _engine.Instrument.Effects);
        }

        foreach (var group in _engine.Instrument.Groups)
        {
            if (group.Effects != null)
            {
                yield return ("group[" + group.Index.ToString(CultureInfo.InvariantCulture) + "].",
                    group.Effects);
            }
        }

        foreach (var bus in _engine.Instrument.Buses)
        {
            if (bus.Effects != null)
            {
                yield return ("bus[" + bus.Index.ToString(CultureInfo.InvariantCulture) + "].", bus.Effects);
            }
        }
    }
}

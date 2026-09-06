using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The parameter targets of the things that DRIVE an instrument rather than sound in it: modulators,
/// note sequences, the arpeggiator, the MIDI handlers, and the bindings inside them.
/// </summary>
/// <remarks>
/// A binding can turn another binding off, and can rewrite the sequence attributes of a MIDI note
/// binding. Those targets write onto the parsed <see cref="DecentSamplerBinding"/>, which is the object
/// the sequencer phase reads, so a key switch that swaps sequences works the moment the sequencer
/// exists.
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    /// <summary>The target of a modulator parameter, or null when the parameter is not one.</summary>
    /// <param name="modulator">The modulator.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForModulator(DecentSamplerModulator modulator, string parameter)
    {
        var name = "modulator[" + Ordinal(modulator.Index) + "]." + parameter;
        var folded = Fold(parameter);

        if (folded is "modamount" or "amount")
        {
            return Number(
                modulator, name, parameter, modulator.ModAmount ?? 1.0,
                value => modulator.ModAmount = value);
        }

        switch (modulator)
        {
            case DecentSamplerLfoModulator lfo:
                switch (folded)
                {
                    case "frequency":
                    case "rate":
                        return Number(
                            lfo, name, parameter, lfo.Frequency ?? 1.0, value => lfo.Frequency = value);

                    case "shape":
                        return Choice<DecentSamplerLfoShape>(
                            lfo, name, parameter, lfo.Shape ?? DecentSamplerLfoShape.Sine,
                            value =>
                            {
                                lfo.Shape = value;
                                lfo.ShapeName = value.ToString();
                            });

                    case "moddelaytime":
                    case "delaytime":
                        return Number(
                            lfo, name, parameter, lfo.DelayTime ?? 0.0, value => lfo.DelayTime = value);

                    case "trigger":
                        return Choice<DecentSamplerModulatorTrigger>(
                            lfo, name, parameter, lfo.Trigger ?? DecentSamplerModulatorTrigger.None,
                            value => lfo.Trigger = value);
                }

                break;

            case DecentSamplerEnvelopeModulator envelope:
                switch (folded)
                {
                    case "envattack":
                        return Number(
                            envelope, name, parameter, envelope.Attack ?? 0.0,
                            value => envelope.Attack = value);

                    case "envattackcurve":
                        return Number(
                            envelope, name, parameter, envelope.AttackCurve ?? -100.0,
                            value => envelope.AttackCurve = value);

                    case "envdecay":
                        return Number(
                            envelope, name, parameter, envelope.Decay ?? 0.0, value => envelope.Decay = value);

                    case "envdecaycurve":
                        return Number(
                            envelope, name, parameter, envelope.DecayCurve ?? 100.0,
                            value => envelope.DecayCurve = value);

                    case "envsustain":
                        return Number(
                            envelope, name, parameter, envelope.Sustain ?? 1.0,
                            value => envelope.Sustain = value);

                    case "envrelease":
                        return Number(
                            envelope, name, parameter, envelope.Release ?? 0.0,
                            value => envelope.Release = value);

                    case "envreleasecurve":
                        return Number(
                            envelope, name, parameter, envelope.ReleaseCurve ?? 100.0,
                            value => envelope.ReleaseCurve = value);

                    case "moddelaytime":
                    case "delaytime":
                        return Number(
                            envelope, name, parameter, envelope.DelayTime ?? 0.0,
                            value => envelope.DelayTime = value);
                }

                break;

            case DecentSamplerRandomModulator random:
                switch (folded)
                {
                    case "frequency":
                    case "rate":
                        return Number(
                            random, name, parameter, random.Frequency ?? 1.0,
                            value => random.Frequency = value);

                    case "trigger":
                        return Choice<DecentSamplerModulatorTrigger>(
                            random, name, parameter, random.Trigger ?? DecentSamplerModulatorTrigger.None,
                            value => random.Trigger = value);
                }

                break;
        }

        return null;
    }

    /// <summary>The target of a note-sequence parameter, or null when the parameter is not one.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForSequence(DecentSamplerNoteSequence sequence, string parameter)
    {
        var name = "sequence[" + Ordinal(sequence.Index) + "]." + parameter;

        return Fold(parameter) switch
        {
            "rate" => Number(
                sequence, name, parameter, sequence.Rate ?? 1.0, value => sequence.Rate = value),
            "length" => Number(
                sequence, name, parameter, sequence.Length ?? 4.0, value => sequence.Length = value),
            _ => null,
        };
    }

    /// <summary>The target of an arpeggiator parameter, or null when the parameter is not one.</summary>
    /// <param name="arpeggiator">The arpeggiator.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForArpeggiator(
        DecentSamplerArpeggiator arpeggiator, string parameter)
    {
        var name = "arpeggiator." + parameter;

        switch (Fold(parameter))
        {
            case "arpenabled":
            case "enabled":
                return Switch(
                    arpeggiator, name, parameter, arpeggiator.Enabled ?? false,
                    value => arpeggiator.Enabled = value);

            case "arporder":
                return Choice<DecentSamplerArpOrder>(
                    arpeggiator, name, parameter, arpeggiator.Order ?? DecentSamplerArpOrder.Up,
                    value => arpeggiator.Order = value);

            case "arpoctaverange":
                return Integer(
                    arpeggiator, name, parameter, arpeggiator.OctaveRange ?? 1,
                    value => arpeggiator.OctaveRange = value);

            case "arpoctavemode":
                return Choice<DecentSamplerArpOctaveMode>(
                    arpeggiator, name, parameter,
                    arpeggiator.OctaveMode ?? DecentSamplerArpOctaveMode.ReplayPerOctave,
                    value => arpeggiator.OctaveMode = value);

            case "arpstepcount":
                return Integer(
                    arpeggiator, name, parameter, arpeggiator.StepCount ?? 16,
                    value => arpeggiator.StepCount = value);

            case "arpgatelength":
                return Number(
                    arpeggiator, name, parameter, arpeggiator.GateLength ?? 0.75,
                    value => arpeggiator.GateLength = value);

            case "arpsyncdivision":
                return Choice<DecentSamplerSyncDivision>(
                    arpeggiator, name, parameter,
                    arpeggiator.SyncDivision ?? DecentSamplerSyncDivision.NoteOneSixteenth,
                    value => arpeggiator.SyncDivision = value);

            case "arpfollowglobaltempo":
                return Switch(
                    arpeggiator, name, parameter, arpeggiator.FollowGlobalTempo ?? true,
                    value => arpeggiator.FollowGlobalTempo = value);

            case "arpratemultiplier":
                return Number(
                    arpeggiator, name, parameter, arpeggiator.RateMultiplier ?? 1.0,
                    value => arpeggiator.RateMultiplier = value);

            case "arpoverridebpm":
                return Number(
                    arpeggiator, name, parameter, arpeggiator.OverrideBpm ?? 120.0,
                    value => arpeggiator.OverrideBpm = value);

            default:
                return null;
        }
    }

    /// <summary>The target of a MIDI note handler's <c>ENABLED</c>, or null for anything else.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForMidiHandler(
        DecentSamplerMidiHandler handler, string parameter)
    {
        if (Fold(parameter) != "enabled" || handler is not DecentSamplerMidiNote note)
        {
            return null;
        }

        var name = "midi[" + Ordinal(handler.Index) + "]." + parameter;
        return Switch(note, name, parameter, note.Enabled ?? true, value => note.Enabled = value);
    }

    /// <summary>
    /// The target of a parameter on another binding: its <c>ENABLED</c> switch, or one of the
    /// sequence attributes a MIDI note binding carries.
    /// </summary>
    /// <param name="binding">The binding being changed.</param>
    /// <param name="owner">A name for the thing the binding lives in, used in messages.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForBinding(
        DecentSamplerBinding binding, string owner, string parameter)
    {
        var name = owner + "binding." + parameter;

        switch (Fold(parameter))
        {
            case "enabled":
                return Switch(
                    binding, name, parameter, binding.Enabled ?? true, value => binding.Enabled = value);

            case "seqindex":
                return Integer(
                    binding, name, parameter, binding.SeqIndex ?? 0, value => binding.SeqIndex = value);

            case "seqloopmode":
                return Choice<DecentSamplerSeqLoopMode>(
                    binding, name, parameter, binding.SeqLoopMode ?? DecentSamplerSeqLoopMode.Forward,
                    value => binding.SeqLoopMode = value);

            case "seqplaybackrate":
                return Number(
                    binding, name, parameter, binding.SeqPlaybackRate ?? 1.0,
                    value => binding.SeqPlaybackRate = value);

            case "seqtranspose":
                return Number(
                    binding, name, parameter, binding.SeqTranspose ?? 0.0,
                    value => binding.SeqTranspose = value);

            case "seqtransposewithrootnote":
                return Number(
                    binding, name, parameter, binding.SeqTransposeWithRootNote ?? 0.0,
                    value => binding.SeqTransposeWithRootNote = value);

            case "seqtrackmidiinputvelocity":
                return Number(
                    binding, name, parameter, binding.SeqTrackMidiInputVelocity ?? 1.0,
                    value => binding.SeqTrackMidiInputVelocity = value);

            default:
                return null;
        }
    }
}

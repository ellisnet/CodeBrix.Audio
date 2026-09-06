using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.Mpe;

namespace CodeBrix.Audio.Synth.DecentSampler.Modulation;

// The <modulators> runtime: every modulator the preset declares, ticked once per block for a
// global-scope one and once per voice per block for a voice-scope one, writing its output through
// the binding engine's temporary-modulation path.
//
// HOW A VOICE-SCOPE MODULATOR REACHES THE SOUND. A contribution is stored against the voice that
// made it, and the effective value it produces is not the one the group, zone or effect property
// holds - that one carries the GLOBAL contributions only. So the runtime publishes each voice's own
// effective values onto those properties immediately before the voice renders and puts the global
// ones back immediately after (PushVoice and PopVoice). Everything downstream - the voice's gain,
// pan and tuning, and any per-voice effect chain - therefore reads the right numbers with no lookup
// of its own, and a preset with no voice-scope modulators pays nothing at all.
//
// Nothing here allocates once the instrument is loaded: the per-instance state is two flat arrays
// of structs, sized for the synthesizer's polyphony.
internal sealed class DecentSamplerModulationRuntime
{
    private readonly DecentSamplerBindingEngine _engine;
    private readonly MpeChannelState _mpe;
    private readonly DecentSamplerModulationSource[] _sources;
    private readonly DecentSamplerModulationState[] _globalStates;
    private readonly DecentSamplerModulationState[] _voiceStates;
    private readonly DecentSamplerParameter[] _voiceTargets;
    private readonly bool[] _voiceStarted;
    private readonly int _polyphony;
    private readonly double _blockSeconds;

    private TempoSource _tempo;
    private int _pushedVoice = -1;

    internal DecentSamplerModulationRuntime(
        DecentSamplerInstrument instrument,
        MpeChannelState mpe,
        int sampleRate,
        int blockSize,
        int maximumPolyphony,
        int randomSeed,
        IList<string> problems)
    {
        _mpe = mpe;
        _polyphony = maximumPolyphony;
        _blockSeconds = blockSize / (double)sampleRate;

        var engine = instrument.BindingEngine;
        _engine = engine;

        var sources = new List<DecentSamplerModulationSource>();
        var voiceTargets = new List<DecentSamplerParameter>();

        foreach (var modulator in instrument.Modulators)
        {
            var live = new List<DecentSamplerBinding>();

            foreach (var binding in modulator.Bindings)
            {
                var route = engine?.RouteFor(binding);

                if (route == null || !route.IsTemporary)
                {
                    continue;
                }

                live.Add(binding);
            }

            if (live.Count == 0)
            {
                if (modulator.Bindings.Count == 0)
                {
                    problems?.Add(
                        "modulator " + modulator.Index + " (" + modulator.Kind +
                        ") has no bindings, so it modulates nothing");
                }

                continue;
            }

            var seed = SeedFor(modulator, randomSeed, sources.Count);
            var source = new DecentSamplerModulationSource(modulator, sources.Count, live, seed);
            sources.Add(source);

            if (!source.IsVoiceScope)
            {
                continue;
            }

            foreach (var binding in live)
            {
                foreach (var target in engine.RouteFor(binding).Targets)
                {
                    if (target is DecentSamplerParameter parameter && !voiceTargets.Contains(parameter))
                    {
                        voiceTargets.Add(parameter);
                    }
                }
            }
        }

        _sources = [.. sources];
        _voiceTargets = [.. voiceTargets];
        _globalStates = new DecentSamplerModulationState[_sources.Length];
        _voiceStates = new DecentSamplerModulationState[_sources.Length * Math.Max(1, _polyphony)];
        _voiceStarted = new bool[Math.Max(1, _polyphony)];

        IsActive = _sources.Length > 0;
        HasVoiceScope = _voiceTargets.Length > 0;

        StartGlobalSources();
    }

    // Whether the preset has any modulator that reaches anything, which is the one test the
    // synthesizer makes before doing per-block work.
    internal bool IsActive { get; }

    // Whether any modulator runs per voice, which is what makes the push-and-pop worth doing.
    internal bool HasVoiceScope { get; }

    // How many modulators are running, for tests and diagnostics.
    internal int SourceCount => _sources.Length;

    // The transport the musical-time LFOs follow. Never null in practice; the synthesizer sets it
    // every block.
    internal TempoSource Tempo
    {
        get => _tempo;
        set => _tempo = value;
    }

    internal DecentSamplerModulationSource SourceAt(int index) => _sources[index];

    // Puts every modulator back where it started and takes every contribution out of every target.
    internal void Reset()
    {
        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];
            source.ResetRandom();

            ClearAll(source, IParameterTarget.GlobalVoice);
            _globalStates[index] = default;

            for (var voice = 0; voice < _polyphony; voice++)
            {
                _voiceStates[(index * _polyphony) + voice] = default;
            }
        }

        for (var voice = 0; voice < _polyphony; voice++)
        {
            if (_voiceStarted[voice])
            {
                _engine.ClearVoiceModulation(voice);
                _voiceStarted[voice] = false;
            }
        }

        _pushedVoice = -1;
        StartGlobalSources();
    }

    // A note has been struck. Global-scope envelopes are gated by the keyboard as a whole, so one
    // starts on the first key down; global LFOs and random sources restart only when their trigger
    // says attack.
    internal void NoteOn(int channel, int key, int velocity, int heldKeysBefore)
    {
        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];

            if (source.IsVoiceScope)
            {
                continue;
            }

            ref var state = ref _globalStates[index];
            var context = GlobalContext(channel, key, velocity);

            switch (source.Kind)
            {
                case DecentSamplerModulatorKind.Envelope:
                    if (heldKeysBefore == 0 || state.Stage == DecentSamplerModulationStage.Release ||
                        state.Stage == DecentSamplerModulationStage.Finished || !state.IsRunning)
                    {
                        source.Start(ref state, context);
                    }

                    break;

                case DecentSamplerModulatorKind.MidiVelocity:
                    state.Velocity = velocity;
                    break;

                case DecentSamplerModulatorKind.Lfo:
                    if (((DecentSamplerLfoModulator)source.Modulator).Trigger ==
                        DecentSamplerModulatorTrigger.Attack)
                    {
                        source.Start(ref state, context);
                    }

                    break;

                case DecentSamplerModulatorKind.Random:
                {
                    var random = (DecentSamplerRandomModulator)source.Modulator;

                    if ((random.Mode ?? DecentSamplerRandomMode.NoteOn) == DecentSamplerRandomMode.NoteOn ||
                        random.Trigger == DecentSamplerModulatorTrigger.Attack)
                    {
                        source.Start(ref state, context);
                    }

                    break;
                }
            }
        }
    }

    // The last key came up: a global-scope envelope releases.
    internal void NoteOff(int heldKeysAfter)
    {
        if (heldKeysAfter > 0)
        {
            return;
        }

        for (var index = 0; index < _sources.Length; index++)
        {
            if (!_sources[index].IsVoiceScope)
            {
                _sources[index].Release(ref _globalStates[index]);
            }
        }
    }

    // A voice has started: every voice-scope modulator gets a fresh instance for it.
    internal void VoiceStarted(int voiceId, int channel, int key, int velocity)
    {
        if (!IsVoice(voiceId))
        {
            return;
        }

        _voiceStarted[voiceId] = true;

        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];

            if (!source.IsVoiceScope)
            {
                continue;
            }

            source.Start(
                ref _voiceStates[(index * _polyphony) + voiceId],
                new DecentSamplerModulationContext(_tempo, _mpe, channel, key, velocity));
        }
    }

    // The voice's key came up: its voice-scope envelopes release.
    internal void VoiceReleased(int voiceId)
    {
        if (!IsVoice(voiceId))
        {
            return;
        }

        for (var index = 0; index < _sources.Length; index++)
        {
            if (_sources[index].IsVoiceScope)
            {
                _sources[index].Release(ref _voiceStates[(index * _polyphony) + voiceId]);
            }
        }
    }

    // The voice has finished: every contribution it made comes out of every target.
    internal void VoiceEnded(int voiceId)
    {
        if (!IsVoice(voiceId) || !_voiceStarted[voiceId])
        {
            return;
        }

        _voiceStarted[voiceId] = false;

        for (var index = 0; index < _sources.Length; index++)
        {
            if (_sources[index].IsVoiceScope)
            {
                _voiceStates[(index * _polyphony) + voiceId] = default;
            }
        }

        _engine.ClearVoiceModulation(voiceId);
    }

    // Advances every global-scope modulator by one block.
    internal void TickGlobal()
    {
        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];

            if (source.IsVoiceScope)
            {
                continue;
            }

            ref var state = ref _globalStates[index];
            var context = GlobalContext(MasterChannel(), -1, state.Velocity);

            if (source.Tick(ref state, _blockSeconds, context, out var value))
            {
                ApplyAll(source, IParameterTarget.GlobalVoice, value);
                state.IsApplied = true;
            }
            else if (state.IsApplied)
            {
                ClearAll(source, IParameterTarget.GlobalVoice);
                state.IsApplied = false;
            }
        }
    }

    // Advances one voice's own modulators by one block.
    internal void TickVoice(int voiceId, int channel, int key)
    {
        if (!IsVoice(voiceId))
        {
            return;
        }

        // The MPE rule that a newer note on the same member channel takes the channel's expression:
        // a superseded note keeps whatever timbre, pressure and controller values it had.
        var ownsChannel = _mpe.OwnsChannelExpression(channel, key);

        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];

            if (!source.IsVoiceScope)
            {
                continue;
            }

            ref var state = ref _voiceStates[(index * _polyphony) + voiceId];

            if (!ownsChannel && ReadsChannelExpression(source))
            {
                continue;
            }

            var context = new DecentSamplerModulationContext(_tempo, _mpe, channel, key, state.Velocity);

            if (source.Tick(ref state, _blockSeconds, context, out var value))
            {
                ApplyAll(source, voiceId, value);
                state.IsApplied = true;
            }
            else if (state.IsApplied)
            {
                ClearAll(source, voiceId);
                state.IsApplied = false;
            }
        }
    }

    // Writes one voice's own effective values onto the parameters its modulators reach, so that the
    // voice renders with them. Always paired with PopVoice.
    internal void PushVoice(int voiceId)
    {
        if (!HasVoiceScope)
        {
            return;
        }

        _pushedVoice = voiceId;

        for (var index = 0; index < _voiceTargets.Length; index++)
        {
            _voiceTargets[index].PublishForVoice(voiceId);
        }
    }

    // Puts the global values back after a voice has rendered.
    internal void PopVoice()
    {
        if (!HasVoiceScope || _pushedVoice < 0)
        {
            return;
        }

        _pushedVoice = -1;

        for (var index = 0; index < _voiceTargets.Length; index++)
        {
            _voiceTargets[index].PublishGlobally();
        }
    }

    private static int SeedFor(DecentSamplerModulator modulator, int randomSeed, int sourceIndex)
    {
        if (modulator is DecentSamplerRandomModulator random && random.Seed is int declared)
        {
            return declared;
        }

        // No seed written: the synthesizer's own seed still makes the render reproducible, and each
        // modulator gets its own stream so two random sources do not move together.
        return unchecked(randomSeed + (sourceIndex * 7919));
    }

    private static bool ReadsChannelExpression(DecentSamplerModulationSource source) =>
        source.Kind switch
        {
            DecentSamplerModulatorKind.MpeTimbre => true,
            DecentSamplerModulatorKind.MpePressure => true,
            DecentSamplerModulatorKind.MidiCc =>
                ((DecentSamplerMidiCcModulator)source.Modulator).ChannelFollowsVoice != false,
            _ => false,
        };

    private void StartGlobalSources()
    {
        for (var index = 0; index < _sources.Length; index++)
        {
            var source = _sources[index];

            if (source.IsVoiceScope || source.Kind == DecentSamplerModulatorKind.Envelope)
            {
                // A global envelope waits for the keyboard; everything else free-runs from the start.
                continue;
            }

            source.Start(ref _globalStates[index], GlobalContext(MasterChannel(), -1, 0));
        }
    }

    private DecentSamplerModulationContext GlobalContext(int channel, int key, int velocity) =>
        new DecentSamplerModulationContext(_tempo, _mpe, channel, key, velocity);

    // Which channel a global-scope MPE source reads. With a zone configured that is the zone's
    // master, whose gestures apply to every note; otherwise channel 1.
    private int MasterChannel()
    {
        var lower = _mpe.LowerZone;

        if (lower.IsActive)
        {
            return lower.MasterChannel - 1;
        }

        var upper = _mpe.UpperZone;
        return upper.IsActive ? upper.MasterChannel - 1 : 0;
    }

    private void ApplyAll(DecentSamplerModulationSource source, int voiceId, double value)
    {
        var bindings = source.Bindings;
        var input = source.InputFor(value);

        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];

            if (binding.Enabled == false)
            {
                continue;
            }

            _engine.ApplyModulation(
                binding, source.SourceId, voiceId, input, source.AmountFor(binding), source.RestingOutput);
        }
    }

    private void ClearAll(DecentSamplerModulationSource source, int voiceId)
    {
        var bindings = source.Bindings;

        for (var index = 0; index < bindings.Length; index++)
        {
            _engine.ClearModulation(bindings[index], source.SourceId, voiceId);
        }
    }

    private bool IsVoice(int voiceId) => 0 <= voiceId && voiceId < _polyphony;
}

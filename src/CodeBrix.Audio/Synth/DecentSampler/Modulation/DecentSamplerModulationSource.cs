using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Modulation;

// One <modulators> element turned into something that produces a number once per block, plus the
// bindings that number is written through.
//
// Everything the preset can change while it plays - modAmount, an LFO's frequency and shape, an
// envelope's segments - is read from the parsed model on EVERY tick rather than cached here, which
// is what makes the modulator's own parameters bindable (Appendix B's type="modulator" bindings)
// with no extra machinery.
//
// UNMEASURED, all of it: the developer guide describes each modulator in words and gives no traces,
// and nothing about modulators was captured in the reference-player measurement round. The choices
// are listed in the completion report; the ones that matter most are the LFO shapes' phase and
// polarity, what delayTime does to the output, and the smoothing law of the two MPE sources.
internal sealed class DecentSamplerModulationSource
{
    private readonly DecentSamplerBinding[] _bindings;
    private readonly bool _isVoiceScope;

    internal DecentSamplerModulationSource(
        DecentSamplerModulator modulator, int sourceId, IReadOnlyList<DecentSamplerBinding> bindings, int seed)
    {
        Modulator = modulator;
        SourceId = sourceId;
        _bindings = [.. bindings];
        _isVoiceScope = (modulator.Scope ?? DefaultScope(modulator.Kind)) == DecentSamplerModulatorScope.Voice;
        Random = new Random(seed);
        Seed = seed;
    }

    internal DecentSamplerModulator Modulator { get; }

    // Identifies this modulator's contributions inside every target it reaches.
    internal int SourceId { get; }

    // Whether each note gets its own copy. The guide's defaults differ by element: global for the
    // LFO, the controller source and the random source, per voice for the envelope, the velocity
    // source and the two MPE sources.
    internal bool IsVoiceScope => _isVoiceScope;

    internal DecentSamplerBinding[] Bindings => _bindings;

    // The seeded generator behind a random modulator. Shared by every voice of one modulator, so the
    // same performance draws the same numbers in the same order on every run.
    internal Random Random { get; private set; }

    internal int Seed { get; }

    internal DecentSamplerModulatorKind Kind => Modulator.Kind;

    // The depth of one binding, read live so that a MOD_AMOUNT binding takes effect at once. A depth
    // written on the binding itself wins over the modulator's.
    internal double AmountFor(DecentSamplerBinding binding) =>
        binding.ModAmount ?? Modulator.ModAmount ?? 1.0;

    internal void ResetRandom() => Random = new Random(Seed);

    // Starts one instance: a note-on for a voice-scope modulator, and for a global one either the
    // first note (an envelope) or a note-on while trigger="attack" (an LFO or a random source).
    internal void Start(ref DecentSamplerModulationState state, in DecentSamplerModulationContext context)
    {
        state.IsRunning = true;
        state.Phase = 0.0;
        state.ElapsedSeconds = 0.0;
        state.ReleaseLevel = 0.0;
        state.Velocity = context.Velocity;
        state.Stage = DecentSamplerModulationStage.Delay;
        state.DelayRemaining = Math.Max(0.0, DelaySeconds());

        switch (Kind)
        {
            case DecentSamplerModulatorKind.Random:
                state.Value = NextRandom();
                state.HasValue = true;
                break;

            case DecentSamplerModulatorKind.MpeTimbre:
            case DecentSamplerModulatorKind.MpePressure:
                // A fresh note starts AT the controller's present value rather than sliding up to it,
                // so the first note after a slide is not silently dark.
                state.Value = ReadMpe(context);
                state.HasValue = true;
                break;

            default:
                state.HasValue = false;
                state.Value = 0.0;
                break;
        }
    }

    // The key came up. Only the envelope has anything to do.
    internal void Release(ref DecentSamplerModulationState state)
    {
        if (Kind != DecentSamplerModulatorKind.Envelope || !state.IsRunning)
        {
            return;
        }

        if (state.Stage == DecentSamplerModulationStage.Release ||
            state.Stage == DecentSamplerModulationStage.Finished)
        {
            return;
        }

        state.ReleaseLevel = EnvelopeLevel(ref state);
        state.Stage = DecentSamplerModulationStage.Release;
        state.ElapsedSeconds = 0.0;
    }

    // Advances one block and returns the modulator's output. The result is false while the modulator
    // is offering nothing at all, which is what an LFO does inside its delayTime.
    internal bool Tick(
        ref DecentSamplerModulationState state,
        double seconds,
        in DecentSamplerModulationContext context,
        out double value)
    {
        value = 0.0;

        if (!state.IsRunning)
        {
            return false;
        }

        switch (Kind)
        {
            case DecentSamplerModulatorKind.Lfo:
                return TickLfo(ref state, seconds, context, out value);

            case DecentSamplerModulatorKind.Envelope:
                value = TickEnvelope(ref state, seconds);
                return true;

            case DecentSamplerModulatorKind.MidiCc:
                value = TickController(context);
                return true;

            case DecentSamplerModulatorKind.MidiVelocity:
                value = Math.Clamp(state.Velocity, 0, 127) / 127.0;
                return true;

            case DecentSamplerModulatorKind.MpeTimbre:
            case DecentSamplerModulatorKind.MpePressure:
                value = TickMpe(ref state, seconds, context);
                return true;

            case DecentSamplerModulatorKind.Random:
                value = TickRandom(ref state, seconds);
                return true;

            default:
                return false;
        }
    }

    // The range the binding translation reads the output against. Every modulator but the random one
    // produces 0 to 1; the guide says a <random> "always produces a value between -1 and 1", so its
    // own range is declared and a linear translation still spans the whole output range.
    internal DecentSamplerBindingInput InputFor(double value) =>
        Kind == DecentSamplerModulatorKind.Random
            ? new DecentSamplerBindingInput(value, -1.0, 1.0)
            : DecentSamplerBindingInput.Normalised(value);

    // The raw value this modulator sits at when nothing is driving it, which is what the `modulate`
    // behaviour takes back out after translation. MEASURED for the two kinds the round covered: a
    // <midiCC> rests at 0 (so its neutral is translationOutputMin) and an <lfo> at 0.5 (the middle of
    // its 0-to-1 swing, so its neutral is the middle of the output range). The rest are INFERRED from
    // what each source reads when nothing has happened: an envelope and a <velocity> both start at 0,
    // MPE pressure is 0 with no finger weight, MPE timbre centres at 0.5 as the specification's CC 74
    // does, and a <random> rests at the middle of its declared -1..1 range.
    internal double RestingOutput =>
        Kind switch
        {
            DecentSamplerModulatorKind.Lfo => 0.5,
            DecentSamplerModulatorKind.MpeTimbre => 0.5,
            DecentSamplerModulatorKind.Random => 0.0,
            _ => 0.0,
        };

    private static DecentSamplerModulatorScope DefaultScope(DecentSamplerModulatorKind kind) =>
        kind switch
        {
            DecentSamplerModulatorKind.Envelope => DecentSamplerModulatorScope.Voice,
            DecentSamplerModulatorKind.MidiVelocity => DecentSamplerModulatorScope.Voice,
            DecentSamplerModulatorKind.MpeTimbre => DecentSamplerModulatorScope.Voice,
            DecentSamplerModulatorKind.MpePressure => DecentSamplerModulatorScope.Voice,
            _ => DecentSamplerModulatorScope.Global,
        };

    private double DelaySeconds() =>
        Modulator switch
        {
            DecentSamplerLfoModulator lfo => lfo.DelayTime ?? 0.0,
            DecentSamplerEnvelopeModulator envelope => envelope.DelayTime ?? 0.0,
            _ => 0.0,
        };

    // ---- the LFO -----------------------------------------------------------------------------------

    private bool TickLfo(
        ref DecentSamplerModulationState state,
        double seconds,
        in DecentSamplerModulationContext context,
        out double value)
    {
        var lfo = (DecentSamplerLfoModulator)Modulator;

        if (state.DelayRemaining > 0.0)
        {
            // "During this delay period, the LFO outputs zero." Read as offering NO modulation at
            // all, which is the only reading that behaves for all four modBehaviors: an output of a
            // literal zero would translate to the bottom of the binding's output range and pull the
            // target down instead of leaving it alone. UNMEASURED.
            state.DelayRemaining -= seconds;
            value = 0.0;
            return false;
        }

        value = Shape(lfo.Shape ?? DecentSamplerLfoShape.Sine, state.Phase);

        var hertz = LfoHertz(lfo, context.Tempo);
        state.Phase += hertz * seconds;

        if (state.Phase >= 1.0 || state.Phase < 0.0)
        {
            state.Phase -= Math.Floor(state.Phase);
        }

        return true;
    }

    private static double LfoHertz(DecentSamplerLfoModulator lfo, TempoSource tempo)
    {
        var frequency = lfo.Frequency ?? 1.0;

        if ((lfo.FrequencyFormat ?? DecentSamplerFrequencyFormat.Hz) != DecentSamplerFrequencyFormat.MusicalTime)
        {
            return frequency <= 0.0 ? 0.0 : frequency;
        }

        // In musical time the frequency attribute is a subdivision index and the subdivision is the
        // LFO's PERIOD, so index 13 (a quarter note) at 120 BPM is one cycle every half second.
        var index = (int)Math.Round(frequency, MidpointRounding.AwayFromZero);
        var period = DecentSamplerTempo.SecondsForSubdivision(index, tempo);
        return period <= 0.0 ? 0.0 : 1.0 / period;
    }

    // The four shapes, 0 to 1 with 0.5 at rest. UNMEASURED: the phase each shape starts at and
    // whether the saw rises or falls are choices, not readings. Sine and triangle start at rest and
    // rise, the saw rises from its minimum across the cycle, and the square starts high.
    private static double Shape(DecentSamplerLfoShape shape, double phase)
    {
        var wrapped = phase - Math.Floor(phase);

        return shape switch
        {
            DecentSamplerLfoShape.Square => wrapped < 0.5 ? 1.0 : 0.0,
            DecentSamplerLfoShape.Saw => wrapped,
            DecentSamplerLfoShape.Triangle => wrapped < 0.25
                ? 0.5 + (2.0 * wrapped)
                : wrapped < 0.75 ? 1.5 - (2.0 * wrapped) : (2.0 * wrapped) - 1.5,
            _ => 0.5 + (0.5 * Math.Sin(2.0 * Math.PI * wrapped)),
        };
    }

    // ---- the envelope ------------------------------------------------------------------------------

    private double TickEnvelope(ref DecentSamplerModulationState state, double seconds)
    {
        if (state.Stage == DecentSamplerModulationStage.Delay)
        {
            if (state.DelayRemaining > 0.0)
            {
                state.DelayRemaining -= seconds;
                return 0.0;
            }

            state.Stage = DecentSamplerModulationStage.Attack;
            state.ElapsedSeconds = 0.0;
        }

        var level = EnvelopeLevel(ref state);
        state.ElapsedSeconds += seconds;
        return level;
    }

    private double EnvelopeLevel(ref DecentSamplerModulationState state)
    {
        var envelope = (DecentSamplerEnvelopeModulator)Modulator;
        var sustain = Math.Clamp(envelope.Sustain ?? 1.0, 0.0, 1.0);

        switch (state.Stage)
        {
            case DecentSamplerModulationStage.Delay:
                return 0.0;

            case DecentSamplerModulationStage.Attack:
            {
                var attack = Math.Max(0.0, envelope.Attack ?? 0.0);

                if (attack <= 0.0 || state.ElapsedSeconds >= attack)
                {
                    state.Stage = DecentSamplerModulationStage.Decay;
                    state.ElapsedSeconds = 0.0;
                    return EnvelopeLevel(ref state);
                }

                // MEASURED (round 3, item 45): every stage of an <envelope> modulator runs one fixed
                // exponential-approach shape, gain = (1 - exp(-4x)) / (1 - exp(-4)) across the stage.
                // The default curve attributes put ModulatorShape at exactly that.
                return DecentSamplerCurves.ModulatorShape(
                    -(envelope.AttackCurve ?? -100.0), state.ElapsedSeconds / attack);
            }

            case DecentSamplerModulationStage.Decay:
            {
                var decay = Math.Max(0.0, envelope.Decay ?? 0.0);

                if (decay <= 0.0 || state.ElapsedSeconds >= decay)
                {
                    state.Stage = DecentSamplerModulationStage.Sustain;
                    state.ElapsedSeconds = 0.0;
                    return sustain;
                }

                // The decay is the mirror of the attack's curve toward the sustain, and `sustain` is a
                // LINEAR AMPLITUDE: sustain="0.5" measured -6.02 dB below the envelope's own peak.
                var fallen = DecentSamplerCurves.ModulatorShape(
                    envelope.DecayCurve ?? 100.0, state.ElapsedSeconds / decay);
                return 1.0 - ((1.0 - sustain) * fallen);
            }

            case DecentSamplerModulationStage.Sustain:
                return sustain;

            case DecentSamplerModulationStage.Release:
            {
                var release = Math.Max(0.0, envelope.Release ?? 0.0);

                if (release <= 0.0 || state.ElapsedSeconds >= release)
                {
                    state.Stage = DecentSamplerModulationStage.Finished;
                    return 0.0;
                }

                // The release runs for exactly `release` seconds and ENDS AT ZERO (measured: silence at
                // note-off + 1.0 s for release="1.0", with the group's own 4 s release still open).
                var fallen = DecentSamplerCurves.ModulatorShape(
                    envelope.ReleaseCurve ?? 100.0, state.ElapsedSeconds / release);
                return state.ReleaseLevel * (1.0 - fallen);
            }

            default:
                return 0.0;
        }
    }

    // ---- the MIDI and MPE sources -------------------------------------------------------------------

    private double TickController(in DecentSamplerModulationContext context)
    {
        var modulator = (DecentSamplerMidiCcModulator)Modulator;
        var controller = Math.Clamp(modulator.Number ?? 1, 0, 127);
        var channel = ChannelFor(modulator, context);

        return context.Mpe.Controller(channel, controller) / 127.0;
    }

    // channel="voice" reads the channel the note arrived on, which is what makes a per-note
    // controller behave in an expressive performance. A fixed channel is written 1 to 16 and read
    // 0-based. The guide's own default: "voice" for a voice-scope modulator, channel 1 for a global
    // one - a global modulator has no voice to follow.
    private static int ChannelFor(
        DecentSamplerMidiCcModulator modulator, in DecentSamplerModulationContext context)
    {
        if (modulator.ChannelFollowsVoice == true)
        {
            return context.Channel;
        }

        if (modulator.Channel is int fixedChannel && fixedChannel >= 1 && fixedChannel <= 16)
        {
            return fixedChannel - 1;
        }

        return context.Key >= 0 ? context.Channel : 0;
    }

    private double TickMpe(
        ref DecentSamplerModulationState state, double seconds, in DecentSamplerModulationContext context)
    {
        var target = ReadMpe(context);

        if (!state.HasValue)
        {
            state.Value = target;
            state.HasValue = true;
            return target;
        }

        var rising = target > state.Value;
        var milliseconds = Modulator switch
        {
            DecentSamplerMpeTimbreModulator timbre =>
                rising ? timbre.RisingSmoothingTime ?? 0.0 : timbre.FallingSmoothingTime ?? 0.0,
            DecentSamplerMpePressureModulator pressure =>
                rising ? pressure.RisingSmoothingTime ?? 0.0 : pressure.FallingSmoothingTime ?? 0.0,
            _ => 0.0,
        };

        if (milliseconds <= 0.0)
        {
            state.Value = target;
            return target;
        }

        // UNMEASURED: read as a FULL-SCALE slew, so the stated time is what a move across the whole
        // 0-to-1 range takes and a shorter move arrives sooner. The alternative reading - an
        // exponential whose time constant is the stated time - never actually reaches its target.
        var step = seconds / (milliseconds / 1000.0);
        var difference = target - state.Value;

        state.Value += Math.Abs(difference) <= step ? difference : Math.Sign(difference) * step;
        return state.Value;
    }

    private double ReadMpe(in DecentSamplerModulationContext context) =>
        Kind == DecentSamplerModulatorKind.MpeTimbre
            ? context.Mpe.Timbre(context.Channel)
            : context.Mpe.Pressure(context.Channel, context.Key < 0 ? 0 : context.Key);

    // ---- the random source ---------------------------------------------------------------------------

    private double TickRandom(ref DecentSamplerModulationState state, double seconds)
    {
        var random = (DecentSamplerRandomModulator)Modulator;

        if ((random.Mode ?? DecentSamplerRandomMode.NoteOn) != DecentSamplerRandomMode.Periodic)
        {
            return state.Value;
        }

        var frequency = random.Frequency ?? 1.0;

        if (frequency <= 0.0)
        {
            return state.Value;
        }

        state.Phase += seconds * frequency;

        if (state.Phase >= 1.0)
        {
            state.Phase -= Math.Floor(state.Phase);
            state.Value = NextRandom();
        }

        return state.Value;
    }

    private double NextRandom() => (Random.NextDouble() * 2.0) - 1.0;
}

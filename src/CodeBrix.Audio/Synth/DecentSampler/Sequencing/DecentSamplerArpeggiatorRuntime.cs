using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// The arpeggiator: one per preset, and only ever one. When it is armed it CONSUMES every note that
// reaches it and emits its own instead, stepping through the notes being held.
//
// EVERY PARAMETER IS READ AT THE TOP OF EVERY STEP, straight off the parsed <arpeggiator> element,
// because the binding engine writes an effective value onto that element and a modulator contributes
// to it there too. That is what makes an LFO on ARP_GATE_LENGTH or a knob on ARP_RATE_MULTIPLIER
// audible on the very next note.
//
// RATE. One step is the arpSyncDivision note value at the working tempo, divided by
// arpRateMultiplier - "higher values play faster":
//     stepSeconds = subdivisionBeats * 60 / bpm / arpRateMultiplier
// with bpm the live tempo when arpFollowGlobalTempo is true and arpOverrideBpm when it is false.
// The subdivision table is the one every musical-time feature of the format shares.
internal sealed class DecentSamplerArpeggiatorRuntime
{
    // The guide's documented ranges.
    private const double MinimumRateMultiplier = 0.01;
    private const double MaximumRateMultiplier = 100.0;
    private const double MinimumOverrideBpm = 1.0;
    private const double MaximumOverrideBpm = 1000.0;
    private const double MaximumGateLength = 16.0;

    // The guide's documented defaults for the two attributes whose absence changes the sound.
    private const double DefaultGateLength = 0.75;

    // A hard ceiling on steps per block, so an absurd rate cannot spin the render thread.
    private const int MaximumStepsPerBlock = 256;

    private readonly DecentSamplerSequencingRuntime _owner;
    private readonly DecentSamplerInstrument _instrument;

    private readonly List<DecentSamplerArpNote> _held = [];
    private readonly List<DecentSamplerArpNote> _pool = [];
    private readonly List<DecentSamplerArpNote> _pattern = [];
    private readonly List<DecentSamplerArpNote> _adopted = [];

    private DecentSamplerArpeggiator _element;
    private Random _random;

    private double _framesUntilStep;
    private int _stepIndex;
    private int _lastPatternIndex = -1;
    private int _pressCounter;
    private bool _wasEnabled;
    private bool _patternIsStale = true;

    private DecentSamplerArpOrder _builtOrder;
    private int _builtOctaveRange;
    private DecentSamplerArpOctaveMode _builtOctaveMode;
    private int _builtStepCount;

    internal DecentSamplerArpeggiatorRuntime(
        DecentSamplerSequencingRuntime owner, DecentSamplerInstrument instrument, int randomSeed)
    {
        _owner = owner;
        _instrument = instrument;
        _element = instrument.Arpeggiator;
        _random = new Random(randomSeed);
    }

    // Whether the arpeggiator is armed. The <arpeggiator> element is created on demand by the binding
    // engine, so a preset that only turns it on from a knob still gets one.
    internal bool IsEnabled => _element != null && (_element.Enabled ?? false);

    // Whether the preset declares an <arpeggiator> at all.
    internal bool IsDeclared => _element != null;

    // How many notes are being held. For the tests.
    internal int HeldNoteCount => _held.Count;

    // The pattern the arpeggiator would step through right now, rebuilt on demand. For the tests.
    internal IReadOnlyList<DecentSamplerArpNote> Pattern
    {
        get
        {
            RebuildIfNeeded();
            return _pattern;
        }
    }

    // Takes a note the arpeggiator has consumed. Returns false when it was already held, so that a
    // repeated note-on does not double the pool.
    internal bool HoldNote(int channel, int key, int velocity)
    {
        for (var index = 0; index < _held.Count; index++)
        {
            if (_held[index].Channel == channel && _held[index].Key == key)
            {
                return false;
            }
        }

        var wasEmpty = _held.Count == 0;
        _held.Add(new DecentSamplerArpNote(channel, key, velocity, _pressCounter++));
        _patternIsStale = true;

        if (wasEmpty)
        {
            // MEASURED (round 2, item 30): the first key of a chord starts the clock and the FIRST
            // STEP FIRES IMMEDIATELY - 2 to 12 ms after the key-down, not on the next grid boundary.
            //
            // The step index is deliberately NOT reset here. The measurement found the pattern's
            // starting position is not always the lowest held note: two of three chords began on the
            // middle note, so a position register survives between chords. Only the clock restarts.
            _framesUntilStep = 0.0;
            _lastPatternIndex = -1;
        }

        return true;
    }

    // Releases a note the arpeggiator was holding. Returns true when it held one.
    internal bool ReleaseNote(int channel, int key)
    {
        for (var index = 0; index < _held.Count; index++)
        {
            if (_held[index].Channel != channel || _held[index].Key != key)
            {
                continue;
            }

            _held.RemoveAt(index);
            _patternIsStale = true;

            if (_held.Count == 0)
            {
                // MEASURED (round 2, item 30): releasing every key stops the pattern, but the step
                // that had ALREADY STARTED runs its full gate - a chord released mid-step at 1.30 s
                // still let the note that began at 1.011 s ring its whole 0.261 s. So only the
                // scheduling stops here; the sounding note keeps its own scheduled note-off.
                StopScheduling();
            }

            return true;
        }

        return false;
    }

    // Drops every held note and releases whatever the arpeggiator has sounding.
    internal void Clear()
    {
        _held.Clear();
        _pressCounter = 0;
        _patternIsStale = true;
        Silence();
    }

    // Drops every note one channel was holding, for a per-channel panic.
    internal void ClearChannel(int channel)
    {
        for (var index = _held.Count - 1; index >= 0; index--)
        {
            if (_held[index].Channel == channel)
            {
                _held.RemoveAt(index);
            }
        }

        _patternIsStale = true;

        if (_held.Count == 0)
        {
            Silence();
        }
    }

    // Advances the arpeggiator across one render block, emitting a note at every step boundary that
    // falls inside it. Also picks up an ARP_ENABLED change made since the last block.
    internal void Tick(long blockStartFrame, int blockSize, int sampleRate, TempoSource tempo)
    {
        // The element can appear after construction: a binding materialises one when a preset drives
        // the arpeggiator entirely from its interface.
        _element ??= _instrument.Arpeggiator;

        var enabled = IsEnabled;

        if (enabled != _wasEnabled)
        {
            if (enabled)
            {
                Arm();
            }
            else
            {
                Disarm();
            }

            _wasEnabled = enabled;
        }

        if (!enabled || _held.Count == 0)
        {
            return;
        }

        RebuildIfNeeded();

        if (_pattern.Count == 0)
        {
            return;
        }

        var remaining = (double)blockSize;
        var cursor = 0.0;
        var steps = 0;

        while (true)
        {
            if (_framesUntilStep >= remaining || steps >= MaximumStepsPerBlock)
            {
                _framesUntilStep -= remaining;

                if (_framesUntilStep < 0.0)
                {
                    _framesUntilStep = 0.0;
                }

                return;
            }

            cursor += _framesUntilStep;
            remaining -= _framesUntilStep;
            steps++;

            var stepFrames = StepFrames(sampleRate, tempo);
            var offset = (int)cursor;

            if (offset >= blockSize)
            {
                offset = blockSize - 1;
            }

            EmitStep(blockStartFrame, offset, stepFrames);
            _framesUntilStep = stepFrames;
        }
    }

    // The pattern is stale whenever the held notes changed or one of the four shape parameters moved.
    private void RebuildIfNeeded()
    {
        var order = _element?.Order ?? DecentSamplerArpOrder.Up;
        var octaveRange = _element?.OctaveRange ?? 1;
        var octaveMode = _element?.OctaveMode ?? DecentSamplerArpOctaveMode.ReplayPerOctave;
        var stepCount = _element?.StepCount ?? DecentSamplerArpeggiatorPattern.MaximumStepCount;

        if (!_patternIsStale && order == _builtOrder && octaveRange == _builtOctaveRange &&
            octaveMode == _builtOctaveMode && stepCount == _builtStepCount)
        {
            return;
        }

        DecentSamplerArpeggiatorPattern.Build(
            _held, order, octaveRange, octaveMode, stepCount, _pool, _pattern);

        _builtOrder = order;
        _builtOctaveRange = octaveRange;
        _builtOctaveMode = octaveMode;
        _builtStepCount = stepCount;
        _patternIsStale = false;

        if (_pattern.Count > 0)
        {
            _stepIndex %= _pattern.Count;
        }
        else
        {
            _stepIndex = 0;
        }
    }

    private void EmitStep(long blockStartFrame, int frameOffset, double stepFrames)
    {
        var order = _element?.Order ?? DecentSamplerArpOrder.Up;
        var index = SelectIndex(order);

        if (index < 0 || index >= _pattern.Count)
        {
            return;
        }

        var note = _pattern[index];
        _lastPatternIndex = index;
        _stepIndex = (_stepIndex + 1) % _pattern.Count;

        _owner.EmitFromArpeggiator(note.Channel, note.Key, note.Velocity, frameOffset);

        // arpGateLength is the fraction of ONE STEP the note rings for. 1.0 lets it run to the next
        // step and beyond; anything larger overlaps deliberately.
        var gate = Math.Clamp(_element?.GateLength ?? DefaultGateLength, 0.0, MaximumGateLength);
        var gateFrames = stepFrames * gate;

        if (gateFrames <= 0.0)
        {
            gateFrames = 1.0;
        }

        _owner.ScheduleNoteOff(
            this, note.Channel, note.Key, blockStartFrame + frameOffset + (long)gateFrames,
            arpeggiatorAware: false);
    }

    private int SelectIndex(DecentSamplerArpOrder order)
    {
        var count = _pattern.Count;

        switch (order)
        {
            case DecentSamplerArpOrder.Random:
                return _random.Next(count);

            case DecentSamplerArpOrder.RandomNoRepeat:
                if (count == 1 || _lastPatternIndex < 0)
                {
                    return _random.Next(count);
                }

                var draw = _random.Next(count - 1);
                return draw >= _lastPatternIndex ? draw + 1 : draw;

            default:
                return _stepIndex % count;
        }
    }

    // How many frames one step lasts, from the parameters as they stand at this instant.
    private double StepFrames(int sampleRate, TempoSource tempo)
    {
        var follow = _element?.FollowGlobalTempo ?? true;

        var beatsPerMinute = follow
            ? tempo.BeatsPerMinute
            : Math.Clamp(
                _element?.OverrideBpm ?? DecentSamplerSequencingRuntime.FixedBeatsPerMinute,
                MinimumOverrideBpm,
                MaximumOverrideBpm);

        if (beatsPerMinute <= 0.0)
        {
            beatsPerMinute = DecentSamplerSequencingRuntime.FixedBeatsPerMinute;
        }

        var division = _element?.SyncDivision ?? DecentSamplerSyncDivision.NoteOneSixteenth;
        var beats = DecentSamplerTempo.BeatsForSubdivision(DecentSamplerTempo.SubdivisionOf(division));

        var multiplier = Math.Clamp(
            _element?.RateMultiplier ?? 1.0, MinimumRateMultiplier, MaximumRateMultiplier);

        var seconds = beats * 60.0 / beatsPerMinute / multiplier;
        var frames = seconds * sampleRate;

        // One frame is the floor; a step shorter than that would be inaudible and would spin.
        return frames < 1.0 ? 1.0 : frames;
    }

    // Switched on in the middle of a held chord: the arpeggiator takes the keys that are already
    // down, and the raw voices playing them are released, because the guide says an armed
    // arpeggiator takes over every note played rather than layering over it.
    private void Arm()
    {
        if (_held.Count > 0)
        {
            return;
        }

        _adopted.Clear();
        _owner.Host.CollectHeldKeys(_adopted);

        for (var index = 0; index < _adopted.Count; index++)
        {
            var note = _adopted[index];
            _held.Add(new DecentSamplerArpNote(note.Channel, note.Key, note.Velocity, _pressCounter++));
            _owner.Host.StopNote(note.Channel, note.Key);
        }

        _patternIsStale = true;
        _framesUntilStep = 0.0;
        _stepIndex = 0;
        _lastPatternIndex = -1;
    }

    // Switched off: the notes it was playing are released. The keys the player is still holding are
    // NOT handed back to the sampler, because they were consumed on the way in and never sounded.
    private void Disarm()
    {
        Silence();
        _held.Clear();
        _pressCounter = 0;
        _patternIsStale = true;
    }

    private void Silence()
    {
        _owner.ReleaseNotesOf(this);
        StopScheduling();
    }

    // Stops scheduling further steps without touching what is already sounding. The step index is
    // kept: MEASURED, the arpeggiator's position register survives between chords.
    private void StopScheduling()
    {
        _framesUntilStep = 0.0;
        _lastPatternIndex = -1;
    }

    // Reseeds the random orders, so a replay of the same events renders identically.
    internal void Reseed(int seed) => _random = new Random(seed);
}

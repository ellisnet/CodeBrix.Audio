using System;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// One running note sequence. A preset may have several going at once - the guide keys them by
// seqPlayerIdentifier, and a key-triggered sequence with no identifier gets one player per key.
//
// TIME. Positions and lengths are in SEQUENCE BEATS. The sequence's own rate multiplies them, the
// binding's seqPlaybackRate multiplies them again, and the tempo turns them into seconds:
//     sequence beats per second = beatsPerMinute / 60 * rate * seqPlaybackRate
// seqFollowGlobalTempo="false" replaces the live tempo with a fixed 120 BPM, exactly as the guide
// says. Every one of those four values is read on EVERY block, so a RATE binding on the sequence or
// a SEQ_PLAYBACK_RATE binding on the trigger changes the speed of a sequence that is already running.
//
// ORDER. The loop mode decides WHICH note plays at each onset; the onsets themselves always follow
// the positions the sequence declares, so the rhythm the author wrote is preserved in every mode.
// MEASURED (round 3, item 40): no mode moves the grid - every one of the five kept the declared
// 0.500 s step - so a mode substitutes the NOTE at each slot and never the rhythm. forward walks the
// notes in order, reverse plays note 0 and then walks backwards from the end, random draws one each
// time, random_no_repeat draws one that is not the note just played, and no_loop plays each note once
// and stops.
//
// TWO REFERENCE DEFECTS ARE DELIBERATELY NOT COPIED, on the measurement's own instruction:
//   - no_loop does not stop a sequence whose DECLARED LENGTH is 2 in the reference (two independent
//     length-2 sequences looped for ever while lengths 3 and 4 stopped, whatever the note count).
//     Here no_loop always stops after one pass.
//   - both random modes there have a HOLE: across two runs each, `random` never drew note index 3 of
//     four in 26 draws and `random_no_repeat` never drew index 2 in 30 draws, odds of 0.05 % and
//     0.1 % for a fair draw. Here both draw from every note.
internal sealed class DecentSamplerSequencePlayer
{
    // A hard ceiling on how many notes one player may emit in a single block, so that a sequence
    // whose notes all sit at position 0, or one driven at an absurd playback rate, cannot spin.
    private const int MaximumFiresPerBlock = 256;

    private readonly DecentSamplerSequencingRuntime _owner;

    private DecentSamplerBinding _binding;
    private DecentSamplerNoteSequence _sequence;
    private DecentSamplerSequenceNote[] _notes;
    private double[] _positions;
    private double _length;

    private double _beatsUntilOnset;
    private int _onsetIndex;
    private int _step;
    private int _lastNoteIndex = -1;
    private Random _random;

    private int _triggerChannel;
    private int _triggerKey;
    private int _triggerVelocity;
    private bool _fromNote;

    internal DecentSamplerSequencePlayer(DecentSamplerSequencingRuntime owner, string identifier)
    {
        _owner = owner;
        Identifier = identifier;
    }

    // The key this player is tracked under: seqPlayerIdentifier, or the triggering key when the
    // binding names none.
    internal string Identifier { get; }

    // Whether the player is producing notes.
    internal bool IsRunning { get; private set; }

    // The MIDI channel the trigger arrived on, or 0 for a control trigger.
    internal int TriggerChannel => _triggerChannel;

    // The MIDI key that started the player, or -1 when a control did.
    internal int TriggerKey => _fromNote ? _triggerKey : -1;

    // The binding driving the player. Its SEQ_* attributes are live, so a key switch that rewrites
    // them changes what a running sequence does.
    internal DecentSamplerBinding Binding => _binding;

    // Starts, or restarts, the sequence from its beginning.
    internal void Start(
        DecentSamplerBinding binding,
        DecentSamplerNoteSequence sequence,
        DecentSamplerSequenceNote[] notes,
        double[] positions,
        double length,
        in DecentSamplerSequenceTrigger trigger,
        int seed)
    {
        _binding = binding;
        _sequence = sequence;
        _notes = notes;
        _positions = positions;
        _length = length;

        _triggerChannel = trigger.IsFromNote ? trigger.Channel : 0;
        _triggerKey = trigger.Key;
        _triggerVelocity = trigger.Velocity;
        _fromNote = trigger.IsFromNote;

        _onsetIndex = 0;
        _step = 0;
        _lastNoteIndex = -1;

        // MEASURED (round 3, item 40): the FIRST note fires IMMEDIATELY on the key-down and the grid
        // runs from there. Eight trigger phases spread over the 0.500 s beat all produced a latency
        // of 3 to 12 ms - one processing block - with no correlation to the phase, where a grid-locked
        // start would have produced 500, 363, 229 and 87 ms. So the sequence's own clock starts at the
        // trigger and the whole timeline is shifted so that the first note lands on it: a sequence
        // whose first note is written at beat 2 sounds at once and its later onsets keep their
        // spacing. The wrap gap below (length - lastPosition + firstPosition) already carries the
        // shift, so the loop period stays the declared length.
        _beatsUntilOnset = 0.0;
        _random = new Random(seed);

        IsRunning = notes.Length > 0;
    }

    // Stops the sequence. The notes it has sounding are released by the runtime that owns the
    // pending note-offs.
    internal void Stop() => IsRunning = false;

    // Advances the sequence across one render block, emitting every note whose onset falls inside it.
    internal void Tick(long blockStartFrame, int blockSize, int sampleRate, TempoSource tempo)
    {
        if (!IsRunning || _notes.Length == 0)
        {
            return;
        }

        var rate = _sequence.Rate ?? 1.0;
        var playbackRate = _binding.SeqPlaybackRate ?? 1.0;

        // The guide's ranges: rate is a plain multiplier and seqPlaybackRate runs from 0.001 to 10000.
        // A zero or negative one freezes the sequence rather than making time run backwards.
        if (rate <= 0.0 || playbackRate <= 0.0 || double.IsNaN(rate) || double.IsNaN(playbackRate))
        {
            return;
        }

        var beatsPerMinute = (_binding.SeqFollowGlobalTempo ?? true)
            ? tempo.BeatsPerMinute
            : DecentSamplerSequencingRuntime.FixedBeatsPerMinute;

        var beatsPerFrame = beatsPerMinute / 60.0 * rate * playbackRate / sampleRate;

        if (beatsPerFrame <= 0.0 || double.IsNaN(beatsPerFrame) || double.IsInfinity(beatsPerFrame))
        {
            return;
        }

        var remaining = (double)blockSize;
        var cursor = 0.0;
        var fires = 0;

        while (IsRunning)
        {
            var framesToOnset = _beatsUntilOnset / beatsPerFrame;

            if (framesToOnset >= remaining || fires >= MaximumFiresPerBlock)
            {
                _beatsUntilOnset -= remaining * beatsPerFrame;

                if (_beatsUntilOnset < 0.0)
                {
                    _beatsUntilOnset = 0.0;
                }

                return;
            }

            cursor += framesToOnset;
            remaining -= framesToOnset;
            fires++;

            var offset = (int)cursor;

            if (offset >= blockSize)
            {
                offset = blockSize - 1;
            }

            FireOnset(blockStartFrame, offset, beatsPerFrame);
        }
    }

    private void FireOnset(long blockStartFrame, int frameOffset, double beatsPerFrame)
    {
        var count = _notes.Length;
        var loopMode = _binding.SeqLoopMode ?? DecentSamplerSeqLoopMode.Forward;
        var index = SelectNote(loopMode, count);

        if (index >= 0)
        {
            Emit(_notes[index], blockStartFrame, frameOffset, beatsPerFrame);
            _lastNoteIndex = index;
        }

        _step++;

        if (loopMode == DecentSamplerSeqLoopMode.NoLoop && _step >= count)
        {
            IsRunning = false;
            return;
        }

        // The onsets always follow the positions the sequence declares, wrapping over its length.
        var next = _onsetIndex + 1;

        if (next >= count)
        {
            _beatsUntilOnset = Math.Max(0.0, _length - _positions[count - 1]) + _positions[0];
            _onsetIndex = 0;
        }
        else
        {
            _beatsUntilOnset = Math.Max(0.0, _positions[next] - _positions[_onsetIndex]);
            _onsetIndex = next;
        }
    }

    private int SelectNote(DecentSamplerSeqLoopMode loopMode, int count)
    {
        switch (loopMode)
        {
            case DecentSamplerSeqLoopMode.Reverse:
            {
                // MEASURED (round 3, item 40): reverse plays note 0 FIRST and then walks backwards
                // from the end - 0, n-1, n-2 ... 1 - which is not the plain reversal that would
                // start at n-1.
                var step = _step % count;
                return step == 0 ? 0 : count - step;
            }

            case DecentSamplerSeqLoopMode.Random:
                // A FAIR draw over every note. The reference's own draw has a measured hole in it
                // (round 3, item 40) and the measurement says not to copy it.
                return _random.Next(count);

            case DecentSamplerSeqLoopMode.RandomNoRepeat:
                if (count == 1 || _lastNoteIndex < 0)
                {
                    return _random.Next(count);
                }

                // Draw over the pool with the last note left out, then step past it. That is uniform
                // over the notes that are allowed, which rejection sampling in a loop is not worth.
                var draw = _random.Next(count - 1);
                return draw >= _lastNoteIndex ? draw + 1 : draw;

            case DecentSamplerSeqLoopMode.NoLoop:
                // One pass and stop, at EVERY declared length. The reference fails to stop a sequence
                // whose declared length is 2 (round 3, item 40); that is a defect, not a rule.
                return _step < count ? _step : -1;

            default:
                return _step % count;
        }
    }

    private void Emit(
        DecentSamplerSequenceNote note, long blockStartFrame, int frameOffset, double beatsPerFrame)
    {
        var written = note.Note ?? -1;

        if (written < 0)
        {
            return;
        }

        var key = written + Transposition();

        if (key < 0 || key > 127)
        {
            return;
        }

        var velocity = Velocity(note);

        if (velocity <= 0)
        {
            return;
        }

        _owner.EmitFromSequencer(_triggerChannel, key, velocity, frameOffset);

        // MEASURED (round 3, item 40): a <note>'s length is TRUNCATED TO A WHOLE BEAT, the same way
        // its position is, and a length of 0 - or one that truncates to 0 - makes the note last ONE
        // STEP rather than ringing on. Notes longer than their step simply overlap and sum; there is
        // no note-stealing.
        var lengthBeats = Math.Floor(Math.Max(0.0, note.Length ?? 0.0));

        if (lengthBeats <= 0.0)
        {
            lengthBeats = 1.0;
        }

        var lengthFrames = lengthBeats / beatsPerFrame;
        _owner.ScheduleNoteOff(
            this, _triggerChannel, key, blockStartFrame + frameOffset + (long)lengthFrames,
            arpeggiatorAware: true);
    }

    // seqTranspose is an absolute shift in semitones; seqTransposeWithRootNote shifts by how far the
    // triggering key sits above the root it names (the guide: root 24, key 28, so up 4 semitones).
    //
    // MEASURED (round 3, item 40): seqTransposeWithRootNote OVERRIDES seqTranspose when a preset
    // writes both - they do not add. A binding carrying seqTranspose="12" alone moved a sequence up
    // 12; one carrying seqTransposeWithRootNote="24" alone moved it by key-minus-24; and one carrying
    // both moved it by key-minus-24 only.
    private int Transposition()
    {
        if (_fromNote && _binding.SeqTransposeWithRootNote != null)
        {
            return _triggerKey - (int)Math.Round(
                _binding.SeqTransposeWithRootNote.Value, MidpointRounding.AwayFromZero);
        }

        return (int)Math.Round(_binding.SeqTranspose ?? 0.0, MidpointRounding.AwayFromZero);
    }

    // A sequence note's velocity is written 0 to 1; seqTrackMidiInputVelocity blends it toward the
    // velocity of the key that triggered the sequence, with the same linear law the format's
    // ampVelTrack uses. It only applies to a sequence a MIDI key started, as the guide states.
    //
    // MEASURED (round 3, item 40), over three track values, three trigger velocities and two note
    // velocities:
    //     emitted = 127 * noteVelocity * ((1 - track) + track * triggerVelocity / 127)
    // in decibels relative to the same case at trigger velocity 127:
    //     track 1.0   0.00  -6.02  -12.04      (trigger 127, 64, 32)
    //     track 0.5   0.00  -2.45   -4.05
    //     track 0.0   0.00   0.00    0.00
    // and a <note velocity="0.5"> measured exactly 6.02 dB below the same note at velocity="1" at
    // every trigger velocity, so the attribute is a plain linear scale on the MIDI velocity.
    private int Velocity(DecentSamplerSequenceNote note)
    {
        var written = Math.Clamp(note.Velocity ?? 1.0, 0.0, 1.0);

        if (written <= 0.0)
        {
            return 0;
        }

        var scale = 1.0;

        if (_fromNote)
        {
            var track = Math.Clamp(
                _binding.SeqTrackMidiInputVelocity ?? DecentSamplerSequencingRuntime.DefaultVelocityTracking,
                0.0,
                1.0);

            scale = (1.0 - track) + (track * (Math.Clamp(_triggerVelocity, 0, 127) / 127.0));
        }

        var velocity = (int)Math.Round(written * 127.0 * scale, MidpointRounding.AwayFromZero);
        return Math.Clamp(velocity, 1, 127);
    }
}

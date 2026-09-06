using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// The note sequencer: the players a preset has running, and the rules that start and stop them.
//
// A <binding type="note_sequence"> with no `parameter` is a TRIGGER rather than a parameter change,
// which is the form every example in the guide uses. seqTriggerBehavior decides what it does:
//     midi_key   start when the key goes down, stop when it comes up (the default)
//     on         start, and keep going until something turns it off
//     off        stop the player named by seqPlayerIdentifier
// The player is tracked under seqPlayerIdentifier when the binding names one; a key-triggered
// binding that names none gets one player PER KEY, so a range like note="24-35" plays a different
// sequence player for every key in it and a chord of key switches runs several at once.
internal sealed class DecentSamplerSequenceRuntime
{
    private readonly DecentSamplerSequencingRuntime _owner;
    private readonly DecentSamplerInstrument _instrument;
    private readonly int _randomSeed;

    private readonly Dictionary<string, DecentSamplerSequencePlayer> _players =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<DecentSamplerSequencePlayer> _order = [];

    // One entry per <sequence>: its notes in position order, their positions, and its length.
    private readonly DecentSamplerSequenceNote[][] _notesByIndex;
    private readonly double[][] _positionsByIndex;
    private readonly double[] _lengthByIndex;

    private int _seedCounter;

    internal DecentSamplerSequenceRuntime(
        DecentSamplerSequencingRuntime owner, DecentSamplerInstrument instrument, int randomSeed)
    {
        _owner = owner;
        _instrument = instrument;
        _randomSeed = randomSeed;

        var sequences = instrument.Sequences;
        _notesByIndex = new DecentSamplerSequenceNote[sequences.Count][];
        _positionsByIndex = new double[sequences.Count][];
        _lengthByIndex = new double[sequences.Count];

        for (var index = 0; index < sequences.Count; index++)
        {
            Prepare(sequences[index], index);
        }
    }

    // How many players exist, running or not. For the tests.
    internal int PlayerCount => _order.Count;

    // How many players are producing notes right now.
    internal int RunningPlayerCount
    {
        get
        {
            var running = 0;

            for (var index = 0; index < _order.Count; index++)
            {
                if (_order[index].IsRunning)
                {
                    running++;
                }
            }

            return running;
        }
    }

    // Whether the preset declares any sequence at all.
    internal bool HasSequences => _notesByIndex.Length > 0;

    // Acts on one note-sequence trigger binding.
    internal void Trigger(DecentSamplerBinding binding, in DecentSamplerSequenceTrigger trigger)
    {
        var behavior = binding.SeqTriggerBehavior ?? DecentSamplerSeqTriggerBehavior.MidiKey;

        switch (behavior)
        {
            case DecentSamplerSeqTriggerBehavior.Off:
                StopPlayer(KeyFor(binding, trigger));
                return;

            case DecentSamplerSeqTriggerBehavior.On:
                if (trigger.IsFromNote && !trigger.IsNoteOn)
                {
                    // A handler with eventType="any" fires on the way up too; "on" starts the
                    // sequence once, on the way down.
                    return;
                }

                StartPlayer(binding, trigger);
                return;

            default:
                if (!trigger.IsFromNote)
                {
                    // midi_key with no key behind it: the guide says the behaviour follows a MIDI note
                    // binding, so a control firing one starts the sequence and nothing stops it.
                    StartPlayer(binding, trigger);
                    return;
                }

                if (trigger.IsNoteOn)
                {
                    StartPlayer(binding, trigger);
                }
                else
                {
                    StopPlayer(KeyFor(binding, trigger));
                }

                return;
        }
    }

    // Stops every player a released key started, for the case the <note> handler only listens for
    // note-on and so never fires its binding again on the way up.
    internal void MidiKeyReleased(int channel, int key)
    {
        for (var index = _order.Count - 1; index >= 0; index--)
        {
            var player = _order[index];

            if (player.IsRunning && player.TriggerKey == key && player.TriggerChannel == channel &&
                (player.Binding.SeqTriggerBehavior ?? DecentSamplerSeqTriggerBehavior.MidiKey) ==
                DecentSamplerSeqTriggerBehavior.MidiKey)
            {
                Stop(player);
            }
        }
    }

    // Stops every player one channel started, for a per-channel panic.
    internal void ChannelReleased(int channel)
    {
        for (var index = _order.Count - 1; index >= 0; index--)
        {
            var player = _order[index];

            if (player.IsRunning && player.TriggerKey >= 0 && player.TriggerChannel == channel)
            {
                Stop(player);
            }
        }
    }

    // Advances every running player across one render block.
    internal void Tick(long blockStartFrame, int blockSize, int sampleRate, TempoSource tempo)
    {
        for (var index = 0; index < _order.Count; index++)
        {
            _order[index].Tick(blockStartFrame, blockSize, sampleRate, tempo);
        }
    }

    // Stops everything, releasing whatever the players have sounding.
    internal void StopAll()
    {
        for (var index = 0; index < _order.Count; index++)
        {
            if (_order[index].IsRunning)
            {
                Stop(_order[index]);
            }
        }
    }

    // Throws away every player, for Reset.
    internal void Clear()
    {
        StopAll();
        _players.Clear();
        _order.Clear();
        _seedCounter = 0;
    }

    private void StartPlayer(DecentSamplerBinding binding, in DecentSamplerSequenceTrigger trigger)
    {
        var sequenceIndex = binding.SeqIndex ?? binding.Position ?? -1;

        if (sequenceIndex < 0 || sequenceIndex >= _notesByIndex.Length)
        {
            return;
        }

        var identifier = KeyFor(binding, trigger);

        if (!_players.TryGetValue(identifier, out var player))
        {
            player = new DecentSamplerSequencePlayer(_owner, identifier);
            _players[identifier] = player;
            _order.Add(player);
        }
        else if (player.IsRunning)
        {
            // Retriggering a player that is already going restarts it, and its ringing notes stop
            // rather than being left to hang.
            Stop(player);
        }

        player.Start(
            binding,
            _instrument.Sequences[sequenceIndex],
            _notesByIndex[sequenceIndex],
            _positionsByIndex[sequenceIndex],
            _lengthByIndex[sequenceIndex],
            trigger,
            unchecked(_randomSeed + (_seedCounter++ * 7919)));
    }

    private void StopPlayer(string identifier)
    {
        if (identifier != null && _players.TryGetValue(identifier, out var player) && player.IsRunning)
        {
            Stop(player);
        }
    }

    private void Stop(DecentSamplerSequencePlayer player)
    {
        player.Stop();
        _owner.ReleaseNotesOf(player);
    }

    // The identifier a player is tracked under. seqPlayerIdentifier wins; a key-triggered binding
    // with none gets one player per (channel, key), which is what makes a range of key switches play
    // one sequence each.
    private static string KeyFor(DecentSamplerBinding binding, in DecentSamplerSequenceTrigger trigger)
    {
        if (!string.IsNullOrWhiteSpace(binding.SeqPlayerIdentifier))
        {
            return binding.SeqPlayerIdentifier.Trim();
        }

        if (trigger.IsFromNote)
        {
            return "#key:" + trigger.Channel.ToString(CultureInfo.InvariantCulture) + ":" +
                   trigger.Key.ToString(CultureInfo.InvariantCulture);
        }

        return "#binding:" + binding.LineNumber.ToString(CultureInfo.InvariantCulture) + ":" +
               (binding.SeqIndex ?? binding.Position ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    // The notes of one sequence in position order, with the length the sequence runs for. The
    // `length` attribute is required; a sequence that leaves it out, or writes one shorter than its
    // own notes reach, falls back to the last onset so that it still loops sensibly.
    private void Prepare(DecentSamplerNoteSequence sequence, int index)
    {
        // MEASURED (round 3, item 40): a <note>'s position is TRUNCATED TO A WHOLE BEAT - "1.5" is 1
        // and "0.5" is 0 - and the sequence's DECLARED LENGTH truncates the sequence, so a note
        // written at or beyond it never plays. Both matter: a sequence written with fractional
        // positions plays CHORDS rather than a rhythm, which is the single most surprising thing in
        // the whole item.
        var declared = sequence.Length ?? 0.0;
        var limit = declared > 0.0 ? Math.Floor(declared) : double.PositiveInfinity;

        var notes = new List<DecentSamplerSequenceNote>(sequence.Notes.Count);
        var truncated = new List<double>(sequence.Notes.Count);

        foreach (var note in sequence.Notes)
        {
            if (note.Note == null || note.Position == null)
            {
                continue;
            }

            var beat = Math.Floor(Math.Max(0.0, note.Position.Value));

            if (beat >= limit)
            {
                continue;
            }

            notes.Add(note);
            truncated.Add(beat);
        }

        // A stable sort, so two notes on the same beat keep their document order - and after the
        // truncation two notes often ARE on the same beat.
        var order = new int[notes.Count];
        for (var i = 0; i < order.Length; i++) { order[i] = i; }

        var keys = truncated.ToArray();
        Array.Sort((double[])keys.Clone(), order);

        var sorted = new DecentSamplerSequenceNote[notes.Count];
        var positions = new double[notes.Count];
        var highest = 0.0;

        for (var i = 0; i < order.Length; i++)
        {
            sorted[i] = notes[order[i]];
            positions[i] = truncated[order[i]];
            highest = Math.Max(highest, positions[i]);
        }

        _notesByIndex[index] = sorted;
        _positionsByIndex[index] = positions;
        _lengthByIndex[index] = Math.Max(declared > 0.0 ? Math.Floor(declared) : 0.0, highest);
    }
}

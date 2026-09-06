using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// The <midi> element at run time, the note sequencer and the arpeggiator, and the note path that
// joins them to the sampler.
//
// THE NOTE PATH, in order, for every note that arrives:
//     1. the <midi> handlers run first, exactly as the guide says: "the bindings that live below
//        this note listener are called before any notes are played". A handler carrying
//        swallowNotes="true" CONSUMES the note - that is a key switch, and the sampler never sees it.
//     2. the arpeggiator, when it is armed, consumes the note and holds it. Its own notes are what
//        the sampler plays.
//     3. otherwise the note goes straight into the voice runtime.
//
// A GENERATED NOTE - one the sequencer produced - takes the SAME path from step 1, so a sequence can
// work a key switch and can feed the arpeggiator. A note the ARPEGGIATOR produced skips to step 3:
// its source note already passed the handlers, and sending it round again would let an arpeggiator
// feed itself. A note-sequence trigger is also refused while a generated note is being dispatched,
// so a sequence cannot start itself. Those two rules are what bound the recursion.
internal sealed class DecentSamplerSequencingRuntime
{
    // The tempo a sequence runs at when seqFollowGlobalTempo="false", and the arpeggiator's own
    // default override tempo. Both are the guide's figure and the tempo the reference standalone
    // runs at with no host.
    internal const double FixedBeatsPerMinute = 120.0;

    // seqTrackMidiInputVelocity when the binding does not write one. The guide's note-sequence
    // tutorial gives the default as 1.0.
    internal const double DefaultVelocityTracking = 1.0;

    private readonly IDecentSamplerNoteHost _host;
    private readonly DecentSamplerInstrument _instrument;
    private readonly DecentSamplerSequenceRuntime _sequences;
    private readonly DecentSamplerArpeggiatorRuntime _arpeggiator;
    private readonly List<PendingNoteOff> _pending = [];
    private readonly int _randomSeed;

    private int _generationDepth;

    internal DecentSamplerSequencingRuntime(
        IDecentSamplerNoteHost host, DecentSamplerInstrument instrument, int randomSeed)
    {
        _host = host;
        _instrument = instrument;
        _randomSeed = randomSeed;
        _sequences = new DecentSamplerSequenceRuntime(this, instrument, randomSeed);
        _arpeggiator = new DecentSamplerArpeggiatorRuntime(this, instrument, randomSeed);

        if (instrument.BindingEngine != null)
        {
            // WEAK, for the same reason the all-notes-off subscription is: the binding engine belongs
            // to the INSTRUMENT, which outlives the synthesizers playing it. The last synthesizer
            // built over an instrument is the one its sequence triggers reach - the parameter state a
            // binding writes is shared by every synthesizer on that instrument in any case.
            var weak = new WeakReference<DecentSamplerSequencingRuntime>(this);

            instrument.BindingEngine.SequenceTrigger = (binding, trigger) =>
            {
                if (weak.TryGetTarget(out var runtime))
                {
                    runtime.OnSequenceTrigger(binding, trigger);
                }
            };
        }
    }

    // The synthesizer the generated notes go to.
    internal IDecentSamplerNoteHost Host => _host;

    // The note sequencer.
    internal DecentSamplerSequenceRuntime Sequences => _sequences;

    // The arpeggiator.
    internal DecentSamplerArpeggiatorRuntime Arpeggiator => _arpeggiator;

    // Whether anything here can change what the sampler hears. False for a preset with no <midi>
    // element, no sequences and no arpeggiator, which is most of them.
    internal bool IsActive =>
        _instrument.MidiHandlers.Count > 0 || _sequences.HasSequences || _arpeggiator.IsDeclared;

    // How many notes are waiting for their scheduled release. For the tests.
    internal int PendingNoteOffCount => _pending.Count;

    // An incoming MIDI note-on, before the sampler sees it. Returns true when the note was consumed
    // and must not reach the voice runtime.
    internal bool HandleMidiNoteOn(int channel, int key, int velocity)
    {
        if (_instrument.ProcessNoteOn(channel, key, velocity))
        {
            return true;
        }

        return ConsumeByArpeggiator(channel, key, velocity);
    }

    // An incoming MIDI note-off, before the sampler sees it. Returns true when the note was consumed.
    internal bool HandleMidiNoteOff(int channel, int key, int velocity)
    {
        var swallowed = _instrument.ProcessNoteOff(channel, key, velocity);

        // A <note> handler that only listens for note_on never fires again on the way up, so a
        // midi_key sequence started by this key is stopped here instead.
        _sequences.MidiKeyReleased(channel, key);

        if (_arpeggiator.IsEnabled && _arpeggiator.ReleaseNote(channel, key))
        {
            return true;
        }

        return swallowed;
    }

    // An incoming controller message. The <cc> handlers fire on a CHANGE of the controller's value
    // only, which is measured behaviour of the reference player.
    internal void HandleControlChange(int channel, int controller, int value) =>
        _instrument.ProcessControlChange(channel, controller, value);

    // One render block: the note-offs that have come due, then the sequences, then the arpeggiator.
    // Sequences run first so that a sequence note reaches the arpeggiator's pool in the same block.
    internal void Tick(long blockStartFrame, int blockSize)
    {
        if (!IsActive)
        {
            return;
        }

        DispatchDueNoteOffs(blockStartFrame, blockSize);
        _sequences.Tick(blockStartFrame, blockSize, _host.SampleRate, _host.Tempo);
        _arpeggiator.Tick(blockStartFrame, blockSize, _host.SampleRate, _host.Tempo);
    }

    // Everything stops: an ALL_NOTES_OFF binding, a panic, or a Reset.
    internal void StopEverything()
    {
        _sequences.StopAll();
        _arpeggiator.Clear();
        _pending.Clear();
    }

    // One channel goes quiet: an All Notes Off or All Sound Off on that channel alone. The
    // arpeggiator drops the keys it was holding there, and any sequence that channel started stops.
    internal void StopChannel(int channel)
    {
        _sequences.ChannelReleased(channel);
        _arpeggiator.ClearChannel(channel);

        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            if (_pending[index].Channel == channel)
            {
                _pending.RemoveAt(index);
            }
        }
    }

    // Back to the state a fresh synthesizer is in.
    internal void Reset()
    {
        _sequences.Clear();
        _arpeggiator.Clear();
        _arpeggiator.Reseed(_randomSeed);
        _pending.Clear();
        _generationDepth = 0;
    }

    // A note the sequencer produced. It takes the whole note path from the top, so it can work a key
    // switch and can feed the arpeggiator.
    internal void EmitFromSequencer(int channel, int key, int velocity, int frameOffset)
    {
        _generationDepth++;

        try
        {
            if (_instrument.ProcessNoteOn(channel, key, velocity))
            {
                return;
            }

            if (ConsumeByArpeggiator(channel, key, velocity))
            {
                return;
            }

            _host.StartNote(channel, key, velocity, frameOffset);
        }
        finally
        {
            _generationDepth--;
        }
    }

    // A note the arpeggiator produced. It goes straight into the voice runtime: its source note has
    // already been through the handlers, and feeding it back in would let the arpeggiator arpeggiate
    // its own output.
    internal void EmitFromArpeggiator(int channel, int key, int velocity, int frameOffset) =>
        _host.StartNote(channel, key, velocity, frameOffset);

    // Books a note to be released at an absolute frame. The release lands at the start of the block
    // that frame falls in, and never in the same block as its own note-on, so that a gate shorter
    // than one block still sounds.
    //
    // arpeggiatorAware is true for a SEQUENCER note, which the arpeggiator may have taken into its
    // held pool and which therefore has to leave that pool rather than release a voice; false for the
    // arpeggiator's own notes, which are voices, and taking those out of the pool would end the chord.
    internal void ScheduleNoteOff(object owner, int channel, int key, long frame, bool arpeggiatorAware)
    {
        _pending.Add(new PendingNoteOff
        {
            Owner = owner,
            Channel = channel,
            Key = key,
            Frame = frame,
            ArpeggiatorAware = arpeggiatorAware,
        });
    }

    // Releases everything one generator has sounding, at once. That is what stopping a sequence
    // player, or disarming the arpeggiator, does to its ringing notes.
    internal void ReleaseNotesOf(object owner)
    {
        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(_pending[index].Owner, owner))
            {
                continue;
            }

            var note = _pending[index];
            _pending.RemoveAt(index);
            ReleaseNote(note.Channel, note.Key, note.ArpeggiatorAware);
        }
    }

    // Hands a note to the arpeggiator when it is armed. Reading IsEnabled here, after the handlers
    // have run, is what lets a key switch arm the arpeggiator with the very note that arms it.
    private bool ConsumeByArpeggiator(int channel, int key, int velocity)
    {
        if (!_arpeggiator.IsEnabled)
        {
            return false;
        }

        _arpeggiator.HoldNote(channel, key, velocity);
        return true;
    }

    private void DispatchDueNoteOffs(long blockStartFrame, int blockSize)
    {
        var limit = blockStartFrame + blockSize;

        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            if (_pending[index].Frame >= limit)
            {
                continue;
            }

            var note = _pending[index];
            _pending.RemoveAt(index);
            ReleaseNote(note.Channel, note.Key, note.ArpeggiatorAware);
        }
    }

    // A generated note coming back off. It leaves the arpeggiator's pool when the arpeggiator is
    // holding it, and otherwise releases the voices playing it.
    private void ReleaseNote(int channel, int key, bool arpeggiatorAware)
    {
        if (arpeggiatorAware && _arpeggiator.IsEnabled && _arpeggiator.ReleaseNote(channel, key))
        {
            return;
        }

        _host.StopNote(channel, key);
    }

    // A <binding type="note_sequence"> with no `parameter` fired. Refused while a generated note is
    // in flight, which is what stops a sequence from starting itself.
    private void OnSequenceTrigger(
        DecentSamplerBinding binding, DecentSamplerSequenceTrigger trigger)
    {
        if (_generationDepth > 0)
        {
            return;
        }

        _sequences.Trigger(binding, trigger);
    }

    private struct PendingNoteOff
    {
        public object Owner;
        public int Channel;
        public int Key;
        public long Frame;
        public bool ArpeggiatorAware;
    }
}

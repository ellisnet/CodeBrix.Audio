namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// What fired a note-sequence binding: a MIDI note going down or coming up, or a control being moved.
// A note-sequence binding with seqTriggerBehavior="midi_key" needs the key and its velocity, because
// seqTransposeWithRootNote measures against the key and seqTrackMidiInputVelocity against its
// velocity; a binding under a button knows neither.
internal readonly struct DecentSamplerSequenceTrigger
{
    // A trigger that did not come from a MIDI note - a knob, a button state, a menu option.
    internal static readonly DecentSamplerSequenceTrigger Control =
        new DecentSamplerSequenceTrigger(-1, -1, 0, false, false);

    internal DecentSamplerSequenceTrigger(
        int channel, int key, int velocity, bool isFromNote, bool isNoteOn)
    {
        Channel = channel;
        Key = key;
        Velocity = velocity;
        IsFromNote = isFromNote;
        IsNoteOn = isNoteOn;
    }

    // The MIDI channel the note arrived on, or -1.
    internal int Channel { get; }

    // The MIDI note that fired the handler, or -1.
    internal int Key { get; }

    // The velocity of that note, 0 when there was none.
    internal int Velocity { get; }

    // Whether a <midi><note> handler fired the binding.
    internal bool IsFromNote { get; }

    // Whether the note was going down. False for a note-off and for a control.
    internal bool IsNoteOn { get; }

    // A note-on trigger from a <midi><note> handler.
    internal static DecentSamplerSequenceTrigger NoteOn(int channel, int key, int velocity) =>
        new DecentSamplerSequenceTrigger(channel, key, velocity, true, true);

    // A note-off trigger from a <midi><note> handler.
    internal static DecentSamplerSequenceTrigger NoteOff(int channel, int key) =>
        new DecentSamplerSequenceTrigger(channel, key, 0, true, false);
}

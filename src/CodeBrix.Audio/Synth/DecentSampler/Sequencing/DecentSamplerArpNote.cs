using System.Globalization;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// One note in the arpeggiator's world: a key that is being held, or one of its octave copies. The
// CHANNEL travels with the note so that an MPE performance keeps every note on the member channel it
// arrived on, which is what carries that note's own bend, pressure and timbre.
internal readonly struct DecentSamplerArpNote
{
    internal DecentSamplerArpNote(int channel, int key, int velocity, int pressOrder)
    {
        Channel = channel;
        Key = key;
        Velocity = velocity;
        PressOrder = pressOrder;
    }

    // The MIDI channel the originating note arrived on.
    internal int Channel { get; }

    // The MIDI note number, already octave-shifted for a copy.
    internal int Key { get; }

    // The velocity of the originating note-on.
    internal int Velocity { get; }

    // Where the originating note sits in the order the keys were pressed, which arpOrder="as_played"
    // replays and every other order ignores.
    internal int PressOrder { get; }

    // The same note an octave or more up.
    internal DecentSamplerArpNote Transposed(int semitones) =>
        new DecentSamplerArpNote(Channel, Key + semitones, Velocity, PressOrder);

    public override string ToString() => Key.ToString(CultureInfo.InvariantCulture);
}

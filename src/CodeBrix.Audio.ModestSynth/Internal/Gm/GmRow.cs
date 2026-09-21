using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// ONE ROW OF THE BANK: which family template the voicing starts from, and the handful of numbers
// that make it that program rather than its neighbour.
//
// EVERY FIELD IS OPTIONAL. A number left at "not a number" and an enum left at its zero mean "keep
// what the template said", so a row states only what it changes and reads as a list of differences.
// That is what makes retuning a voicing after a listening session a one-line edit (plan D12, D31),
// and it is why this type is INTERNAL and stays internal (D49): the row format is free to grow a
// field in a data-only republish because no consumer can see it.
internal sealed class GmRow
{
    // What this voicing is called, for diagnostics. The bank fills it in from the General MIDI name
    // when a row does not say.
    internal string Name;

    // The family spine to start from. Not set means the program's own family, which is the answer
    // for 120 of the 128; the handful that borrow another family's spine say so (Orchestra Hit is
    // percussive, Guitar harmonics is a bell).
    internal GeneralMidiProgramFamily? Template;

    // ---------------------------------------------------------------- the main layer

    internal GmTone Tone = GmTone.None;

    internal double Shape = double.NaN;

    internal double Ring = double.NaN;

    internal double Transpose = double.NaN;

    internal double Fine = double.NaN;

    internal double MainLevel = double.NaN;

    // A stereo unison on the main layer: 2 or 3 detuned copies spread across the field. It multiplies
    // the layer's cost, so the bank uses it on the programs that are defined by it and nowhere else.
    internal int Unison;

    internal double Detune = double.NaN;

    internal double Spread = double.NaN;

    internal GmLayerRow Layer2;

    internal GmLayerRow Layer3;

    // ------------------------------------------------------------- the amplitude envelope

    internal double Delay = double.NaN;

    internal double Attack = double.NaN;

    internal double Hold = double.NaN;

    internal double Decay = double.NaN;

    internal double Sustain = double.NaN;

    internal double Release = double.NaN;

    internal bool LinearAttack;

    // ------------------------------------------------------------------------ the filter

    internal GmFilterMode Filter = GmFilterMode.Off;

    // Set the mode to Off deliberately rather than by omission.
    internal bool FilterOff;

    internal double Cutoff = double.NaN;

    internal double Resonance = double.NaN;

    internal double KeyTracking = double.NaN;

    internal double VelocityOctaves = double.NaN;

    internal double FilterEnvelope = double.NaN;

    internal double FilterAttack = double.NaN;

    internal double FilterDecay = double.NaN;

    internal double FilterSustain = double.NaN;

    internal double FilterRelease = double.NaN;

    // ------------------------------------------------------------------- the modulation

    internal double VibratoRate = double.NaN;

    internal double VibratoDepth = double.NaN;

    internal double VibratoDelay = double.NaN;

    internal double VibratoFade = double.NaN;

    internal double Tremolo = double.NaN;

    internal double FilterLfo = double.NaN;

    internal double PitchEnvelope = double.NaN;

    internal double PitchEnvelopeTime = double.NaN;

    // ------------------------------------------------------------------ level and place

    internal double Level = double.NaN;

    internal double Pan = double.NaN;

    internal double VelocityToLevel = double.NaN;

    internal double VelocityToAttack = double.NaN;

    internal double KeyToTime = double.NaN;

    internal double Reverb = double.NaN;

    internal double Chorus = double.NaN;

    internal GmInsertSpec Insert;

    // --------------------------------------------------------------------- percussion

    internal double FixedKey = double.NaN;

    internal int ExclusiveGroup;

    internal bool IgnoreNoteOff;
}

using System;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// ONE VOICING: everything the General MIDI synthesizer needs to sound one program, or one note of
// the percussion kit. This is what a bank row is turned into, and what a voice is started from.
//
// IT IS INTERNAL AND IT STAYS INTERNAL (plan D49). A later phase retunes the bank after a listening
// session and that republish must be DATA ONLY; if this shape were public API, a retune that wanted
// one more parameter would be a breaking change. The consumer's knobs are the public
// GeneralMidiAdjustment, which is applied ON TOP of a spec and does not care what is in it.
internal sealed class GmVoiceSpec
{
    // Three components is as far as the bank goes. More is possible and none of the 175 voicings
    // needed it; the cap keeps the per-voice arrays a fixed size.
    internal const int MaximumLayers = 3;

    // Three layers at a stereo unison of three. Nothing in the bank comes close, and the cap is what
    // lets a voice size its arrays once.
    internal const int MaximumOscillators = 6;

    // What this voicing is, for diagnostics and for the coverage tests.
    internal string Name;

    internal GmLayerSpec[] Layers = [];

    internal GmEnvelopeSpec Amplitude = new GmEnvelopeSpec();

    internal GmFilterSpec Filter = new GmFilterSpec();

    internal GmLfoSpec Lfo = new GmLfoSpec();

    // A pitch envelope: how many semitones the note starts above (or below) where it settles, and
    // how long it takes to get there. A kick drops an octave and a half in forty milliseconds; a
    // brass note scoops up a semitone; a Synth Drum is nothing but this.
    internal double PitchEnvelopeSemitones;

    internal double PitchEnvelopeSeconds = 0.05;

    // The voicing's own level, which is how the bank keeps 128 programs at a comparable loudness.
    internal double Level = 1.0;

    internal double Pan;

    // How much of the loudness follows the velocity, 0 to 1 - the same law the rest of the package
    // uses.
    internal double VelocityToLevel = 0.85;

    // How much a hard note shortens the attack, 0 (not at all) to 1 (a tenth of the time at full
    // velocity).
    internal double VelocityToAttack;

    // How much faster a high note runs its envelopes. 1.0 halves every stage per octave above
    // middle C, which is roughly what a piano or a marimba does; 0 leaves every key the same.
    internal double KeyToTime;

    // What the program asks the reverb and chorus buses for when a channel has not said otherwise.
    // The General MIDI defaults are 40 and 0 out of 127; a row raises or lowers them per program.
    internal double ReverbSend = 40.0 / 127.0;

    internal double ChorusSend;

    // Percussion: a one-shot runs to its natural end and note-off means nothing to it.
    internal bool IgnoreNoteOff;

    // Percussion: notes in the same non-zero group cut each other off - the closed hi-hat silencing
    // the open one is the rule everybody notices when it is missing.
    internal int ExclusiveGroup;

    // A voicing pinned to one pitch: every percussion note is, and the key that played it only
    // chose which voicing to use. Not a number when the voicing follows the keyboard.
    internal double FixedKey = double.NaN;

    internal GmInsertSpec Insert;

    internal bool IsFixedPitch => !double.IsNaN(FixedKey);

    internal int OscillatorCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Layers.Length; i++) { total += Layers[i].OscillatorCount; }
            return total;
        }
    }

    // True when anything in the voicing places sound off centre, which is what decides whether the
    // voice runs one filter over a mono sum or two over a stereo pair.
    internal bool HasStereoContent
    {
        get
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].Pan != 0.0) { return true; }
                if (Layers[i].OscillatorCount > 1 && Layers[i].UnisonSpread != 0.0) { return true; }
            }

            return false;
        }
    }

    internal GmVoiceSpec Clone()
    {
        GmLayerSpec[] layers = new GmLayerSpec[Layers.Length];
        for (int i = 0; i < Layers.Length; i++) { layers[i] = Layers[i].Clone(); }

        return new GmVoiceSpec
        {
            Name = Name,
            Layers = layers,
            Amplitude = Amplitude.Clone(),
            Filter = Filter.Clone(),
            Lfo = Lfo.Clone(),
            PitchEnvelopeSemitones = PitchEnvelopeSemitones,
            PitchEnvelopeSeconds = PitchEnvelopeSeconds,
            Level = Level,
            Pan = Pan,
            VelocityToLevel = VelocityToLevel,
            VelocityToAttack = VelocityToAttack,
            KeyToTime = KeyToTime,
            ReverbSend = ReverbSend,
            ChorusSend = ChorusSend,
            IgnoreNoteOff = IgnoreNoteOff,
            ExclusiveGroup = ExclusiveGroup,
            FixedKey = FixedKey,
            Insert = Insert == null ? null : Insert.Clone(),
        };
    }
}

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The public GeneralMidiAdjustment, resolved into the plain numbers a voice starts with. It is a
// struct so that reading the adjustment for a program on the MIDI path allocates nothing.
//
// Everything here is applied ON TOP of a GmVoiceSpec and never changes one, which is the whole point
// of D49: the bank's rows may be retuned in a data-only republish without the consumer's knobs
// moving underneath them.
internal readonly struct GmVoiceAdjustment
{
    internal static readonly GmVoiceAdjustment None = new GmVoiceAdjustment(1.0, 0.0, 1.0, 1.0, 1.0, double.NaN, 0.0);

    internal GmVoiceAdjustment(
        double level,
        double brightnessOctaves,
        double attackScale,
        double releaseScale,
        double vibratoScale,
        double reverbSend,
        double pan)
    {
        Level = level;
        BrightnessOctaves = brightnessOctaves;
        AttackScale = attackScale;
        ReleaseScale = releaseScale;
        VibratoScale = vibratoScale;
        ReverbSend = reverbSend;
        Pan = pan;
    }

    internal double Level { get; }

    internal double BrightnessOctaves { get; }

    internal double AttackScale { get; }

    internal double ReleaseScale { get; }

    internal double VibratoScale { get; }

    // Not a number when the program has not overridden the send.
    internal double ReverbSend { get; }

    // Added to the voicing's own pan, then clamped.
    internal double Pan { get; }
}

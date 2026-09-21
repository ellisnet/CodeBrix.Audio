namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The voice's one low-frequency oscillator, as a bank row states it.
//
// It has a DELAYED ONSET and a fade, because that is what makes synthesized vibrato sound played
// rather than switched on: a singer, a violinist and a flautist all start a note straight and bring
// the vibrato in over the first part of it.
internal sealed class GmLfoSpec
{
    internal double Rate = 5.0;

    // How long after note-on the modulation stays at zero.
    internal double Delay = 0.3;

    // How long it then takes to reach full depth.
    internal double FadeIn = 0.4;

    // Vibrato, in cents either side of the note. CC1 ADDS to this, so a program with none still
    // answers the modulation wheel.
    internal double ToPitchCents;

    // Tremolo: how much of the level the modulation takes away at its trough, 0 to 1. A vibraphone
    // is the reason this exists.
    internal double ToLevel;

    // How many octaves the modulation moves the filter cutoff either side of where it sits.
    internal double ToCutoffOctaves;

    internal GmLfoSpec Clone() =>
        new GmLfoSpec
        {
            Rate = Rate,
            Delay = Delay,
            FadeIn = FadeIn,
            ToPitchCents = ToPitchCents,
            ToLevel = ToLevel,
            ToCutoffOctaves = ToCutoffOctaves,
        };
}

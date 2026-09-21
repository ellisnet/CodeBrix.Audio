namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The oscillator recipes the bank is written in. A row names one of these per layer instead of
// spelling out a whole ModestPatch, which is what keeps 175 voicings readable - and it is where a
// change that should reach every program at once is made.
//
// Each recipe takes two general-purpose knobs, SHAPE and RING, and reads them the way that recipe
// finds useful (GmTones says which). That is enough to differentiate the programs inside a family
// without inventing a parameter per recipe.
internal enum GmTone
{
    // A layer that is not there.
    None = 0,

    Sine,
    Triangle,
    Saw,
    Square,
    Noise,

    // The waveguide string: shape is the pick (mellow to bright), ring is how long it holds on.
    Pluck,

    // The formant tone - one fixed resonance near 2.4 kHz over the note's own fundamental.
    Formant,

    // A SUNG VOWEL: a glottal source with breath in it, through two or three resonances that stay
    // where they are in Hertz while the note moves. Shape is the vowel, from a closed "oo" at 0 to
    // an open "ah" at 1; ring is how much air goes with the tone. See GmChoirSpec.
    ChoirVowel,

    // Additive stacks. Shape tilts them darker (0) or brighter (1).
    HarmonicOrgan,
    HarmonicOrganFull,
    HarmonicOdd,
    HarmonicSoft,
    HarmonicBright,
    HarmonicBell,
    HarmonicReed,

    // The multi-frame wavetable player. Shape is the position in the table, from hollow to full.
    WavetablePad,
    WavetableVox,

    // Six-operator FM. Shape moves the modulation index - the single most useful brightness control
    // there is - and ring stretches the modulator's own decay.
    FmEPiano,
    FmBell,
    FmBrass,
    FmBass,
    FmClav,
    FmWood,
    FmMetal,
    FmReed,
    FmString,
    FmPiano,
    FmVoice,
    FmGlass,
}

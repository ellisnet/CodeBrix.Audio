namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// Which output of the state-variable filter a voice listens to.
internal enum GmFilterMode
{
    // No filter at all, and no cost: an organ, a pure sine or an additive stack that is already
    // exactly the spectrum it wants to be.
    Off = 0,

    // The workhorse. Everything struck, plucked, bowed or blown loses its top as it decays.
    LowPass = 1,

    // Cymbals, hi-hats, shakers, breath noise - noise with its body taken out.
    HighPass = 2,

    // A narrow band: the cheapest way to put a resonance somewhere, which is what a woodblock, a
    // claves and a tuned drum shell all are.
    BandPass = 3,
}

using System;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// HOW ONE SINGER IS BUILT. Everything GmChoirOscillator needs to sing one vowel: where the formants
// sit, how far the voice wanders off the note, how much air goes with the tone, and how the top is
// shaped.
//
// WHY A VOICE IS NOT AN ORGAN. An organ pipe's spectrum is a fixed set of HARMONIC levels, so the
// whole spectrum slides up and down with the note. A sung vowel is the opposite: the throat and the
// mouth are a filter that does not move when the pitch does, so the same two or three resonances -
// the FORMANTS - stay where they are while the harmonics run up and down through them. Fixing them
// in HERTZ rather than in harmonic numbers is the single thing that makes the difference audible.
//
// The frequencies below are the ordinary published values for a sung "ah" and a sung "oo", taken at
// two register anchors and interpolated with the key, because a bass's mouth is longer than a
// soprano's and a mixed chorus is what this program is for. They move by about a fifth of their own
// value from the bottom of the keyboard to the top, while the pitch moves by a factor of sixteen.
internal sealed class GmChoirSpec
{
    // How many resonances one singer is built from. THE FIRST THREE ARE THE VOWEL: the lowest two
    // are what a listener hears as "ah" rather than "oo", and the third is the ring that makes a
    // sung tone carry across an orchestra. The last two are the HIGHER POLES every throat has
    // whatever it is saying - they are the same for both vowels here - and they are in because
    // three resonances on their own fall away at thirty-six decibels an octave above the third,
    // which is far darker than any voice: measured, it left nothing at all above three kilohertz.
    internal const int FormantCount = 5;

    // How many of them name the vowel.
    internal const int VowelFormantCount = 3;

    // The keys the two register anchors were written for - C2 and C6.
    internal const double LowRegisterKey = 36.0;

    internal const double HighRegisterKey = 84.0;

    // The open "ah" of "father", the vowel a choir holds. Bass, then soprano.
    private static readonly double[] AahLow = [620.0, 1060.0, 2450.0, 3300.0, 4150.0];
    private static readonly double[] AahHigh = [820.0, 1260.0, 2900.0, 3650.0, 4500.0];
    private static readonly double[] AahBandwidth = [95.0, 115.0, 200.0, 300.0, 380.0];

    // The closed "oo" of "moon" - the same throat with the mouth nearly shut, which drops both of
    // the vowel-naming formants a long way and leaves the ring where it is.
    private static readonly double[] OohLow = [330.0, 720.0, 2350.0, 3300.0, 4150.0];
    private static readonly double[] OohHigh = [420.0, 900.0, 2700.0, 3650.0, 4500.0];
    private static readonly double[] OohBandwidth = [80.0, 110.0, 200.0, 300.0, 380.0];

    // The three formants at the bottom of the keyboard and at the top, in Hz, and how wide each
    // resonance is. A wider resonance is a less pinched vowel and a section rather than a soloist.
    internal double[] LowRegisterHz = AahLow;

    internal double[] HighRegisterHz = AahHigh;

    internal double[] BandwidthHz = AahBandwidth;

    // How far this singer's pitch wanders, in cents either way. It is a SLOW wander - two sine
    // components under a hertz - and it is what stops a chord of voices sounding like one voice
    // three times as loud.
    internal double DriftCents = 8.0;

    // This singer's OWN vibrato, on top of whatever the voicing's low-frequency oscillator does.
    // The rate, the depth and the moment it starts are all drawn from the singer's seed, inside the
    // spread named here, because no two people in a section breathe together.
    internal double VibratoCents = 7.0;

    internal double VibratoRateHz = 5.0;

    internal double VibratoOnsetSeconds = 0.55;

    internal double VibratoFadeSeconds = 0.9;

    // How much of each singer's spread the seed may draw, as a fraction either side of the numbers
    // above: 0.25 means a rate anywhere from three quarters to five quarters of VibratoRateHz.
    internal double VibratoRateSpread = 0.14;

    internal double VibratoDepthSpread = 0.45;

    internal double VibratoOnsetSpread = 0.5;

    // THE BREATH. Noise added to the source BEFORE the formants, so it is shaped by the same throat
    // the tone is - which is what aspiration actually is. It is strongest as the note starts and
    // settles to a little in the sustain; a voice with none at all is an organ with a vowel on it.
    internal double BreathAtOnset = 0.5;

    internal double BreathInSustain = 0.06;

    internal double BreathSeconds = 0.3;

    // A first-order lift above PresenceHz, for the last of the brightness a cascade of resonances
    // does not give by itself.
    internal double Presence = 0.7;

    internal double PresenceHz = 1900.0;

    // THE NARROWEST A RESONANCE MAY BE, as a fraction of the note's own pitch. A resonance narrower
    // than the gap between two harmonics is not heard as a resonance at all: whether it lands on a
    // harmonic or between two of them then decides how loud the note is, and measured that way one
    // note came out twelve decibels under its neighbour. A section of several people is broader
    // than one person anyway, so widening it is both the fix and the truth.
    internal double NarrowestBandwidthOfPitch = 0.55;

    // A DELIBERATE tilt with pitch, on top of the level the oscillator works out for itself: each
    // 0.1 is about a third of a decibel an octave, positive making the top of the range quieter.
    // Zero leaves a bass and a soprano at the level the arithmetic says they are.
    internal double PitchTilt;

    // The voicing's own gain, before the bank's Level.
    internal double Gain = 2.2;

    // Turns a row's recipe and its two knobs into one singer's settings. SHAPE is the vowel, from a
    // closed "oo" at 0 to an open "ah" at 1; RING is how much breath goes with the tone, 1 being
    // the ordinary amount. Anything that is not a choir recipe has no settings and gets null.
    internal static GmChoirSpec For(GmTone tone, double shape, double ring)
    {
        if (tone != GmTone.ChoirVowel) { return null; }

        double vowel = double.IsNaN(shape) ? 1.0 : shape < 0.0 ? 0.0 : shape > 1.0 ? 1.0 : shape;
        double breath = double.IsNaN(ring) ? 1.0 : ring < 0.0 ? 0.0 : ring > 4.0 ? 4.0 : ring;

        GmChoirSpec spec = new GmChoirSpec
        {
            LowRegisterHz = Blend(OohLow, AahLow, vowel),
            HighRegisterHz = Blend(OohHigh, AahHigh, vowel),
            BandwidthHz = Blend(OohBandwidth, AahBandwidth, vowel),
        };

        spec.BreathAtOnset *= breath;
        spec.BreathInSustain *= breath;

        return spec;
    }

    internal GmChoirSpec Clone() =>
        new GmChoirSpec
        {
            LowRegisterHz = (double[])LowRegisterHz.Clone(),
            HighRegisterHz = (double[])HighRegisterHz.Clone(),
            BandwidthHz = (double[])BandwidthHz.Clone(),
            DriftCents = DriftCents,
            VibratoCents = VibratoCents,
            VibratoRateHz = VibratoRateHz,
            VibratoOnsetSeconds = VibratoOnsetSeconds,
            VibratoFadeSeconds = VibratoFadeSeconds,
            VibratoRateSpread = VibratoRateSpread,
            VibratoDepthSpread = VibratoDepthSpread,
            VibratoOnsetSpread = VibratoOnsetSpread,
            BreathAtOnset = BreathAtOnset,
            BreathInSustain = BreathInSustain,
            BreathSeconds = BreathSeconds,
            Presence = Presence,
            PresenceHz = PresenceHz,
            NarrowestBandwidthOfPitch = NarrowestBandwidthOfPitch,
            PitchTilt = PitchTilt,
            Gain = Gain,
        };

    // The widest the pitch of one singer can ever be from the note it was given, in cents: the
    // wander plus the deepest vibrato the seed can draw. Stated here so a test can assert it rather
    // than guess it.
    internal double WidestDetuneCents =>
        DriftCents + (VibratoCents * (1.0 + VibratoDepthSpread));

    private static double[] Blend(double[] closed, double[] open, double vowel)
    {
        double[] blended = new double[FormantCount];

        for (int i = 0; i < FormantCount; i++)
        {
            blended[i] = closed[i] + (vowel * (open[i] - closed[i]));
        }

        return blended;
    }
}

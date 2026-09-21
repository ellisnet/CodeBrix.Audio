using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// THE KIT: one row per General MIDI percussion note, all 47 of them, from 35 to 81.
//
// A percussion note is not a pitch. The note number CHOOSES A VOICING and the voicing states its own
// pitch, so every row here carries a FixedKey; note-off means nothing (the spine sets IgnoreNoteOff
// and a one-shot runs to its natural end); and the notes that cannot physically sound together share
// an EXCLUSIVE GROUP, which is why a closed hi-hat silences an open one.
//
// PAN AND LEVEL ARE PART OF THE KIT. A real kit is laid out in front of the listener - toms sweeping
// left to right, hi-hat to one side, the ride to the other, the drums up the middle - and a kit
// rendered flat in the centre is the single most obvious thing missing from a cheap drum machine.
//
// THE EXCLUSIVE GROUPS, which are the ones General MIDI kits have always used:
//   1  the hi-hat: closed (42), pedal (44) and open (46)
//   2  the whistles: short (71) and long (72)
//   3  the guiros: short (73) and long (74)
//   4  the cuicas: mute (78) and open (79)
//   5  the triangles: mute (80) and open (81)
// THE LEVELS ARE MEASURED, NOT GUESSED - but unlike the melodic programs they are deliberately NOT
// all the same. A kit piece is judged by how hard it hits, so each row's Level was set from the PEAK
// of a hit at velocity 100 onto a designed profile: the kicks and snares loudest, the toms just
// under them, the hats and shakers well below. GeneralMidiBankTests fences the spread.
internal static class GmPercussionRows
{
    internal static GmRow Row(int noteNumber)
    {
        switch (noteNumber)
        {
            // ---------------------------------------------------------------- the drums
            case 35: // Acoustic Bass Drum - the bigger, softer of the two
                return new GmRow
                {
                    Tone = GmTone.Sine, FixedKey = 33.0, FilterOff = true,
                    Attack = 0.0006, Decay = 0.4, Sustain = 0.0, Release = 0.06,
                    PitchEnvelope = 13.0, PitchEnvelopeTime = 0.045,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.12,
                        Attack = 0.0005, Decay = 0.012, Sustain = 0.0, Release = 0.01,
                    },
                    Level = 0.9802, Reverb = 0.12,
                };

            case 36: // Bass Drum 1 - tighter, and with more beater in it
                return new GmRow
                {
                    Tone = GmTone.Sine, FixedKey = 35.0, FilterOff = true,
                    Attack = 0.0005, Decay = 0.3, Sustain = 0.0, Release = 0.05,
                    PitchEnvelope = 16.0, PitchEnvelopeTime = 0.032,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.16,
                        Attack = 0.0005, Decay = 0.01, Sustain = 0.0, Release = 0.008,
                    },
                    Level = 0.9584, Reverb = 0.1,
                };

            case 37: // Side Stick - a stick laid across the rim, so a dry wooden crack
                return new GmRow
                {
                    Tone = GmTone.FmWood, Shape = 0.7, Ring = 0.4, FixedKey = 73.0,
                    Filter = GmFilterMode.BandPass, Cutoff = 2400.0, Resonance = 0.55,
                    Attack = 0.0005, Decay = 0.07, Sustain = 0.0, Release = 0.02,
                    Level = 1.179, Pan = -0.12, Reverb = 0.22,
                };

            case 38: // Acoustic Snare - a tuned shell with the wires rattling underneath
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 900.0, Resonance = 0.25,
                    Attack = 0.0005, Decay = 0.2, Sustain = 0.0, Release = 0.04,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Triangle, FixedKey = 55.0, Level = 0.38,
                        Attack = 0.0005, Decay = 0.09, Sustain = 0.0, Release = 0.03,
                    },
                    Layer3 = new GmLayerRow
                    {
                        Tone = GmTone.Triangle, FixedKey = 62.0, Level = 0.22,
                        Attack = 0.0005, Decay = 0.06, Sustain = 0.0, Release = 0.03,
                    },
                    Level = 0.6778, Pan = -0.1, Reverb = 0.3,
                };

            case 39: // Hand Clap - a burst of upper-middle noise with a hard edge
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.BandPass,
                    Cutoff = 1500.0, Resonance = 0.45,
                    Attack = 0.001, Decay = 0.14, Sustain = 0.0, Release = 0.04,
                    Level = 0.7276, Pan = 0.18, Reverb = 0.35,
                };

            case 40: // Electric Snare - shorter, brighter, and with a tighter shell
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 1400.0, Resonance = 0.3,
                    Attack = 0.0005, Decay = 0.15, Sustain = 0.0, Release = 0.03,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Triangle, FixedKey = 60.0, Level = 0.3,
                        Attack = 0.0005, Decay = 0.05, Sustain = 0.0, Release = 0.02,
                    },
                    Level = 0.7951, Pan = -0.1, Reverb = 0.26,
                };

            // ---------------- the toms, swept across the field the way a kit is laid out
            case 41: // Low Floor Tom
                return Tom(38.0, 0.6, -0.45, 0.707);

            case 43: // High Floor Tom
                return Tom(42.0, 0.55, -0.34, 0.7296);

            case 45: // Low Tom
                return Tom(46.0, 0.5, -0.2, 0.7532);

            case 47: // Low-Mid Tom
                return Tom(50.0, 0.46, -0.05, 0.8249);

            case 48: // Hi-Mid Tom
                return Tom(54.0, 0.42, 0.12, 0.7822);

            case 50: // High Tom
                return Tom(58.0, 0.38, 0.28, 0.7742);

            // -------------------------------------------------------------- the hi-hat
            case 42: // Closed Hi Hat - short, tight, and it cuts the open one dead
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 7200.0, Resonance = 0.3,
                    Attack = 0.0005, Decay = 0.045, Sustain = 0.0, Release = 0.012,
                    Level = 0.6241, Pan = 0.36, ExclusiveGroup = 1, Reverb = 0.16,
                };

            case 44: // Pedal Hi-Hat - the foot closing it, which is duller and a little longer
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 5200.0, Resonance = 0.25,
                    Attack = 0.0006, Decay = 0.09, Sustain = 0.0, Release = 0.02,
                    Level = 0.4568, Pan = 0.36, ExclusiveGroup = 1, Reverb = 0.16,
                };

            case 46: // Open Hi-Hat - two cymbals ringing against each other until something stops it
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 6200.0, Resonance = 0.3,
                    Attack = 0.0008, Decay = 0.65, Sustain = 0.0, Release = 0.12,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.FmMetal, Shape = 0.7, Ring = 0.3, FixedKey = 84.0,
                        Level = 0.16, Attack = 0.0008, Decay = 0.5, Sustain = 0.0, Release = 0.1,
                    },
                    Level = 0.6178, Pan = 0.36, ExclusiveGroup = 1, Reverb = 0.24,
                };

            // ------------------------------------------------------------- the cymbals
            case 49: // Crash Cymbal 1
                return Crash(82.0, 2.4, -0.5, 6.0, 0.9771);

            case 57: // Crash Cymbal 2 - the second crash, thinner and on the other side
                return Crash(86.0, 1.9, 0.5, 6.4, 0.6527);

            case 55: // Splash Cymbal - small, fast, and gone
                return Crash(90.0, 0.85, -0.42, 7.2, 1.047);

            case 52: // Chinese Cymbal - trashy, and it does not decay so much as clatter
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 4200.0, Resonance = 0.2,
                    Attack = 0.002, Decay = 1.7, Sustain = 0.0, Release = 0.4,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.FmMetal, Shape = 0.9, Ring = 1.2, FixedKey = 79.0,
                        Level = 0.3, Attack = 0.002, Decay = 1.4, Sustain = 0.0, Release = 0.35,
                    },
                    Level = 0.5202, Pan = -0.56, Reverb = 0.42,
                };

            case 51: // Ride Cymbal 1 - a stick on a big cymbal: a ping over a wash
                return Ride(88.0, 1.6, 0.46, 0.4427);

            case 59: // Ride Cymbal 2
                return Ride(85.0, 1.9, 0.4, 0.479);

            case 53: // Ride Bell - the cup of the ride, which is almost a pitch
                return new GmRow
                {
                    Tone = GmTone.FmMetal, Shape = 0.35, Ring = 0.45, FixedKey = 87.0,
                    Filter = GmFilterMode.BandPass, Cutoff = 3000.0, Resonance = 0.4,
                    Attack = 0.0008, Decay = 1.1, Sustain = 0.0, Release = 0.25,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.1,
                        Attack = 0.0008, Decay = 0.25, Sustain = 0.0, Release = 0.08,
                    },
                    Level = 0.8392, Pan = 0.46, Reverb = 0.35,
                };

            // ------------------------------------------------ shakers, bells and rattles
            case 54: // Tambourine - a skin and a lot of small jingles
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 6800.0, Resonance = 0.4,
                    Attack = 0.0006, Decay = 0.26, Sustain = 0.0, Release = 0.06,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.FmMetal, Shape = 0.8, Ring = 0.2, FixedKey = 95.0,
                        Level = 0.12, Attack = 0.0006, Decay = 0.2, Sustain = 0.0, Release = 0.05,
                    },
                    Level = 0.5933, Pan = 0.24, Reverb = 0.3,
                };

            case 56: // Cowbell - a struck steel box, deliberately inharmonic
                return new GmRow
                {
                    Tone = GmTone.FmMetal, Shape = 0.3, Ring = 0.15, FixedKey = 79.0,
                    Filter = GmFilterMode.BandPass, Cutoff = 2600.0, Resonance = 0.35,
                    Attack = 0.0005, Decay = 0.32, Sustain = 0.0, Release = 0.07,
                    Level = 1.073, Pan = -0.22, Reverb = 0.24,
                };

            case 58: // Vibraslap - a rattle shaken loose, which is noise pulsing as it dies
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.BandPass,
                    Cutoff = 2800.0, Resonance = 0.6,
                    Attack = 0.001, Decay = 0.75, Sustain = 0.0, Release = 0.1,
                    Tremolo = 0.9, VibratoRate = 22.0, VibratoDelay = 0.0, VibratoFade = 0.01,
                    Level = 0.5777, Pan = 0.3, Reverb = 0.3,
                };

            case 69: // Cabasa - beads over metal, brief and very high
                return Shaker(9500.0, 0.065, 0.32, 0.897);

            case 70: // Maracas - the same idea in a gourd, drier and shorter still
                return Shaker(7800.0, 0.05, -0.32, 0.516);

            // ---------------------------------------------------- hand drums and timbales
            case 60: // Hi Bongo
                return HandDrum(76.0, 0.2, 0.32, 3200.0, 0.5743);

            case 61: // Low Bongo
                return HandDrum(70.0, 0.24, 0.32, 2600.0, 0.569);

            case 62: // Mute Hi Conga - a slap with the hand left on the skin
                return HandDrum(67.0, 0.11, -0.28, 2800.0, 0.6117);

            case 63: // Open Hi Conga
                return HandDrum(64.0, 0.38, -0.28, 2000.0, 0.5934);

            case 64: // Low Conga
                return HandDrum(59.0, 0.45, -0.28, 1600.0, 0.6063);

            case 65: // High Timbale - a metal shell, so more ring than a conga
                return new GmRow
                {
                    Tone = GmTone.Triangle, FixedKey = 71.0,
                    Filter = GmFilterMode.LowPass, Cutoff = 3400.0, Resonance = 0.3,
                    Attack = 0.0005, Decay = 0.34, Sustain = 0.0, Release = 0.06,
                    PitchEnvelope = 2.0, PitchEnvelopeTime = 0.04,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.18,
                        Attack = 0.0005, Decay = 0.05, Sustain = 0.0, Release = 0.02,
                    },
                    Level = 0.5662, Pan = 0.26, Reverb = 0.3,
                };

            case 66: // Low Timbale
                return new GmRow
                {
                    Tone = GmTone.Triangle, FixedKey = 65.0,
                    Filter = GmFilterMode.LowPass, Cutoff = 2600.0, Resonance = 0.3,
                    Attack = 0.0005, Decay = 0.4, Sustain = 0.0, Release = 0.07,
                    PitchEnvelope = 2.0, PitchEnvelopeTime = 0.045,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.16,
                        Attack = 0.0005, Decay = 0.06, Sustain = 0.0, Release = 0.02,
                    },
                    Level = 0.5689, Pan = 0.26, Reverb = 0.3,
                };

            case 67: // High Agogo - a small struck bell
                return Agogo(88.0, 0.42, -0.34, 0.9898);

            case 68: // Low Agogo
                return Agogo(83.0, 0.48, -0.34, 0.9184);

            // ----------------------------------------------- whistles, guiros and wood
            case 71: // Short Whistle - a samba whistle, chirped
                return Whistle(0.16, 0.2, 0.4174);

            case 72: // Long Whistle - the same whistle, held
                return Whistle(0.55, 0.2, 0.4198);

            case 73: // Short Guiro - a stick dragged once across the notches
                return Guiro(0.13, -0.24, 0.5306);

            case 74: // Long Guiro - the same drag, taken slowly
                return Guiro(0.5, -0.24, 0.4597);

            case 75: // Claves - two hardwood sticks; the driest sound in the kit
                return Wood(86.0, 0.1, 0.0, 4200.0, 1.394);

            case 76: // Hi Wood Block
                return Wood(81.0, 0.13, 0.22, 3400.0, 1.169);

            case 77: // Low Wood Block
                return Wood(75.0, 0.16, 0.22, 2700.0, 1.118);

            case 78: // Mute Cuica - a rubbed stick inside a drum, stopped short with the thumb
                return new GmRow
                {
                    Tone = GmTone.Triangle, FixedKey = 74.0,
                    Filter = GmFilterMode.BandPass, Cutoff = 1600.0, Resonance = 0.5,
                    Attack = 0.004, Decay = 0.14, Sustain = 0.0, Release = 0.03,
                    PitchEnvelope = 5.0, PitchEnvelopeTime = 0.05,
                    Level = 3.516, Pan = 0.16, ExclusiveGroup = 4, Reverb = 0.25,
                };

            case 79: // Open Cuica - the same rub let go, so the pitch slides up and stays
                return new GmRow
                {
                    Tone = GmTone.Triangle, FixedKey = 69.0,
                    Filter = GmFilterMode.BandPass, Cutoff = 1500.0, Resonance = 0.5,
                    Attack = 0.006, Decay = 0.45, Sustain = 0.0, Release = 0.06,
                    PitchEnvelope = -7.0, PitchEnvelopeTime = 0.12,
                    Level = 8.748, Pan = 0.16, ExclusiveGroup = 4, Reverb = 0.28,
                };

            case 80: // Mute Triangle - struck and immediately held
                return Triangle(0.13, 5, 0.5241);

            case 81: // Open Triangle - struck and left to ring
                return Triangle(1.7, 5, 0.517);

            default:
                // Unreachable: GmBank checks the range before it asks. A plain noise hit rather than
                // silence, because a kit piece that says nothing is the hardest fault to find.
                return new GmRow { Tone = GmTone.Noise, Decay = 0.15, Level = 0.5 };
        }
    }

    // A tom: a tuned skin whose pitch falls as it is struck, plus the stick hitting it.
    private static GmRow Tom(double key, double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.Sine, FixedKey = key,
            Filter = GmFilterMode.LowPass, Cutoff = 2200.0, Resonance = 0.2,
            Attack = 0.0006, Decay = decay, Sustain = 0.0, Release = 0.08,
            PitchEnvelope = 5.0, PitchEnvelopeTime = 0.07,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.Noise, Level = 0.14,
                Attack = 0.0005, Decay = 0.05, Sustain = 0.0, Release = 0.02,
            },
            Level = level, Pan = pan, Reverb = 0.3,
        };

    // A crash: a wash of high noise with inharmonic metal ringing through it.
    private static GmRow Crash(double key, double decay, double pan, double cutoffKilohertz, double level) =>
        new GmRow
        {
            Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
            Cutoff = cutoffKilohertz * 1000.0, Resonance = 0.2,
            Attack = 0.0015, Decay = decay, Sustain = 0.0, Release = 0.35,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.FmMetal, Shape = 0.8, Ring = 1.4, FixedKey = key,
                Level = 0.22, Attack = 0.0015, Decay = decay * 0.8, Sustain = 0.0, Release = 0.3,
            },
            Level = level, Pan = pan, Reverb = 0.45,
        };

    // A ride: a defined stick ping riding on a much quieter wash.
    private static GmRow Ride(double key, double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.FmMetal, Shape = 0.6, Ring = 1.0, FixedKey = key,
            Filter = GmFilterMode.HighPass, Cutoff = 3000.0, Resonance = 0.15,
            Attack = 0.0008, Decay = decay, Sustain = 0.0, Release = 0.3,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.Noise, Level = 0.16,
                Attack = 0.0008, Decay = decay * 0.5, Sustain = 0.0, Release = 0.15,
            },
            Level = level, Pan = pan, Reverb = 0.38,
        };

    // A shaker: very high noise, over almost before it started.
    private static GmRow Shaker(double cutoff, double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
            Cutoff = cutoff, Resonance = 0.3,
            Attack = 0.0008, Decay = decay, Sustain = 0.0, Release = 0.015,
            Level = level, Pan = pan, Reverb = 0.2,
        };

    // A hand drum: a skin with a short pitch drop and the slap of the hand on it.
    private static GmRow HandDrum(double key, double decay, double pan, double cutoff, double level) =>
        new GmRow
        {
            Tone = GmTone.Triangle, FixedKey = key,
            Filter = GmFilterMode.LowPass, Cutoff = cutoff, Resonance = 0.3,
            Attack = 0.0005, Decay = decay, Sustain = 0.0, Release = 0.05,
            PitchEnvelope = 3.0, PitchEnvelopeTime = 0.035,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.Noise, Level = 0.13,
                Attack = 0.0005, Decay = 0.035, Sustain = 0.0, Release = 0.02,
            },
            Level = level, Pan = pan, Reverb = 0.26,
        };

    // An agogo bell: struck metal with a clear pitch to it.
    private static GmRow Agogo(double key, double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.FmMetal, Shape = 0.28, Ring = 0.18, FixedKey = key,
            Filter = GmFilterMode.BandPass, Cutoff = 3200.0, Resonance = 0.35,
            Attack = 0.0005, Decay = decay, Sustain = 0.0, Release = 0.08,
            Level = level, Pan = pan, Reverb = 0.3,
        };

    // A samba whistle: a high tone with air around it and a fast warble.
    private static GmRow Whistle(double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.Sine, FixedKey = 98.0, FilterOff = true,
            Attack = 0.004, Decay = decay, Sustain = 0.0, Release = 0.03,
            VibratoRate = 14.0, VibratoDepth = 45.0, VibratoDelay = 0.0, VibratoFade = 0.02,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.Noise, Level = 0.09,
                Attack = 0.004, Decay = decay, Sustain = 0.0, Release = 0.03,
            },
            Level = level, Pan = pan, ExclusiveGroup = 2, Reverb = 0.3,
        };

    // A guiro: a stick dragged over ridges, which is a band of noise pulsing at the drag rate.
    private static GmRow Guiro(double decay, double pan, double level) =>
        new GmRow
        {
            Tone = GmTone.Noise, Filter = GmFilterMode.BandPass,
            Cutoff = 3400.0, Resonance = 0.6,
            Attack = 0.002, Decay = decay, Sustain = 0.0, Release = 0.04,
            Tremolo = 0.85, VibratoRate = 34.0, VibratoDelay = 0.0, VibratoFade = 0.005,
            Level = level, Pan = pan, ExclusiveGroup = 3, Reverb = 0.26,
        };

    // Struck hardwood: a knock with a resonance and nothing after it.
    private static GmRow Wood(double key, double decay, double pan, double cutoff, double level) =>
        new GmRow
        {
            Tone = GmTone.FmWood, Shape = 0.65, Ring = 0.45, FixedKey = key,
            Filter = GmFilterMode.BandPass, Cutoff = cutoff, Resonance = 0.45,
            Attack = 0.0005, Decay = decay, Sustain = 0.0, Release = 0.02,
            Level = level, Pan = pan, Reverb = 0.22,
        };

    // A triangle: a thin metal bar, either held or left to ring for a very long time.
    private static GmRow Triangle(double decay, int group, double level) =>
        new GmRow
        {
            Tone = GmTone.FmGlass, Shape = 0.85, Ring = 1.3, FixedKey = 96.0,
            Filter = GmFilterMode.HighPass, Cutoff = 2600.0, Resonance = 0.2,
            Attack = 0.0008, Decay = decay, Sustain = 0.0, Release = 0.2,
            Level = level, Pan = -0.18, ExclusiveGroup = group, Reverb = 0.45,
        };
}

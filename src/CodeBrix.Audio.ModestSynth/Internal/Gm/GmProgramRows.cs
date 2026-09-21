using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Effects;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// THE TABLE: one row per General MIDI program, all 128 of them.
//
// A row says which FAMILY SPINE it starts from - by omission, its own family - and then only the
// numbers that make it that program rather than the one next to it. Everything else comes from
// GmFamilyTemplates, so the eight programs of a family agree about how they start, stop, answer
// velocity and ask for reverb without eight copies of those decisions.
//
// HOW TO RETUNE A VOICING (plan D31): find its case below and change a number. Nothing else moves,
// no API changes, and the tests - which iterate all 128 generically - keep fencing it. To retune a
// WHOLE FAMILY, change its spine in GmFamilyTemplates instead.
//
// ORDER OF EFFORT (plan Q3, fact 3.9): the care went first into the families the auditions favoured
// and synthesis does best - pads, bells and the rest of the chromatic percussion, electric pianos,
// organs, plucked strings, choir and voice, string ensembles, synth leads and basses - and then into
// everything else. Solo bowed strings and the concert piano are the weakest and are meant to be.
// THE LEVELS ARE MEASURED, NOT GUESSED. Every row's Level was set so that the loudest hundred
// milliseconds of a held middle C at velocity 100 comes out at the same figure for all 128 programs,
// which is what a General MIDI file assumes when it mixes itself. GeneralMidiBankTests fences the
// band; a retune that changes a voicing's loudness has to move its Level back into it.
//
// THREE ROWS ARE DELIBERATELY BELOW THAT LINE - 004 Electric Piano 1, 027 Electric Guitar (clean)
// and 109 Bag pipe. A held middle C is not how any of the three is heard: each one sustains and
// sums where its family neighbours decay, so on the phrase the auditions actually play it stood
// four to six decibels above the family around it and was the imbalance a listener noticed first.
// Each was trimmed by measurement on THAT phrase - the loudest hundred milliseconds of its own tour
// phrase against the median of its family - and each trim is written beside its Level below. The
// held-note band still holds them; they simply sit at the quiet end of it.
internal static class GmProgramRows
{
    internal static GmRow Row(int program)
    {
        switch (program / GeneralMidi.ProgramsPerFamily)
        {
            case 0: return Piano(program);
            case 1: return ChromaticPercussion(program);
            case 2: return Organ(program);
            case 3: return Guitar(program);
            case 4: return Bass(program);
            case 5: return Strings(program);
            case 6: return Ensemble(program);
            case 7: return Brass(program);
            case 8: return Reed(program);
            case 9: return Pipe(program);
            case 10: return SynthLead(program);
            case 11: return SynthPad(program);
            case 12: return SynthEffects(program);
            case 13: return Ethnic(program);
            case 14: return Percussive(program);
            default: return SoundEffects(program);
        }
    }

    // ============================================================== 0-7  PIANO
    // A struck string is where synthesis is at its weakest (plan D7) and these are an honest best:
    // an FM hammer over an FM body, a filter that closes as the note dies, and a top octave that
    // dies much faster than the bottom one.
    private static GmRow Piano(int program)
    {
        switch (program)
        {
            case 0: // Acoustic Grand Piano
                return new GmRow { Tone = GmTone.FmPiano, Shape = 0.5, Ring = 0.5, Cutoff = 4000.0, Decay = 6.5, Level = 0.3812 };

            case 1: // Bright Acoustic Piano
                return new GmRow
                {
                    Tone = GmTone.FmPiano, Shape = 0.75, Ring = 0.55,
                    Cutoff = 5800.0, Decay = 5.5, VelocityOctaves = 1.6, Level = 0.377,
                };

            case 2: // Electric Grand Piano
                return new GmRow
                {
                    Tone = GmTone.FmPiano, Shape = 0.6, Cutoff = 4800.0, Decay = 5.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.FmEPiano, Level = 0.35, Decay = 3.0 },
                    Chorus = 0.2, Level = 0.3255,
                };

            case 3: // Honky-tonk Piano - two strings that were never quite tuned to each other
                return new GmRow
                {
                    Tone = GmTone.FmPiano, Shape = 0.62, Cutoff = 4600.0, Decay = 4.5,
                    Unison = 2, Detune = 17.0, Spread = 0.22, MainLevel = 0.72, Level = 0.4064,
                };

            case 4: // Electric Piano 1 - the tine piano, with the gentle phaser that always sat on it
                return new GmRow
                {
                    Tone = GmTone.FmEPiano, Shape = 0.5, Ring = 0.8,
                    // Level trimmed 1.7 dB from 0.7802: it stood at +4.4 dB on the piano family's
                    // own phrase, where the rest of the family sits around +2.7 dB.
                    Decay = 4.2, Release = 0.35, Cutoff = 4200.0, Level = 0.6415,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.Phaser,
                        Parameter1 = "modrate", Value1 = 0.35,
                        Parameter2 = "moddepth", Value2 = 0.55,
                        Parameter3 = "mix", Value3 = 0.32,
                        Parameter4 = "feedback", Value4 = 0.25,
                    },
                };

            case 5: // Electric Piano 2 - the harder, brighter FM piano
                return new GmRow
                {
                    Tone = GmTone.FmEPiano, Shape = 0.9, Ring = 0.55,
                    Decay = 3.2, Cutoff = 6000.0, Level = 0.4715, Chorus = 0.35,
                };

            case 6: // Harpsichord - plucked, and famously indifferent to how hard the key is struck
                return new GmRow
                {
                    Tone = GmTone.FmClav, Shape = 0.8, Ring = 0.6,
                    Decay = 2.2, Release = 0.1, Cutoff = 5200.0, Resonance = 0.2,
                    VelocityToLevel = 0.15, VelocityOctaves = 0.3, Level = 0.426,
                    Layer2 = new GmLayerRow { Tone = GmTone.Pluck, Shape = 0.7, Ring = 0.5, Level = 0.3, Decay = 1.4 },
                    Reverb = 0.4,
                };

            case 7: // Clavi - the same pluck with a resonant filter and a hard velocity response
                return new GmRow
                {
                    Tone = GmTone.FmClav, Shape = 1.0, Ring = 0.4,
                    Decay = 1.4, Release = 0.08, Cutoff = 2400.0, Resonance = 0.45,
                    FilterEnvelope = 2.0, FilterDecay = 0.5, FilterSustain = 0.2,
                    VelocityOctaves = 1.8, Level = 0.5566,
                };

            default:
                return new GmRow();
        }
    }

    // ============================== 8-15  CHROMATIC PERCUSSION
    // One of the families the auditions rated highest and one synthesis is unambiguously good at:
    // struck metal and struck wood are inharmonic partials over an exponential decay, which is
    // exactly what FM and an additive stack make.
    private static GmRow ChromaticPercussion(int program)
    {
        switch (program)
        {
            case 8: // Celesta - small hammers on small bars; sweet, short, and high
                return new GmRow
                {
                    Tone = GmTone.FmGlass, Shape = 0.4, Ring = 0.45,
                    Decay = 1.9, Release = 0.3, Cutoff = 7000.0,
                    Level = 0.5149, KeyToTime = 0.75, Reverb = 0.52,
                };

            case 9: // Glockenspiel - harder and brighter than the celesta, and shorter still
                return new GmRow
                {
                    Tone = GmTone.FmGlass, Shape = 0.62, Ring = 0.4,
                    Decay = 1.5, Cutoff = 9500.0, Level = 0.5355, KeyToTime = 0.9, Reverb = 0.5,
                };

            case 10: // Music Box - a plucked comb tooth, so a fast start and a thin ring
                return new GmRow
                {
                    Tone = GmTone.FmGlass, Shape = 0.32, Ring = 0.35,
                    Decay = 1.5, Cutoff = 6200.0, Level = 0.5126, KeyToTime = 0.85,
                    Layer2 = new GmLayerRow { Tone = GmTone.FmBell, Shape = 0.25, Level = 0.22, Decay = 0.7 },
                    Reverb = 0.5,
                };

            case 11: // Vibraphone - the one instrument whose tremolo IS the instrument
                return new GmRow
                {
                    Tone = GmTone.FmBell, Shape = 0.3, Ring = 0.8,
                    Decay = 3.6, Release = 0.5, Cutoff = 5200.0,
                    VibratoRate = 5.2, VibratoDelay = 0.0, VibratoFade = 0.12, Tremolo = 0.5,
                    Level = 0.5572, KeyToTime = 0.6, Reverb = 0.5,
                };

            case 12: // Marimba - a wooden bar, so the fourth partial sings and the rest goes quickly
                return new GmRow
                {
                    Tone = GmTone.FmBell, Shape = 0.35, Ring = 0.2,
                    Decay = 1.1, Cutoff = 4200.0, Level = 0.5785, KeyToTime = 0.85, Reverb = 0.38,
                };

            case 13: // Xylophone - the marimba's smaller, harder, much shorter relative
                return new GmRow
                {
                    Tone = GmTone.FmWood, Shape = 0.9, Ring = 1.0,
                    Decay = 0.55, Cutoff = 7500.0, Level = 0.8605, KeyToTime = 0.9, Reverb = 0.4,
                };

            case 14: // Tubular Bells - a long metal tube: inharmonic, loud, and slow to die
                return new GmRow
                {
                    Tone = GmTone.FmBell, Shape = 0.38, Ring = 1.4,
                    Attack = 0.002, Decay = 9.0, Release = 2.2, Cutoff = 5000.0,
                    Level = 0.4188, KeyToTime = 0.35, Reverb = 0.6,
                };

            case 15: // Dulcimer - struck strings in courses, which is what the detuning is
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.6, Ring = 0.82,
                    Decay = 2.6, Cutoff = 4400.0, Unison = 2, Detune = 11.0, Spread = 0.3,
                    Level = 1.237, KeyToTime = 0.5, Reverb = 0.45,
                };

            default:
                return new GmRow();
        }
    }

    // ================================================== 16-23  ORGAN
    // Drawbars are an additive stack and nothing else: the registration IS the sound, so these rows
    // are mostly a choice of partials and how hard the key click is.
    private static GmRow Organ(int program)
    {
        switch (program)
        {
            case 16: // Drawbar Organ - the classic 88 8000 000 registration
                return new GmRow { Tone = GmTone.HarmonicOrgan, Shape = 0.5, Level = 0.426 };

            case 17: // Percussive Organ - the same, with the second-harmonic percussion tab down
                return new GmRow
                {
                    Tone = GmTone.HarmonicOrgan, Shape = 0.55,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Sine, Transpose = 24.0, Level = 0.5,
                        Attack = 0.001, Decay = 0.3, Sustain = 0.0, Release = 0.05,
                    }, Level = 0.3849,
                };

            case 18: // Rock Organ - a full registration pushed into a rotary-speaker snarl
                return new GmRow
                {
                    Tone = GmTone.HarmonicOrganFull, Shape = 0.72, Level = 0.4367,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.WaveShaper,
                        Parameter1 = "drive", Value1 = 0.55,
                        Parameter2 = "outputlevel", Value2 = 0.7,
                        Parameter3 = "mix", Value3 = 0.75,
                    },
                    Chorus = 0.3, Reverb = 0.4,
                };

            case 19: // Church Organ - a big room, a slow speech and a full principal chorus
                return new GmRow
                {
                    Tone = GmTone.HarmonicOrganFull, Shape = 0.5,
                    Attack = 0.06, Release = 0.28, Level = 0.513, Reverb = 0.72,
                };

            case 20: // Reed Organ - odd-harmonic, smaller, and softer in the attack
                return new GmRow
                {
                    Tone = GmTone.HarmonicOdd, Shape = 0.5,
                    Attack = 0.03, Release = 0.09, Level = 0.345, Reverb = 0.42,
                };

            case 21: // Accordion - two reeds a few cents apart, which is the whole character
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.5,
                    Attack = 0.02, Release = 0.07, Unison = 2, Detune = 13.0, Spread = 0.3,
                    Level = 0.3487, Filter = GmFilterMode.LowPass, Cutoff = 3000.0, Reverb = 0.35,
                };

            case 22: // Harmonica - a free reed with the player's breath around it
                return new GmRow
                {
                    Tone = GmTone.HarmonicOdd, Shape = 0.62,
                    Attack = 0.025, Release = 0.08, Level = 0.4603,
                    Filter = GmFilterMode.LowPass, Cutoff = 3400.0,
                    VibratoRate = 6.0, VibratoDepth = 14.0, VibratoDelay = 0.25,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.05, Attack = 0.02, Sustain = 0.3 },
                    Reverb = 0.38,
                };

            case 23: // Tango Accordion - the same box, tuned wider and voiced brighter
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.68,
                    Attack = 0.018, Release = 0.07, Unison = 2, Detune = 19.0, Spread = 0.35,
                    Level = 0.5505, Filter = GmFilterMode.LowPass, Cutoff = 3600.0, Reverb = 0.35,
                };

            default:
                return new GmRow();
        }
    }

    // =============================================== 24-31  GUITAR
    // The waveguide string is the best thing in the package and these lean on it hard. The knobs
    // that matter are the pick (shape, mellow to bright) and how long the string holds on (ring).
    private static GmRow Guitar(int program)
    {
        switch (program)
        {
            case 24: // Acoustic Guitar (nylon) - a soft, round pick and a dark body
                return new GmRow { Tone = GmTone.Pluck, Shape = 0.18, Ring = 0.72, Cutoff = 2900.0, Decay = 3.5, Level = 0.9069 };

            case 25: // Acoustic Guitar (steel) - a bright pick, and the plectrum heard hitting it
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.55, Ring = 0.8, Cutoff = 4600.0, Decay = 4.5,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.09,
                        Attack = 0.0005, Decay = 0.025, Sustain = 0.0, Release = 0.02,
                    },
                    Reverb = 0.33, Level = 1.141,
                };

            case 26: // Electric Guitar (jazz) - a neck pickup and the tone control rolled off
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.12, Ring = 0.86,
                    Cutoff = 2100.0, Decay = 4.0, Reverb = 0.3, Level = 0.7833,
                };

            case 27: // Electric Guitar (clean) - bright, chorused, and left alone
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.42, Ring = 0.87,
                    // Level trimmed 3.1 dB from 1.29: it was the loudest thing in the bank at
                    // +5.8 dB on the guitar phrase, 6.7 dB above 030 three programs away, and the
                    // rest of its family sits around +2.7 dB.
                    Cutoff = 3800.0, Decay = 4.5, Chorus = 0.35, Reverb = 0.34, Level = 0.9028,
                };

            case 28: // Electric Guitar (muted) - the palm on the strings, so nothing rings
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.5, Ring = 0.28,
                    Cutoff = 2600.0, Decay = 0.42, Release = 0.05, Level = 2.206, Reverb = 0.18,
                };

            case 29: // Overdriven Guitar - a pushed amplifier, which is a soft clipper
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.62, Ring = 0.93,
                    Cutoff = 3200.0, Decay = 5.0, Level = 1.364,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.WaveShaper,
                        Parameter1 = "drive", Value1 = 0.6,
                        Parameter2 = "outputlevel", Value2 = 0.55,
                        Parameter3 = "mix", Value3 = 0.85,
                    },
                    Reverb = 0.32,
                };

            case 30: // Distortion Guitar - the same amplifier turned up, and sustaining on it
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.72, Ring = 0.96,
                    Cutoff = 3000.0, Decay = 7.0, Level = 1.741,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.WaveShaper,
                        Parameter1 = "drive", Value1 = 0.9,
                        Parameter2 = "driveboost", Value2 = 0.5,
                        Parameter3 = "outputlevel", Value3 = 0.4,
                        Parameter4 = "mix", Value4 = 1.0,
                    },
                    Reverb = 0.34,
                };

            case 31: // Guitar harmonics - a touched node: a thin bell, not a string
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.ChromaticPercussion,
                    Tone = GmTone.FmGlass, Shape = 0.22, Ring = 0.5,
                    Decay = 2.4, Cutoff = 6000.0, Level = 0.4958, Reverb = 0.45,
                };

            default:
                return new GmRow();
        }
    }

    // ================================================== 32-39  BASS
    // Bass carries a mix and must never be washed out: every row here keeps its reverb low, its
    // filter closed and its level up.
    private static GmRow Bass(int program)
    {
        switch (program)
        {
            case 32: // Acoustic Bass - a thick string on a big box, mostly fundamental
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.22, Ring = 0.6,
                    Decay = 1.8, Sustain = 0.0, Cutoff = 700.0, Level = 1.121, Reverb = 0.16,
                };

            case 33: // Electric Bass (finger)
                return new GmRow { Tone = GmTone.FmBass, Shape = 0.5, Ring = 0.6, Cutoff = 1000.0, Level = 0.4431 };

            case 34: // Electric Bass (pick) - the plectrum click over the same string
                return new GmRow
                {
                    Tone = GmTone.FmBass, Shape = 0.8, Ring = 0.5, Cutoff = 1500.0,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.07,
                        Attack = 0.0005, Decay = 0.02, Sustain = 0.0, Release = 0.02,
                    }, Level = 0.5589,
                };

            case 35: // Fretless Bass - a singing, sliding tone with a slow vibrato on it
                return new GmRow
                {
                    Tone = GmTone.FmBass, Shape = 0.35, Ring = 0.8,
                    Cutoff = 950.0, Sustain = 0.62, Decay = 2.2, Release = 0.2,
                    VibratoRate = 4.6, VibratoDepth = 9.0, VibratoDelay = 0.35, Reverb = 0.22, Level = 0.3718,
                };

            case 36: // Slap Bass 1 - the thumb, which is a filter envelope and a thump
                return new GmRow
                {
                    Tone = GmTone.FmBass, Shape = 1.0, Ring = 0.35,
                    Cutoff = 700.0, Resonance = 0.5, FilterEnvelope = 2.6,
                    FilterAttack = 0.001, FilterDecay = 0.14, FilterSustain = 0.1,
                    Decay = 0.8, Sustain = 0.2,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.1,
                        Attack = 0.0005, Decay = 0.03, Sustain = 0.0, Release = 0.02,
                    }, Level = 0.6255,
                };

            case 37: // Slap Bass 2 - the pop, higher and sharper than the thumb
                return new GmRow
                {
                    Tone = GmTone.FmBass, Shape = 1.0, Ring = 0.25,
                    Cutoff = 1100.0, Resonance = 0.6, FilterEnvelope = 2.4,
                    FilterAttack = 0.001, FilterDecay = 0.1, FilterSustain = 0.06,
                    Decay = 0.6, Sustain = 0.12,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.14,
                        Attack = 0.0005, Decay = 0.02, Sustain = 0.0, Release = 0.02,
                    }, Level = 0.6511,
                };

            case 38: // Synth Bass 1 - a saw under a resonant filter, the oldest trick there is
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 600.0, Resonance = 0.5,
                    FilterEnvelope = 2.2, FilterAttack = 0.002, FilterDecay = 0.35, FilterSustain = 0.15,
                    Decay = 1.4, Sustain = 0.55, Release = 0.12, Level = 0.334,
                };

            case 39: // Synth Bass 2 - a square, lower and rounder, with more resonance
                return new GmRow
                {
                    Tone = GmTone.Square, Cutoff = 450.0, Resonance = 0.62,
                    FilterEnvelope = 2.6, FilterAttack = 0.002, FilterDecay = 0.5, FilterSustain = 0.2,
                    Decay = 1.8, Sustain = 0.6, Release = 0.16, Level = 0.1828,
                };

            default:
                return new GmRow();
        }
    }

    // ============================================== 40-47  STRINGS
    // Solo bowed strings are the weakest thing in the bank and the plan says so plainly (D7). What
    // is here is the honest best: a slow bow, a filter that opens as the note is leaned into, and a
    // vibrato the player brings in rather than switches on.
    private static GmRow Strings(int program)
    {
        switch (program)
        {
            case 40: // Violin
                return new GmRow
                {
                    Tone = GmTone.FmString, Shape = 0.58, Ring = 0.6,
                    Cutoff = 3300.0, VibratoDepth = 17.0, VibratoRate = 5.8, Level = 0.3313,
                };

            case 41: // Viola
                return new GmRow
                {
                    Tone = GmTone.FmString, Shape = 0.48, Ring = 0.7,
                    Cutoff = 2700.0, VibratoDepth = 15.0, VibratoRate = 5.4, Level = 0.3261,
                };

            case 42: // Cello
                return new GmRow
                {
                    Tone = GmTone.FmString, Shape = 0.42, Ring = 0.8,
                    Cutoff = 2000.0, VibratoDepth = 13.0, VibratoRate = 5.0,
                    Attack = 0.12, Level = 0.324,
                };

            case 43: // Contrabass
                return new GmRow
                {
                    Tone = GmTone.FmString, Shape = 0.32, Ring = 0.9,
                    Cutoff = 1300.0, VibratoDepth = 10.0, VibratoRate = 4.6,
                    Attack = 0.14, Level = 0.34, Reverb = 0.4,
                };

            case 44: // Tremolo Strings - a fast bow, which is a fast tremolo on the level
                return new GmRow
                {
                    Tone = GmTone.Saw, Unison = 2, Detune = 10.0, Spread = 0.6,
                    Attack = 0.1, Sustain = 0.9, Release = 0.35, Cutoff = 2400.0,
                    VibratoRate = 7.5, VibratoDelay = 0.0, VibratoFade = 0.05,
                    Tremolo = 0.75, VibratoDepth = 4.0, Level = 0.2607, Reverb = 0.55,
                };

            case 45: // Pizzicato Strings - the bow put down and the string pulled
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.45, Ring = 0.38,
                    Attack = 0.0006, Decay = 0.75, Sustain = 0.0, Release = 0.08,
                    Cutoff = 3000.0, VibratoDepth = 0.0, KeyToTime = 0.6, Level = 1.508, Reverb = 0.42,
                };

            case 46: // Orchestral Harp - one of the best things this bank does
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.24, Ring = 0.88,
                    Attack = 0.0006, Decay = 5.5, Sustain = 0.0, Release = 0.25,
                    Cutoff = 4000.0, KeyTracking = 0.7, VibratoDepth = 0.0,
                    KeyToTime = 0.55, Level = 0.81, Reverb = 0.55,
                };

            case 47: // Timpani - a big tuned drum, so a pitch that drops and a body that booms
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.Percussive,
                    Tone = GmTone.Sine,
                    Attack = 0.001, Decay = 2.4, Sustain = 0.0, Release = 0.3,
                    PitchEnvelope = 3.5, PitchEnvelopeTime = 0.11,
                    Cutoff = 1200.0, KeyTracking = 0.5, KeyToTime = 0.3,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.22,
                        Attack = 0.0005, Decay = 0.12, Sustain = 0.0, Release = 0.05,
                    },
                    Level = 0.2893, Reverb = 0.5,
                };

            default:
                return new GmRow();
        }
    }

    // ============================================= 48-55  ENSEMBLE
    // Sections and voices. This is where the stereo unison is spent, and where the choir programs -
    // the highest-rated sound in either audition - live.
    private static GmRow Ensemble(int program)
    {
        switch (program)
        {
            case 48: // String Ensemble 1
                return new GmRow { Unison = 3, Detune = 9.0, Spread = 0.8, Attack = 0.16, Cutoff = 2300.0, Level = 0.1274 };

            case 49: // String Ensemble 2 - slower and wider than the first
                return new GmRow
                {
                    Unison = 3, Detune = 13.0, Spread = 0.9,
                    Attack = 0.32, Release = 0.6, Cutoff = 2000.0, Reverb = 0.65, Level = 0.1275,
                };

            case 50: // SynthStrings 1 - the same idea made of electricity rather than horsehair
                return new GmRow
                {
                    Unison = 3, Detune = 11.0, Spread = 0.85,
                    Attack = 0.2, Release = 0.5, Cutoff = 2800.0, Resonance = 0.12,
                    FilterEnvelope = 1.0, FilterAttack = 0.5, FilterSustain = 0.7,
                    Chorus = 0.5, Level = 0.1277,
                };

            case 51: // SynthStrings 2 - a wavetable rather than a saw, so it moves as it holds
                return new GmRow
                {
                    Tone = GmTone.WavetablePad, Shape = 0.55,
                    Unison = 3, Detune = 12.0, Spread = 0.9,
                    Attack = 0.28, Release = 0.7, Cutoff = 2600.0,
                    FilterLfo = 0.25, VibratoRate = 0.7, Chorus = 0.45, Level = 0.1313,
                };

            // THE TWO PROGRAMS THAT SING RATHER THAN PLAY. A vowel is two or three resonances that
            // STAY WHERE THEY ARE while the note moves through them, which is a filter and not a
            // waveform - so these two are the one place in the bank whose rows live in a file of
            // their own, beside the voice that reads them. See GmChoirRows and GmChoirSpec.
            case 52:    // Choir Aahs - rated 9.7 in audition
            case 53:    // Voice Oohs - the same throat with the mouth nearly shut
                return GmChoirRows.Row(program, GmChoirRows.BankVoicing);

            case 54: // Synth Voice - a vocal pad rather than a choir
                return new GmRow
                {
                    Tone = GmTone.WavetableVox, Shape = 0.55,
                    Unison = 3, Detune = 11.0, Spread = 0.85,
                    Attack = 0.22, Release = 0.7, Cutoff = 2200.0,
                    FilterLfo = 0.3, VibratoRate = 0.8,
                    Level = 0.2416, Reverb = 0.65, Chorus = 0.45,
                };

            case 55: // Orchestra Hit - the whole orchestra, for a sixth of a second
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.Percussive,
                    Tone = GmTone.Saw, Unison = 3, Detune = 16.0, Spread = 0.8,
                    Attack = 0.004, Decay = 0.34, Sustain = 0.0, Release = 0.1,
                    Cutoff = 3000.0, Resonance = 0.2, FilterEnvelope = 1.6,
                    PitchEnvelope = 1.2, PitchEnvelopeTime = 0.04,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.18,
                        Attack = 0.001, Decay = 0.1, Sustain = 0.0, Release = 0.05,
                    },
                    Level = 0.2857, KeyToTime = 0.2, Reverb = 0.5,
                };

            default:
                return new GmRow();
        }
    }

    // ================================================ 56-63  BRASS
    private static GmRow Brass(int program)
    {
        switch (program)
        {
            case 56: // Trumpet - the brightest of them, and the quickest to speak
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.72, Ring = 0.7,
                    Attack = 0.03, Cutoff = 3100.0, Level = 0.4138,
                };

            case 57: // Trombone
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.58, Ring = 0.85,
                    Attack = 0.04, Cutoff = 2200.0, Level = 0.4677,
                };

            case 58: // Tuba - slow to speak and almost all fundamental
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.4, Ring = 1.0,
                    Attack = 0.06, Cutoff = 1300.0, Level = 0.5387, Reverb = 0.35,
                };

            case 59: // Muted Trumpet - a mute is a resonance, which is a band-pass
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.95, Ring = 0.6,
                    Attack = 0.025, Filter = GmFilterMode.BandPass,
                    Cutoff = 1700.0, Resonance = 0.55, KeyTracking = 0.35,
                    Level = 3.284, Reverb = 0.38,
                };

            case 60: // French Horn - the round one, played into the hand
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.4, Ring = 1.1,
                    Attack = 0.075, Cutoff = 1900.0, Level = 0.5314, Reverb = 0.5,
                };

            case 61: // Brass Section - several players, never quite together
                return new GmRow
                {
                    Tone = GmTone.FmBrass, Shape = 0.65, Ring = 0.8,
                    Attack = 0.05, Cutoff = 2600.0,
                    Unison = 2, Detune = 12.0, Spread = 0.6, Level = 0.3555, Reverb = 0.5,
                };

            case 62: // SynthBrass 1 - a saw and a fast filter envelope, which is the 1980s
                return new GmRow
                {
                    Tone = GmTone.Saw, Attack = 0.012, Sustain = 0.82, Release = 0.2,
                    Cutoff = 1200.0, Resonance = 0.3, FilterEnvelope = 2.2,
                    FilterAttack = 0.06, FilterDecay = 0.5, FilterSustain = 0.45,
                    PitchEnvelope = 0.0, Level = 0.3281,
                };

            case 63: // SynthBrass 2 - wider, slower, and detuned
                return new GmRow
                {
                    Tone = GmTone.Saw, Unison = 2, Detune = 14.0, Spread = 0.55,
                    Attack = 0.04, Sustain = 0.85, Release = 0.25,
                    Cutoff = 1000.0, Resonance = 0.25, FilterEnvelope = 2.4,
                    FilterAttack = 0.12, FilterDecay = 0.8, FilterSustain = 0.5,
                    PitchEnvelope = 0.0, Level = 0.2005, Chorus = 0.3,
                };

            default:
                return new GmRow();
        }
    }

    // ================================================= 64-71  REED
    private static GmRow Reed(int program)
    {
        switch (program)
        {
            case 64: // Soprano Sax
                return new GmRow { Tone = GmTone.HarmonicReed, Shape = 0.68, Cutoff = 3100.0, Level = 0.7884 };

            case 65: // Alto Sax
                return new GmRow { Tone = GmTone.HarmonicReed, Shape = 0.6, Cutoff = 2600.0, Level = 0.752 };

            case 66: // Tenor Sax
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.5, Cutoff = 2200.0,
                    Attack = 0.05, Level = 0.6383,
                };

            case 67: // Baritone Sax
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.42, Cutoff = 1700.0,
                    Attack = 0.06, Level = 0.5344, Reverb = 0.36,
                };

            case 68: // Oboe - a double reed is nasal, which is a resonance well up the spectrum
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.78,
                    Cutoff = 2600.0, Resonance = 0.42, KeyTracking = 0.45,
                    Attack = 0.035, VibratoDepth = 14.0, Level = 0.7423, Reverb = 0.45,
                };

            case 69: // English Horn - the oboe's lower, rounder cousin
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.62,
                    Cutoff = 2100.0, Resonance = 0.35, Attack = 0.04,
                    VibratoDepth = 13.0, Level = 0.732, Reverb = 0.45,
                };

            case 70: // Bassoon - a long double reed, and mostly low
                return new GmRow
                {
                    Tone = GmTone.HarmonicReed, Shape = 0.4,
                    Cutoff = 1400.0, Resonance = 0.3, Attack = 0.05,
                    VibratoDepth = 9.0, Level = 0.4979, Reverb = 0.42,
                };

            case 71: // Clarinet - odd harmonics and almost nothing else; a cylindrical bore
                return new GmRow
                {
                    Tone = GmTone.HarmonicOdd, Shape = 0.5,
                    Cutoff = 2500.0, Attack = 0.04, VibratoDepth = 7.0,
                    Level = 0.4467, Reverb = 0.42,
                };

            default:
                return new GmRow();
        }
    }

    // ================================================= 72-79  PIPE
    // The breath layer the spine provides is what separates every one of these from a sine wave;
    // the rows mostly decide how much of it there is.
    private static GmRow Pipe(int program)
    {
        switch (program)
        {
            case 72: // Piccolo - small, high and airy
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.38, Cutoff = 6500.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.09, Attack = 0.015, Sustain = 0.4 },
                    Level = 0.3343, VibratoDepth = 12.0,
                };

            case 73: // Flute
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.26, Cutoff = 3600.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.1, Attack = 0.02, Sustain = 0.4 },
                    Level = 0.2989, VibratoDepth = 13.0,
                };

            case 74: // Recorder - a wooden whistle, so less breath and fewer partials
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.2, Cutoff = 3000.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.12, Attack = 0.02, Sustain = 0.45 },
                    Level = 0.29, VibratoDepth = 8.0,
                };

            case 75: // Pan Flute - a lot of breath and a wide vibrato
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.3, Cutoff = 3200.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.2, Attack = 0.03, Sustain = 0.5 },
                    Attack = 0.07, VibratoDepth = 17.0, VibratoDelay = 0.3,
                    Level = 0.2977, Reverb = 0.55,
                };

            case 76: // Blown Bottle - almost all breath over a hollow resonance
                return new GmRow
                {
                    Tone = GmTone.Sine, Cutoff = 1700.0, Resonance = 0.3,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.3, Attack = 0.04, Sustain = 0.6 },
                    Attack = 0.08, Level = 0.2258, Reverb = 0.5,
                };

            case 77: // Shakuhachi - breath is the point, and so is the wide slow vibrato
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.36, Cutoff = 2800.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.26, Attack = 0.035, Sustain = 0.55 },
                    Attack = 0.09, VibratoRate = 4.4, VibratoDepth = 19.0, VibratoDelay = 0.28,
                    Level = 0.2997, Reverb = 0.55,
                };

            case 78: // Whistle - a pure tone and almost nothing else
                return new GmRow
                {
                    Tone = GmTone.Sine, Cutoff = 8000.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.035, Attack = 0.02, Sustain = 0.35 },
                    Attack = 0.03, VibratoRate = 6.0, VibratoDepth = 14.0, VibratoDelay = 0.2,
                    Level = 0.2299,
                };

            case 79: // Ocarina - a vessel flute: a fundamental with barely a second partial
                return new GmRow
                {
                    Tone = GmTone.HarmonicSoft, Shape = 0.12, Cutoff = 2400.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Noise, Level = 0.07, Attack = 0.02, Sustain = 0.35 },
                    Attack = 0.04, VibratoDepth = 10.0, Level = 0.2765,
                };

            default:
                return new GmRow();
        }
    }

    // =========================================== 80-87  SYNTH LEAD
    private static GmRow SynthLead(int program)
    {
        switch (program)
        {
            case 80: // Lead 1 (square)
                return new GmRow
                {
                    Tone = GmTone.Square, Cutoff = 2400.0, Resonance = 0.35,
                    FilterEnvelope = 1.6, Level = 0.1863,
                };

            case 81: // Lead 2 (sawtooth)
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 2800.0, Resonance = 0.4,
                    FilterEnvelope = 1.9, Level = 0.3186,
                };

            case 82: // Lead 3 (calliope) - a steam whistle: a near-triangle with a lot of vibrato
                return new GmRow
                {
                    Tone = GmTone.Triangle, Cutoff = 4000.0, Resonance = 0.1, FilterEnvelope = 0.6,
                    Attack = 0.04, Sustain = 0.95, Release = 0.2,
                    VibratoRate = 5.8, VibratoDepth = 18.0, VibratoDelay = 0.2, Level = 0.2838,
                };

            case 83: // Lead 4 (chiff) - a breathy transient on the front of a soft lead
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 2000.0, Resonance = 0.25, FilterEnvelope = 1.4,
                    Attack = 0.012, Sustain = 0.85,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.22,
                        Attack = 0.001, Decay = 0.06, Sustain = 0.0, Release = 0.04,
                    },
                    Level = 0.3097,
                };

            case 84: // Lead 5 (charang) - a hard, shaped, guitar-like lead
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 2200.0, Resonance = 0.45, FilterEnvelope = 1.8,
                    Sustain = 0.7, Level = 0.3166,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.WaveShaper,
                        Parameter1 = "drive", Value1 = 0.6,
                        Parameter2 = "outputlevel", Value2 = 0.55,
                        Parameter3 = "mix", Value3 = 0.8,
                    },
                };

            case 85: // Lead 6 (voice) - a vocal lead, which is the vowel wavetable up front
                return new GmRow
                {
                    Tone = GmTone.WavetableVox, Shape = 0.5,
                    Cutoff = 2600.0, Resonance = 0.2, FilterEnvelope = 1.0,
                    Attack = 0.03, Sustain = 0.9, Release = 0.3,
                    VibratoRate = 5.0, VibratoDepth = 14.0, VibratoDelay = 0.3,
                    Level = 0.4823, Reverb = 0.45,
                };

            case 86: // Lead 7 (fifths) - the lead and a fifth above it, as one voice
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 2600.0, Resonance = 0.3, FilterEnvelope = 1.6,
                    Layer2 = new GmLayerRow { Tone = GmTone.Saw, Transpose = 7.0, Level = 0.6 },
                    Level = 0.2866,
                };

            case 87: // Lead 8 (bass + lead) - a lead with its own octave underneath
                return new GmRow
                {
                    Tone = GmTone.Saw, Cutoff = 2200.0, Resonance = 0.35, FilterEnvelope = 1.8,
                    Layer2 = new GmLayerRow { Tone = GmTone.Square, Transpose = -12.0, Level = 0.55 },
                    Level = 0.2307, Reverb = 0.28,
                };

            default:
                return new GmRow();
        }
    }

    // ============================================ 88-95  SYNTH PAD
    // The family the auditions liked best and the one synthesis wins outright. The spine does most
    // of the work; these rows choose the colour and how far the filter travels.
    private static GmRow SynthPad(int program)
    {
        switch (program)
        {
            case 88: // Pad 1 (new age) - hollow and glassy, with a slow bloom
                return new GmRow
                {
                    Shape = 0.22, Cutoff = 1700.0, FilterEnvelope = 1.6,
                    Attack = 0.8, Release = 1.6, Reverb = 0.68, Level = 0.2047,
                };

            case 89: // Pad 2 (warm) - dark, close, and slow
                return new GmRow
                {
                    Shape = 0.12, Cutoff = 1150.0, FilterEnvelope = 1.1,
                    Attack = 0.9, Release = 1.5, Detune = 6.0, Reverb = 0.6, Level = 0.2351,
                };

            case 90: // Pad 3 (polysynth) - a saw stack, brighter and faster in
                return new GmRow
                {
                    Tone = GmTone.Saw, Unison = 3, Detune = 10.0, Spread = 0.8,
                    Attack = 0.25, Decay = 1.2, Sustain = 0.78, Release = 0.9,
                    Cutoff = 2100.0, Resonance = 0.2, FilterEnvelope = 1.5,
                    Level = 0.1451, Reverb = 0.58,
                };

            case 91: // Pad 4 (choir) - voices held: the pad that came out of the 9.7 audition
                return new GmRow
                {
                    Tone = GmTone.WavetableVox, Shape = 0.42,
                    Unison = 2, Detune = 8.0, Spread = 0.85,
                    Attack = 0.7, Decay = 1.5, Sustain = 0.85, Release = 1.5,
                    Cutoff = 1800.0, FilterEnvelope = 1.1,
                    // Deliberately NOT the formant oscillator, which program 52 uses: a pad is held in
                    // thick chords and the formant tone is the dearest tone in the package, so it is
                    // spent on the choir itself and nowhere else.
                    Layer3 = new GmLayerRow
                    {
                        Tone = GmTone.HarmonicSoft, Shape = 0.55, Level = 0.3, Attack = 0.9, Sustain = 0.85,
                    },
                    Level = 0.2557, Reverb = 0.75, Chorus = 0.4,
                };

            case 92: // Pad 5 (bowed) - a pad with a bow's edge growing inside it
                return new GmRow
                {
                    Shape = 0.38, Cutoff = 1500.0, FilterEnvelope = 1.5,
                    Attack = 0.85, Release = 1.4,
                    Layer2 = new GmLayerRow { Tone = GmTone.FmString, Shape = 0.5, Level = 0.4, Attack = 1.1 },
                    Reverb = 0.7, Level = 0.1609,
                };

            case 93: // Pad 6 (metallic) - inharmonic partials smeared across the field
                return new GmRow
                {
                    Tone = GmTone.FmMetal, Shape = 0.35, Ring = 1.6,
                    Attack = 0.5, Decay = 3.5, Sustain = 0.5, Release = 1.6,
                    Cutoff = 2400.0, FilterEnvelope = 1.2,
                    Insert = new GmInsertSpec
                    {
                        Type = ModestEffectTypes.StereoSimulator,
                        Parameter1 = "width", Value1 = 0.85,
                        Parameter2 = "modrate", Value2 = 0.3,
                        Parameter3 = "moddepth", Value3 = 0.35,
                    },
                    Level = 0.7989, Reverb = 0.7,
                };

            case 94: // Pad 7 (halo) - high, wide and washed; almost no bottom to it
                return new GmRow
                {
                    Tone = GmTone.WavetableVox, Shape = 0.65,
                    Unison = 2, Detune = 9.0, Spread = 0.9,
                    Attack = 1.1, Decay = 2.0, Sustain = 0.8, Release = 2.0,
                    Filter = GmFilterMode.HighPass, Cutoff = 320.0, KeyTracking = 0.3,
                    FilterEnvelope = 0.0, FilterLfo = 0.0,
                    Level = 0.4673, Reverb = 0.78, Chorus = 0.4,
                };

            case 95: // Pad 8 (sweep) - the filter IS the program
                return new GmRow
                {
                    Shape = 0.5, Cutoff = 900.0, Resonance = 0.45, FilterEnvelope = 2.6,
                    FilterAttack = 2.2, FilterDecay = 3.0, FilterSustain = 0.3, FilterRelease = 2.0,
                    Attack = 0.6, Release = 1.6,
                    FilterLfo = 0.55, VibratoRate = 0.55, Reverb = 0.7, Level = 0.1775,
                };

            default:
                return new GmRow();
        }
    }

    // ========================================= 96-103  SYNTH EFFECTS
    // Atmospheres, not instruments. Every one of them is meant to be held rather than played.
    private static GmRow SynthEffects(int program)
    {
        switch (program)
        {
            case 96: // FX 1 (rain) - filtered noise falling through a resonance
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.BandPass,
                    Cutoff = 2600.0, Resonance = 0.6, KeyTracking = 0.8,
                    Attack = 0.15, Decay = 1.4, Sustain = 0.45, Release = 1.2,
                    FilterLfo = 1.1, VibratoRate = 2.4, Level = 0.4977, Reverb = 0.75,
                };

            case 97: // FX 2 (soundtrack) - a slow sweep with a fifth buried in it
                return new GmRow
                {
                    Shape = 0.4, Unison = 2, Detune = 10.0, Spread = 0.85,
                    Attack = 0.9, Release = 2.0, Cutoff = 1300.0, Resonance = 0.35,
                    FilterEnvelope = 2.2, FilterAttack = 1.8, FilterSustain = 0.5,
                    Layer2 = new GmLayerRow { Tone = GmTone.Saw, Transpose = 7.0, Level = 0.28, Attack = 1.4 },
                    Reverb = 0.78, Level = 0.2349,
                };

            case 98: // FX 3 (crystal) - struck glass that will not stop ringing
                return new GmRow
                {
                    Tone = GmTone.FmGlass, Shape = 0.5, Ring = 1.4,
                    Attack = 0.002, Decay = 4.0, Sustain = 0.0, Release = 1.5,
                    Cutoff = 6500.0, KeyTracking = 0.8, FilterEnvelope = 1.0,
                    Level = 0.4103, KeyToTime = 0.4, Reverb = 0.78, Chorus = 0.3,
                };

            case 99: // FX 4 (atmosphere) - a pad with a plucked shimmer on the front of it
                return new GmRow
                {
                    Shape = 0.2, Attack = 0.5, Release = 1.8, Cutoff = 1600.0,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Pluck, Shape = 0.3, Ring = 0.85, Level = 0.5,
                        Attack = 0.001, Decay = 2.5, Sustain = 0.0, Release = 0.4,
                    },
                    Level = 0.2966, Reverb = 0.78,
                };

            case 100: // FX 5 (brightness) - wide open and climbing
                return new GmRow
                {
                    Shape = 0.85, Attack = 0.3, Release = 1.4,
                    Cutoff = 2800.0, Resonance = 0.3, FilterEnvelope = 2.0,
                    FilterAttack = 1.0, FilterSustain = 0.75,
                    Unison = 2, Detune = 12.0, Spread = 0.85, Level = 0.1867, Reverb = 0.72,
                };

            case 101: // FX 6 (goblins) - vowels wobbling somewhere they should not be
                return new GmRow
                {
                    Tone = GmTone.WavetableVox, Shape = 0.7,
                    Attack = 0.6, Decay = 2.0, Sustain = 0.6, Release = 1.5,
                    Cutoff = 1400.0, Resonance = 0.55, FilterEnvelope = 1.2,
                    FilterLfo = 1.4, VibratoRate = 1.1, VibratoDepth = 26.0, VibratoDelay = 0.1,
                    Level = 0.4141, Reverb = 0.8,
                };

            case 102: // FX 7 (echoes) - a bell that falls away as it repeats
                return new GmRow
                {
                    Tone = GmTone.FmBell, Shape = 0.4, Ring = 1.5,
                    Attack = 0.003, Decay = 5.0, Sustain = 0.0, Release = 2.0,
                    Cutoff = 4000.0, FilterEnvelope = 1.2,
                    Tremolo = 0.55, VibratoRate = 3.2, VibratoDelay = 0.15, VibratoFade = 0.4,
                    Level = 0.5856, Reverb = 0.8,
                };

            case 103: // FX 8 (sci-fi) - inharmonic metal under a moving filter
                return new GmRow
                {
                    Tone = GmTone.FmMetal, Shape = 0.6, Ring = 1.2,
                    Attack = 0.05, Decay = 2.5, Sustain = 0.45, Release = 1.4,
                    Cutoff = 1800.0, Resonance = 0.55, FilterEnvelope = 1.8,
                    FilterLfo = 1.0, VibratoRate = 2.8, Level = 0.6012, Reverb = 0.75,
                };

            default:
                return new GmRow();
        }
    }

    // ============================================== 104-111  ETHNIC
    private static GmRow Ethnic(int program)
    {
        switch (program)
        {
            case 104: // Sitar - a buzzing bridge and the sympathetic strings behind it
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.68, Ring = 0.9,
                    Cutoff = 4200.0, Resonance = 0.3, Decay = 4.0,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Pluck, Shape = 0.8, Ring = 0.92,
                        Transpose = 12.0, Fine = 7.0, Level = 0.25, Decay = 3.0,
                    },
                    Level = 1.236, Reverb = 0.45,
                };

            case 105: // Banjo - a skin head and a very bright, very short string
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.85, Ring = 0.5,
                    Cutoff = 5200.0, Decay = 1.3, Level = 1.288, Reverb = 0.3,
                };

            case 106: // Shamisen - struck with a plectrum, and close to dry
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.72, Ring = 0.58,
                    Cutoff = 4000.0, Decay = 1.8, Level = 1.525, Reverb = 0.3,
                };

            case 107: // Koto - long silk strings over a wooden body
                return new GmRow
                {
                    Tone = GmTone.Pluck, Shape = 0.38, Ring = 0.78,
                    Cutoff = 3400.0, Decay = 3.4, Level = 1.185, Reverb = 0.45,
                };

            case 108: // Kalimba - a plucked metal tine, so a bell rather than a string
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.ChromaticPercussion,
                    Tone = GmTone.FmGlass, Shape = 0.28, Ring = 0.3,
                    Decay = 1.1, Cutoff = 4200.0, Level = 0.594, Reverb = 0.42,
                };

            case 109: // Bag pipe - a reed that never stops, over a drone that never moves
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.Reed,
                    Tone = GmTone.HarmonicReed, Shape = 0.75,
                    Attack = 0.03, Sustain = 1.0, Release = 0.06,
                    Cutoff = 3200.0, Resonance = 0.3, VibratoDepth = 0.0,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.HarmonicOdd, Shape = 0.4, FixedKey = 50.0,
                        Level = 0.3, Attack = 0.05, Sustain = 1.0, Release = 0.1,
                    },
                    // Level trimmed 2.7 dB from 0.5343: the drone keeps adding into the measuring
                    // window, which put it at +5.4 dB on the ethnic family's phrase against the
                    // family's own +2.7 dB.
                    Level = 0.3915, VelocityToLevel = 0.3, Reverb = 0.45,
                };

            case 110: // Fiddle - a violin played harder and faster
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.Strings,
                    Tone = GmTone.FmString, Shape = 0.65, Ring = 0.45,
                    Attack = 0.045, Cutoff = 3400.0,
                    VibratoRate = 6.2, VibratoDepth = 19.0, VibratoDelay = 0.2,
                    Level = 0.3389, Reverb = 0.45,
                };

            case 111: // Shanai - a double reed, and a very nasal one
                return new GmRow
                {
                    Template = GeneralMidiProgramFamily.Reed,
                    Tone = GmTone.HarmonicReed, Shape = 0.78,
                    Cutoff = 3000.0, Resonance = 0.55, KeyTracking = 0.4,
                    Attack = 0.03, VibratoDepth = 16.0, Level = 0.725, Reverb = 0.48,
                };

            default:
                return new GmRow();
        }
    }

    // =========================================== 112-119  PERCUSSIVE
    private static GmRow Percussive(int program)
    {
        switch (program)
        {
            case 112: // Tinkle Bell
                return new GmRow
                {
                    Tone = GmTone.FmGlass, Shape = 0.75, Ring = 0.4,
                    Decay = 0.9, Cutoff = 8000.0, Level = 0.6242, Reverb = 0.5,
                };

            case 113: // Agogo - two struck bells, high and pitched
                return new GmRow
                {
                    Tone = GmTone.FmMetal, Shape = 0.4, Ring = 0.2,
                    Decay = 0.65, Cutoff = 5000.0, Level = 0.6555, Reverb = 0.4,
                };

            case 114: // Steel Drums - a hammered pan: inharmonic, and it blooms as it is struck
                return new GmRow
                {
                    Tone = GmTone.FmBell, Shape = 0.45, Ring = 0.35,
                    Attack = 0.003, Decay = 1.5, Cutoff = 4200.0,
                    FilterEnvelope = 1.2, Level = 0.5301, Reverb = 0.48,
                };

            case 115: // Woodblock
                return new GmRow
                {
                    Tone = GmTone.FmWood, Shape = 0.6, Ring = 0.5,
                    Decay = 0.16, Cutoff = 5000.0, Resonance = 0.35, Level = 1.415, Reverb = 0.3,
                };

            case 116: // Taiko Drum - a big skin, so a deep body and a pitch that falls
                return new GmRow
                {
                    Tone = GmTone.Sine,
                    Attack = 0.001, Decay = 1.0, Sustain = 0.0, Release = 0.15,
                    PitchEnvelope = 6.0, PitchEnvelopeTime = 0.07,
                    Cutoff = 900.0, KeyTracking = 0.55, KeyToTime = 0.35,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.26,
                        Attack = 0.0005, Decay = 0.09, Sustain = 0.0, Release = 0.04,
                    },
                    Level = 0.3611, Reverb = 0.45,
                };

            case 117: // Melodic Tom
                return new GmRow
                {
                    Tone = GmTone.Sine,
                    Attack = 0.001, Decay = 0.62, Sustain = 0.0, Release = 0.1,
                    PitchEnvelope = 4.5, PitchEnvelopeTime = 0.08,
                    Cutoff = 1600.0, KeyTracking = 0.7, KeyToTime = 0.45,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Noise, Level = 0.16,
                        Attack = 0.0005, Decay = 0.06, Sustain = 0.0, Release = 0.03,
                    },
                    Level = 0.4278, Reverb = 0.38,
                };

            case 118: // Synth Drum - a sine and a pitch envelope, and that is the whole instrument
                return new GmRow
                {
                    Tone = GmTone.Sine, FilterOff = true,
                    Attack = 0.001, Decay = 0.5, Sustain = 0.0, Release = 0.08,
                    PitchEnvelope = 22.0, PitchEnvelopeTime = 0.1,
                    KeyToTime = 0.35, Level = 0.4687, Reverb = 0.3,
                };

            case 119: // Reverse Cymbal - noise swelling up to a stop, which is an attack and no more
                return new GmRow
                {
                    Tone = GmTone.Noise, LinearAttack = true,
                    Attack = 1.2, Decay = 0.02, Sustain = 0.0, Release = 0.02,
                    Filter = GmFilterMode.HighPass, Cutoff = 1800.0, KeyTracking = 0.2,
                    FilterEnvelope = 0.0, VelocityToLevel = 0.6, VelocityToAttack = 0.0,
                    KeyToTime = 0.0, Level = 0.367, Reverb = 0.6,
                };

            default:
                return new GmRow();
        }
    }

    // ========================================= 120-127  SOUND EFFECTS
    // Not instruments at all. They still have to be deliberate and recognisable, because a file
    // that asks for one and gets silence is a file nobody can debug.
    private static GmRow SoundEffects(int program)
    {
        switch (program)
        {
            case 120: // Guitar Fret Noise - a finger squeaking along a wound string
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.BandPass,
                    Cutoff = 2200.0, Resonance = 0.7, KeyTracking = 0.6,
                    Attack = 0.004, Decay = 0.18, Sustain = 0.0, Release = 0.05,
                    PitchEnvelope = 0.0, FilterLfo = 0.5, VibratoRate = 9.0, VibratoDelay = 0.0,
                    Level = 1.371, Reverb = 0.25,
                };

            case 121: // Breath Noise
                return new GmRow
                {
                    Tone = GmTone.Noise, Cutoff = 1600.0, Resonance = 0.25, KeyTracking = 0.5,
                    Attack = 0.06, Decay = 0.4, Sustain = 0.3, Release = 0.25,
                    Level = 1.034, Reverb = 0.3,
                };

            case 122: // Seashore - broadband noise breathing in and out
                return new GmRow
                {
                    Tone = GmTone.Noise, Cutoff = 1200.0, Resonance = 0.15, KeyTracking = 0.2,
                    Attack = 0.9, Decay = 2.0, Sustain = 0.8, Release = 1.4,
                    FilterEnvelope = 1.2, FilterLfo = 0.9, VibratoRate = 0.35,
                    Level = 0.5519, Reverb = 0.6,
                };

            case 123: // Bird Tweet - a high tone chirping fast
                return new GmRow
                {
                    Tone = GmTone.Sine, FilterOff = true,
                    Transpose = 24.0,
                    Attack = 0.004, Decay = 0.22, Sustain = 0.25, Release = 0.08,
                    VibratoRate = 13.0, VibratoDepth = 380.0, VibratoDelay = 0.0, VibratoFade = 0.02,
                    Level = 0.4882, Reverb = 0.4,
                };

            case 124: // Telephone Ring - a pair of tones chopped at the ringing rate
                return new GmRow
                {
                    Tone = GmTone.Square, FixedKey = 81.0, FilterOff = true,
                    Attack = 0.002, Decay = 0.3, Sustain = 0.9, Release = 0.02,
                    Tremolo = 1.0, VibratoRate = 20.0, VibratoDelay = 0.0, VibratoFade = 0.0,
                    Layer2 = new GmLayerRow { Tone = GmTone.Square, FixedKey = 86.0, Level = 0.7 },
                    Level = 0.223, VelocityToLevel = 0.4, Reverb = 0.2,
                };

            case 125: // Helicopter - a low chop over a rumble
                return new GmRow
                {
                    Tone = GmTone.Noise, Cutoff = 500.0, Resonance = 0.4, KeyTracking = 0.15,
                    Attack = 0.1, Decay = 1.0, Sustain = 0.85, Release = 0.3,
                    Tremolo = 0.95, VibratoRate = 11.0, VibratoDelay = 0.0, VibratoFade = 0.05,
                    Level = 1.907, Reverb = 0.35,
                };

            case 126: // Applause - a crowd, which is noise with a slow swell
                return new GmRow
                {
                    Tone = GmTone.Noise, Filter = GmFilterMode.HighPass,
                    Cutoff = 900.0, Resonance = 0.1, KeyTracking = 0.2,
                    Attack = 0.35, Decay = 1.2, Sustain = 0.75, Release = 0.8,
                    FilterLfo = 0.3, VibratoRate = 6.5, Level = 0.3618, Reverb = 0.55,
                };

            case 127: // The last of the sound effects: a loud, short, low-pitched crack
                return new GmRow
                {
                    Tone = GmTone.Noise, Cutoff = 2400.0, Resonance = 0.35, KeyTracking = 0.3,
                    Attack = 0.0006, Decay = 0.35, Sustain = 0.0, Release = 0.1,
                    FilterEnvelope = 2.0, FilterDecay = 0.12, FilterSustain = 0.0,
                    Layer2 = new GmLayerRow
                    {
                        Tone = GmTone.Sine, FixedKey = 34.0, Level = 0.5,
                        Attack = 0.0005, Decay = 0.2, Sustain = 0.0, Release = 0.06,
                    },
                    Level = 0.9634, Reverb = 0.4,
                };

            default:
                return new GmRow();
        }
    }
}

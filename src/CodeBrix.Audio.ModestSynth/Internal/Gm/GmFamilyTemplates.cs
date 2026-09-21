using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// THE SIXTEEN FAMILY SPINES the bank is built on, keyed by GeneralMidiProgramFamily, plus the one
// the percussion kit starts from.
//
// A spine settles everything the eight programs of a family share - how they start and stop, whether
// they answer velocity with loudness or with brightness, how far a high note runs faster than a low
// one, how much reverb they ask for - so that a ROW only has to say what makes its program itself.
// Retuning a whole family after a listening session happens HERE, once, rather than eight times.
//
// The six patches in ModestSynthPresets are what these are calibrated against: the organ spine is
// the additive organ's registration and envelope, the guitar spine is the plucked string's, the pad
// spine is the wavetable pad's, and the piano and chromatic spines are the electric piano's family.
internal static class GmFamilyTemplates
{
    internal static GmVoiceSpec For(GeneralMidiProgramFamily family)
    {
        switch (family)
        {
            case GeneralMidiProgramFamily.Piano: return Piano();
            case GeneralMidiProgramFamily.ChromaticPercussion: return ChromaticPercussion();
            case GeneralMidiProgramFamily.Organ: return Organ();
            case GeneralMidiProgramFamily.Guitar: return Guitar();
            case GeneralMidiProgramFamily.Bass: return Bass();
            case GeneralMidiProgramFamily.Strings: return Strings();
            case GeneralMidiProgramFamily.Ensemble: return Ensemble();
            case GeneralMidiProgramFamily.Brass: return Brass();
            case GeneralMidiProgramFamily.Reed: return Reed();
            case GeneralMidiProgramFamily.Pipe: return Pipe();
            case GeneralMidiProgramFamily.SynthLead: return SynthLead();
            case GeneralMidiProgramFamily.SynthPad: return SynthPad();
            case GeneralMidiProgramFamily.SynthEffects: return SynthEffects();
            case GeneralMidiProgramFamily.Ethnic: return Ethnic();
            case GeneralMidiProgramFamily.Percussive: return Percussive();
            default: return SoundEffects();
        }
    }

    // Struck strings. Everything dies away on its own, the top of the keyboard dies fastest, and a
    // hard note is brighter rather than merely louder.
    private static GmVoiceSpec Piano() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmPiano }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.001, Decay = 6.0, Sustain = 0.0, Release = 0.28 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 4200.0,
                Resonance = 0.05,
                KeyTracking = 0.7,
                VelocityOctaves = 1.4,
                EnvelopeOctaves = 1.2,
            },
            Level = 0.62,
            VelocityToLevel = 0.9,
            KeyToTime = 0.55,
            ReverbSend = 0.32,
        };

    // Struck metal and struck wood: no sustain at all, a bright start and a fast top end.
    private static GmVoiceSpec ChromaticPercussion() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmGlass }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.0008, Decay = 2.2, Sustain = 0.0, Release = 0.3 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 6500.0,
                KeyTracking = 0.75,
                VelocityOctaves = 1.0,
                EnvelopeOctaves = 0.8,
            },
            Level = 0.58,
            VelocityToLevel = 0.85,
            KeyToTime = 0.8,
            ReverbSend = 0.45,
        };

    // An organ is a switch: on at once, off at once, the same however hard the key is pressed. The
    // linear attack and the low level are the additive organ preset's own, for the same reason it
    // gives - a stack of partials in phase peaks far above its own RMS.
    private static GmVoiceSpec Organ() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.HarmonicOrgan }],
            Amplitude = new GmEnvelopeSpec
            {
                Attack = 0.004,
                Decay = 0.05,
                Sustain = 1.0,
                Release = 0.04,
                LinearAttack = true,
            },
            Filter = new GmFilterSpec { Mode = GmFilterMode.Off },
            Level = 0.34,
            VelocityToLevel = 0.15,
            KeyToTime = 0.0,
            ReverbSend = 0.38,
        };

    // Plucked strings. The waveguide decays by itself, so the envelope only has to avoid a click and
    // put an end to the voice.
    private static GmVoiceSpec Guitar() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.Pluck, Shape = 0.35, Ring = 0.74 }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.0006, Decay = 4.5, Sustain = 0.0, Release = 0.09 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 3400.0,
                KeyTracking = 0.6,
                VelocityOctaves = 1.2,
                EnvelopeOctaves = 1.0,
            },
            Level = 0.7,
            VelocityToLevel = 0.85,
            KeyToTime = 0.45,
            ReverbSend = 0.28,
        };

    // Bass sits low, stays put and never asks for reverb - a washed-out bass is the fastest way to
    // lose the bottom of a mix.
    private static GmVoiceSpec Bass() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmBass }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.003, Decay = 1.6, Sustain = 0.45, Release = 0.12 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 1200.0,
                Resonance = 0.12,
                KeyTracking = 0.55,
                VelocityOctaves = 1.3,
                EnvelopeOctaves = 1.1,
            },
            Level = 0.75,
            VelocityToLevel = 0.8,
            KeyToTime = 0.3,
            ReverbSend = 0.12,
        };

    // Solo bowed strings. Synthesis is weakest here and the plan says so; what carries it is the
    // slow start, the delayed vibrato and the edge the FM modulator grows as the bow digs in.
    private static GmVoiceSpec Strings() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmString }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.1, Decay = 1.2, Sustain = 0.88, Release = 0.28 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2800.0,
                Resonance = 0.1,
                KeyTracking = 0.55,
                VelocityOctaves = 1.1,
                EnvelopeOctaves = 0.7,
            },
            Lfo = new GmLfoSpec { Rate = 5.4, Delay = 0.35, FadeIn = 0.5, ToPitchCents = 14.0 },
            Level = 0.52,
            VelocityToLevel = 0.7,
            VelocityToAttack = 0.45,
            ReverbSend = 0.5,
        };

    // Sections rather than soloists: several players never quite in tune with each other, which is
    // the one thing a section is that a soloist is not. This is where the stereo unison earns its
    // cost.
    private static GmVoiceSpec Ensemble() =>
        new GmVoiceSpec
        {
            Layers =
            [
                new GmLayerSpec
                {
                    Tone = GmTone.Saw,
                    Unison = 3,
                    UnisonDetuneCents = 9.0,
                    UnisonSpread = 0.75,
                },
            ],
            Amplitude = new GmEnvelopeSpec { Attack = 0.14, Decay = 1.0, Sustain = 0.92, Release = 0.45 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2200.0,
                Resonance = 0.06,
                KeyTracking = 0.5,
                VelocityOctaves = 1.0,
                EnvelopeOctaves = 0.6,
            },
            Lfo = new GmLfoSpec { Rate = 4.6, Delay = 0.5, FadeIn = 0.7, ToPitchCents = 8.0 },
            Level = 0.3,
            VelocityToLevel = 0.7,
            VelocityToAttack = 0.4,
            ReverbSend = 0.58,
            ChorusSend = 0.3,
        };

    // Brass: the bite arrives after the note does, and the harder it is played the brighter it gets.
    // The small upward scoop is what a player's lip does on the way into a note.
    private static GmVoiceSpec Brass() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmBrass }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.035, Decay = 0.5, Sustain = 0.86, Release = 0.16 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2600.0,
                Resonance = 0.08,
                KeyTracking = 0.6,
                VelocityOctaves = 1.6,
                EnvelopeOctaves = 0.9,
                Envelope = new GmEnvelopeSpec { Attack = 0.07, Decay = 0.4, Sustain = 0.7, Release = 0.2 },
            },
            Lfo = new GmLfoSpec { Rate = 5.0, Delay = 0.45, FadeIn = 0.6, ToPitchCents = 9.0 },
            PitchEnvelopeSemitones = -0.35,
            PitchEnvelopeSeconds = 0.05,
            Level = 0.5,
            VelocityToLevel = 0.82,
            VelocityToAttack = 0.5,
            ReverbSend = 0.42,
        };

    // Single and double reeds: a fast but not instant start, a steady body and a vibrato the player
    // brings in rather than switches on.
    private static GmVoiceSpec Reed() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.HarmonicReed }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.045, Decay = 0.4, Sustain = 0.9, Release = 0.12 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2400.0,
                Resonance = 0.1,
                KeyTracking = 0.6,
                VelocityOctaves = 1.2,
                EnvelopeOctaves = 0.5,
            },
            Lfo = new GmLfoSpec { Rate = 5.2, Delay = 0.4, FadeIn = 0.5, ToPitchCents = 12.0 },
            Level = 0.44,
            VelocityToLevel = 0.78,
            VelocityToAttack = 0.5,
            ReverbSend = 0.4,
        };

    // Pipes and flutes: a near-sine body with the breath around it, which is the layer that makes
    // the difference between a flute and a sine wave.
    private static GmVoiceSpec Pipe() =>
        new GmVoiceSpec
        {
            Layers =
            [
                new GmLayerSpec { Tone = GmTone.HarmonicSoft, Shape = 0.3 },
                new GmLayerSpec
                {
                    Tone = GmTone.Noise,
                    Level = 0.06,
                    Envelope = new GmEnvelopeSpec { Attack = 0.02, Decay = 0.25, Sustain = 0.35, Release = 0.1 },
                },
            ],
            Amplitude = new GmEnvelopeSpec { Attack = 0.055, Decay = 0.5, Sustain = 0.94, Release = 0.13 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 3200.0,
                KeyTracking = 0.7,
                VelocityOctaves = 1.0,
                EnvelopeOctaves = 0.4,
            },
            Lfo = new GmLfoSpec { Rate = 5.6, Delay = 0.4, FadeIn = 0.55, ToPitchCents = 13.0 },
            Level = 0.46,
            VelocityToLevel = 0.75,
            VelocityToAttack = 0.5,
            ReverbSend = 0.45,
        };

    // Synthesizer leads: one voice out front, bright, with a resonant filter that moves.
    private static GmVoiceSpec SynthLead() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.Saw }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.006, Decay = 0.8, Sustain = 0.78, Release = 0.2 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2600.0,
                Resonance = 0.35,
                KeyTracking = 0.6,
                VelocityOctaves = 1.6,
                EnvelopeOctaves = 1.8,
                Envelope = new GmEnvelopeSpec { Attack = 0.004, Decay = 0.7, Sustain = 0.35, Release = 0.25 },
            },
            Lfo = new GmLfoSpec { Rate = 5.5, Delay = 0.5, FadeIn = 0.6, ToPitchCents = 10.0 },
            Level = 0.44,
            VelocityToLevel = 0.75,
            ReverbSend = 0.35,
        };

    // Pads: the family the auditions liked best, and the one synthesis is unambiguously good at.
    // Slow in, slow out, wide, and washed.
    private static GmVoiceSpec SynthPad() =>
        new GmVoiceSpec
        {
            Layers =
            [
                new GmLayerSpec
                {
                    Tone = GmTone.WavetablePad,
                    Shape = 0.3,
                    Unison = 2,
                    UnisonDetuneCents = 7.0,
                    UnisonSpread = 0.8,
                },
            ],
            Amplitude = new GmEnvelopeSpec { Attack = 0.75, Decay = 1.6, Sustain = 0.8, Release = 1.3 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 1500.0,
                Resonance = 0.12,
                KeyTracking = 0.45,
                VelocityOctaves = 0.9,
                EnvelopeOctaves = 1.4,
                Envelope = new GmEnvelopeSpec { Attack = 1.2, Decay = 2.5, Sustain = 0.6, Release = 1.5 },
            },
            Lfo = new GmLfoSpec { Rate = 0.9, Delay = 0.8, FadeIn = 1.5, ToCutoffOctaves = 0.35 },
            Level = 0.34,
            VelocityToLevel = 0.55,
            ReverbSend = 0.62,
            ChorusSend = 0.4,
        };

    // The eight "FX" programs: atmospheres rather than instruments, so they start slowly, last a
    // long time and modulate visibly.
    private static GmVoiceSpec SynthEffects() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.WavetablePad, Shape = 0.6 }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.4, Decay = 2.5, Sustain = 0.6, Release = 1.6 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 2000.0,
                Resonance = 0.3,
                KeyTracking = 0.4,
                VelocityOctaves = 0.8,
                EnvelopeOctaves = 1.6,
                Envelope = new GmEnvelopeSpec { Attack = 0.8, Decay = 3.0, Sustain = 0.4, Release = 2.0 },
            },
            Lfo = new GmLfoSpec { Rate = 1.6, Delay = 0.2, FadeIn = 1.0, ToCutoffOctaves = 0.8 },
            Level = 0.34,
            VelocityToLevel = 0.6,
            ReverbSend = 0.7,
            ChorusSend = 0.35,
        };

    // Sitar, banjo, koto, kalimba and their neighbours: plucked and struck, mostly, and brighter and
    // shorter than the guitars.
    private static GmVoiceSpec Ethnic() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.Pluck, Shape = 0.5, Ring = 0.68 }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.0006, Decay = 3.2, Sustain = 0.0, Release = 0.1 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 4000.0,
                Resonance = 0.12,
                KeyTracking = 0.6,
                VelocityOctaves = 1.2,
                EnvelopeOctaves = 1.0,
            },
            Level = 0.66,
            VelocityToLevel = 0.85,
            KeyToTime = 0.5,
            ReverbSend = 0.36,
        };

    // The melodic percussion programs: short, struck, and usually carrying a pitch envelope.
    private static GmVoiceSpec Percussive() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.FmWood }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.0006, Decay = 0.7, Sustain = 0.0, Release = 0.1 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 4500.0,
                Resonance = 0.15,
                KeyTracking = 0.7,
                VelocityOctaves = 1.2,
                EnvelopeOctaves = 1.0,
            },
            Level = 0.62,
            VelocityToLevel = 0.9,
            KeyToTime = 0.6,
            ReverbSend = 0.35,
        };

    // The eight non-musical programs. Noise is the backbone of every one of them.
    private static GmVoiceSpec SoundEffects() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.Noise }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.02, Decay = 1.0, Sustain = 0.5, Release = 0.4 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 3000.0,
                Resonance = 0.2,
                KeyTracking = 0.3,
                VelocityOctaves = 0.8,
                EnvelopeOctaves = 0.6,
            },
            Level = 0.4,
            VelocityToLevel = 0.8,
            ReverbSend = 0.3,
        };

    // THE KIT'S SPINE. A percussion note is a one-shot at a fixed pitch: note-off means nothing to
    // it, the key that played it chose the voicing rather than the pitch, and nothing about it
    // follows the keyboard.
    internal static GmVoiceSpec Percussion() =>
        new GmVoiceSpec
        {
            Layers = [new GmLayerSpec { Tone = GmTone.Noise }],
            Amplitude = new GmEnvelopeSpec { Attack = 0.0005, Decay = 0.25, Sustain = 0.0, Release = 0.05 },
            Filter = new GmFilterSpec
            {
                Mode = GmFilterMode.LowPass,
                Cutoff = 5000.0,
                Resonance = 0.15,
                KeyTracking = 0.0,
                VelocityOctaves = 1.2,
                EnvelopeOctaves = 1.0,
            },
            Level = 0.75,
            VelocityToLevel = 0.9,
            KeyToTime = 0.0,
            IgnoreNoteOff = true,
            FixedKey = 60.0,
            ReverbSend = 0.25,
        };
}

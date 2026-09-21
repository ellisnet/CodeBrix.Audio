using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.ModestSynth.Wavetable;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// Turns a GmTone plus its two knobs into a ModestPatch. This is the whole sound-design vocabulary of
// the bank, in one file, so that "make every bell brighter" is one edit rather than fourteen.
//
// The six patches in ModestSynthPresets are the reference voicings these recipes are calibrated
// against: the electric piano IS ModestSynthPresets.ElectricPiano, the pad plays the same table the
// wavetable pad does, and the drawbar stack is the additive organ's registration.
// THE ONE NUMBER TO GET RIGHT IN AN FM RECIPE IS THE MODULATOR'S LEVEL. The engine turns a
// modulator level of 1.0 into ModulationDepthCycles (2.0) cycles of phase modulation, which is about
// 12.6 radians of modulation index - far past the point where the fundamental disappears altogether
// (the first Bessel null is at 2.4). So a recipe that wants a CLEAR PITCH keeps its modulator level
// under about 0.25, and only the deliberately clangorous ones - metal, wood - go above it.
internal static class GmTones
{
    // Every recipe that takes no knob reads this, and a row that says nothing gets the recipe's own
    // judgement rather than a zero.
    internal const double Default = double.NaN;

    private const int VoxFrameSize = 1024;

    private static readonly object VoxGate = new object();

    private static WavetableFile voxTable;

    internal static ModestPatch Create(GmTone tone, double shape, double ring)
    {
        switch (tone)
        {
            case GmTone.Sine:
                return new ModestPatch { Waveform = ModestWaveform.Sine };

            case GmTone.Triangle:
                return new ModestPatch { Waveform = ModestWaveform.Triangle, RandomPhase = true };

            case GmTone.Saw:
                return new ModestPatch { Waveform = ModestWaveform.Saw, RandomPhase = true };

            case GmTone.Square:
                return new ModestPatch { Waveform = ModestWaveform.Square, RandomPhase = true };

            case GmTone.Noise:
                return new ModestPatch { Waveform = ModestWaveform.Noise };

            case GmTone.Pluck:
                return new ModestPatch
                {
                    Waveform = ModestWaveform.Pluck1,
                    PluckType = Or(shape, 0.35),
                    Damping = Or(ring, 0.72),
                    RandomPhase = true,
                };

            case GmTone.Formant:
                return new ModestPatch { Waveform = ModestWaveform.Formant };

            case GmTone.ChoirVowel:
                // A singer is built by GmChoirOscillator rather than from a patch, because two or
                // three fixed resonances are a filter and a patch has no way to say so. The patch
                // is still here, and still a saw: it is the SOURCE the singer's throat is fed, and
                // it is what gives every copy of the layer a start phase of its own.
                return new ModestPatch { Waveform = ModestWaveform.Saw, RandomPhase = true };

            case GmTone.HarmonicOrgan:
                return Additive(Or(shape, 0.5), [1.0, 0.75, 0.5, 0.6, 0.0, 0.3, 0.0, 0.35]);

            case GmTone.HarmonicOrganFull:
                return Additive(
                    Or(shape, 0.6),
                    [1.0, 0.8, 0.62, 0.7, 0.35, 0.45, 0.2, 0.5, 0.0, 0.25, 0.0, 0.3, 0.0, 0.0, 0.0, 0.22]);

            case GmTone.HarmonicOdd:
                return Additive(Or(shape, 0.45), [1.0, 0.0, 0.45, 0.0, 0.25, 0.0, 0.16, 0.0, 0.11, 0.0, 0.08]);

            case GmTone.HarmonicSoft:
                return Additive(Or(shape, 0.35), [1.0, 0.42, 0.2, 0.11, 0.07, 0.045, 0.03, 0.02]);

            case GmTone.HarmonicBright:
                return Additive(
                    Or(shape, 0.7),
                    [1.0, 0.62, 0.46, 0.36, 0.3, 0.25, 0.22, 0.19, 0.17, 0.15, 0.14, 0.12, 0.11, 0.1, 0.09, 0.08]);

            case GmTone.HarmonicBell:
                // A struck bar heard as a fundamental with a strong upper partial and very little in
                // between - the additive road to a glockenspiel or a celesta.
                return Additive(
                    Or(shape, 0.6),
                    [1.0, 0.08, 0.05, 0.55, 0.04, 0.03, 0.0, 0.3, 0.0, 0.14, 0.0, 0.0, 0.0, 0.0, 0.0, 0.1]);

            case GmTone.HarmonicReed:
                // Odd-dominant with the even partials present but quiet: a double reed's signature.
                return Additive(
                    Or(shape, 0.55),
                    [1.0, 0.22, 0.58, 0.14, 0.4, 0.1, 0.28, 0.08, 0.2, 0.06, 0.14, 0.05, 0.1]);

            case GmTone.WavetablePad:
                {
                    ModestPatch pad = ModestSynthPresets.WavetablePad();
                    pad.WavetablePosition = Or(shape, 0.3);
                    return pad;
                }

            case GmTone.WavetableVox:
                return new ModestPatch
                {
                    Waveform = ModestWaveform.Wavetable,
                    WavetableTable = VoxTable(),
                    WavetableFrameSize = VoxFrameSize,
                    WavetablePosition = Or(shape, 0.4),
                    WavetableFrameInterpolation = true,
                    RandomPhase = true,
                };

            case GmTone.FmEPiano:
                {
                    ModestPatch piano = ModestSynthPresets.ElectricPiano();
                    piano.GetFmOperator(2).Level = 0.55 * Scale(shape, 1.0);
                    piano.GetFmOperator(2).Decay = 0.25 * Scale(ring, 1.0);
                    return piano;
                }

            case GmTone.FmBell:
                // An inharmonic 3.5:1 modulator, which is what makes a struck metal bar sound struck
                // rather than bowed. Both envelopes decay and nothing sustains.
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 6.0, carrierRelease: 1.2,
                    modulatorRatio: 3.51, modulatorLevel: 0.20 * Scale(shape, 1.0),
                    modulatorDecay: 1.6 * Scale(ring, 1.0), velocitySensitivity: 4);

            case GmTone.FmGlass:
                // Higher and thinner than the bell: a celesta, a music box, a tinkle.
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 3.0, carrierRelease: 0.6,
                    modulatorRatio: 7.02, modulatorLevel: 0.10 * Scale(shape, 1.0),
                    modulatorDecay: 0.5 * Scale(ring, 1.0), velocitySensitivity: 3);

            case GmTone.FmMetal:
                // Deliberately clangorous: two inharmonic modulators over one carrier. Gongs, crash
                // cymbals, the metallic pad.
                {
                    ModestPatch metal = FmPair(
                        carrierRatio: 1.0, carrierDecay: 8.0, carrierRelease: 2.0,
                        modulatorRatio: 5.63, modulatorLevel: 0.42 * Scale(shape, 1.0),
                        modulatorDecay: 4.0 * Scale(ring, 1.0), velocitySensitivity: 3);

                    ModestFmOperator second = metal.GetFmOperator(3);
                    second.Ratio = 1.0;
                    second.Level = 0.45;
                    second.Attack = 0.0;
                    second.Decay = 6.0;
                    second.Sustain = 0.0;
                    second.Release = 1.5;

                    ModestFmOperator secondModulator = metal.GetFmOperator(4);
                    secondModulator.Ratio = 8.41;
                    secondModulator.Level = 0.28 * Scale(shape, 1.0);
                    secondModulator.Attack = 0.0;
                    secondModulator.Decay = 2.5;
                    secondModulator.Sustain = 0.0;
                    secondModulator.Release = 0.8;

                    return metal;
                }

            case GmTone.FmBrass:
                // The modulator comes in AFTER the carrier and holds, which is the rising bite a
                // brass note develops as the player leans into it.
                {
                    ModestPatch brass = FmPair(
                        carrierRatio: 1.0, carrierDecay: 0.4, carrierRelease: 0.18,
                        modulatorRatio: 1.0, modulatorLevel: 0.16 * Scale(shape, 1.0),
                        modulatorDecay: 0.5, velocitySensitivity: 5);

                    ModestFmOperator carrier = brass.GetFmOperator(1);
                    carrier.Attack = 0.04;
                    carrier.Sustain = 0.9;

                    ModestFmOperator modulator = brass.GetFmOperator(2);
                    modulator.Attack = 0.08 * Scale(ring, 1.0);
                    modulator.Sustain = 0.62;
                    modulator.Release = 0.2;

                    return brass;
                }

            case GmTone.FmBass:
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 1.2, carrierRelease: 0.12,
                    modulatorRatio: 2.0, modulatorLevel: 0.15 * Scale(shape, 1.0),
                    modulatorDecay: 0.35 * Scale(ring, 1.0), velocitySensitivity: 4,
                    carrierSustain: 0.55);

            case GmTone.FmClav:
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 1.1, carrierRelease: 0.08,
                    modulatorRatio: 3.0, modulatorLevel: 0.22 * Scale(shape, 1.0),
                    modulatorDecay: 0.2 * Scale(ring, 1.0), velocitySensitivity: 6);

            case GmTone.FmWood:
                // A short inharmonic knock: woodblock, claves, the body of a marimba bar.
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 0.22, carrierRelease: 0.06,
                    modulatorRatio: 4.73, modulatorLevel: 0.34 * Scale(shape, 1.0),
                    modulatorDecay: 0.05 * Scale(ring, 1.0), velocitySensitivity: 3);

            case GmTone.FmReed:
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 0.5, carrierRelease: 0.16,
                    modulatorRatio: 3.0, modulatorLevel: 0.10 * Scale(shape, 1.0),
                    modulatorDecay: 0.6, velocitySensitivity: 4,
                    carrierSustain: 0.85, modulatorSustain: 0.7);

            case GmTone.FmString:
                // A slowly rising modulator over a held carrier: a bowed string's growing edge.
                {
                    ModestPatch bowed = FmPair(
                        carrierRatio: 1.0, carrierDecay: 1.5, carrierRelease: 0.5,
                        modulatorRatio: 2.0, modulatorLevel: 0.075 * Scale(shape, 1.0),
                        modulatorDecay: 1.5, velocitySensitivity: 3,
                        carrierSustain: 0.85, modulatorSustain: 0.75);

                    bowed.GetFmOperator(1).Attack = 0.09;
                    bowed.GetFmOperator(2).Attack = 0.16 * Scale(ring, 1.0);

                    return bowed;
                }

            case GmTone.FmPiano:
                // Three pairs: a hammer strike, a body and a low partial. Synthesis is weakest here
                // and this is the honest best of it.
                {
                    ModestPatch piano = new ModestPatch
                    {
                        Waveform = ModestWaveform.Fm6Op,
                        FmAlgorithm = 5,
                    };

                    ModestFmOperator carrier = piano.GetFmOperator(1);
                    carrier.Ratio = 1.0;
                    carrier.Level = 1.0;
                    carrier.Attack = 0.0;
                    carrier.Decay = 5.0;
                    carrier.Sustain = 0.0;
                    carrier.Release = 0.35;

                    ModestFmOperator hammer = piano.GetFmOperator(2);
                    hammer.Ratio = 3.0;
                    hammer.Level = 0.16 * Scale(shape, 1.0);
                    hammer.VelocitySensitivity = 6;
                    hammer.Attack = 0.0;
                    hammer.Decay = 0.32 * Scale(ring, 1.0);
                    hammer.Sustain = 0.0;
                    hammer.Release = 0.12;

                    ModestFmOperator body = piano.GetFmOperator(3);
                    body.Ratio = 2.0;
                    body.Level = 0.3;
                    body.Attack = 0.0;
                    body.Decay = 3.0;
                    body.Sustain = 0.0;
                    body.Release = 0.3;

                    ModestFmOperator bodyModulator = piano.GetFmOperator(4);
                    bodyModulator.Ratio = 1.0;
                    bodyModulator.Level = 0.10;
                    bodyModulator.VelocitySensitivity = 4;
                    bodyModulator.Attack = 0.0;
                    bodyModulator.Decay = 0.9;
                    bodyModulator.Sustain = 0.0;
                    bodyModulator.Release = 0.25;

                    piano.GetFmOperator(5).Level = 0.0;
                    piano.GetFmOperator(6).Level = 0.0;

                    return piano;
                }

            case GmTone.FmVoice:
                // A held carrier with a low-index modulator at the third harmonic: the nasal core a
                // formant layer sits on top of.
                return FmPair(
                    carrierRatio: 1.0, carrierDecay: 1.0, carrierRelease: 0.4,
                    modulatorRatio: 3.0, modulatorLevel: 0.05 * Scale(shape, 1.0),
                    modulatorDecay: 1.0, velocitySensitivity: 2,
                    carrierSustain: 0.9, modulatorSustain: 0.8);

            default:
                return new ModestPatch { Waveform = ModestWaveform.Sine };
        }
    }

    private static double Or(double value, double fallback) => double.IsNaN(value) ? fallback : value;

    // A knob that multiplies rather than replaces: 0.5 is half, 1.0 is what the recipe says, 2.0 is
    // double. A row that says nothing gets exactly the recipe.
    private static double Scale(double value, double fallback) =>
        double.IsNaN(value) ? fallback : 0.25 + (1.5 * value);

    private static ModestPatch Additive(double shape, double[] levels)
    {
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Harmonic,
            NumPartials = levels.Length,
            HarmonicNormalization = 1.0,

            // Positive tilt is darker, so a shape of 1 (bright) has to become a negative tilt.
            HarmonicTilt = 1.0 - (2.0 * shape),
        };

        for (int partial = 1; partial <= levels.Length; partial++)
        {
            patch.SetPartialLevel(partial, levels[partial - 1]);
        }

        return patch;
    }

    // Algorithm 5 is three independent modulator-and-carrier pairs; these recipes use the first, and
    // the ones that need more reach for operators 3 and 4 themselves.
    private static ModestPatch FmPair(
        double carrierRatio,
        double carrierDecay,
        double carrierRelease,
        double modulatorRatio,
        double modulatorLevel,
        double modulatorDecay,
        int velocitySensitivity,
        double carrierSustain = 0.0,
        double modulatorSustain = 0.0)
    {
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Fm6Op,
            FmAlgorithm = 5,
        };

        ModestFmOperator carrier = patch.GetFmOperator(1);
        carrier.Ratio = carrierRatio;
        carrier.Level = 1.0;
        carrier.Attack = 0.0;
        carrier.Decay = carrierDecay;
        carrier.Sustain = carrierSustain;
        carrier.Release = carrierRelease;

        ModestFmOperator modulator = patch.GetFmOperator(2);
        modulator.Ratio = modulatorRatio;
        modulator.Level = modulatorLevel;
        modulator.VelocitySensitivity = velocitySensitivity;
        modulator.Attack = 0.0;
        modulator.Decay = modulatorDecay;
        modulator.Sustain = modulatorSustain;
        modulator.Release = carrierRelease * 0.5;

        for (int number = 3; number <= 6; number++) { patch.GetFmOperator(number).Level = 0.0; }

        return patch;
    }

    // Four frames from a hollow formant-like shape to an open one, built additively so every frame
    // is band-limited before the mip map sees it. The choir and voice pads read it.
    private static WavetableFile VoxTable()
    {
        lock (VoxGate)
        {
            if (voxTable != null) { return voxTable; }

            // Each frame emphasises a different pair of "formant" regions by weighting the partials
            // around them, which is what gives the morph its vowel-like movement.
            int[][] emphasis =
            [
                [2, 3],
                [3, 5],
                [4, 8],
                [6, 11],
            ];

            float[] samples = new float[emphasis.Length * VoxFrameSize];

            for (int frame = 0; frame < emphasis.Length; frame++)
            {
                int offset = frame * VoxFrameSize;
                int first = emphasis[frame][0];
                int second = emphasis[frame][1];

                for (int i = 0; i < VoxFrameSize; i++)
                {
                    double phase = 2.0 * Math.PI * i / VoxFrameSize;
                    double value = 0.0;

                    for (int partial = 1; partial <= 20; partial++)
                    {
                        double distance = Math.Min(Math.Abs(partial - first), Math.Abs(partial - second));
                        double gain = 1.0 / (partial * (1.0 + (distance * distance * 0.55)));
                        value += gain * Math.Sin(phase * partial);
                    }

                    samples[offset + i] = (float)(value * 0.9);
                }
            }

            voxTable = WavetableFile.FromSamples(samples, VoxFrameSize, "GeneralMidi vox");
            return voxTable;
        }
    }
}

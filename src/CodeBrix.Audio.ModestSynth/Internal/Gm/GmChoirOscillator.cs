using System;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// ONE SINGER. A band-limited glottal source with a little air in it, run through the two or three
// FIXED resonances of a sung vowel, with a pitch of its own that never quite settles.
//
// THE CHAIN, in order:
//
//   1. a band-limited sawtooth at this singer's own pitch. A sawtooth falls away at six decibels an
//      octave, which is what the flow out of a larynx does once the lips have had their say, so it
//      is the source a formant filter expects rather than a shape chosen for its own sound;
//   2. BREATH - white noise added to the source, strongest as the note starts. It goes in HERE, at
//      the glottis, so the vowel shapes the air exactly as it shapes the tone;
//   3. the vowel: GmChoirSpec.FormantCount resonators in CASCADE, each a two-pole resonance at a
//      fixed frequency in HERTZ. They do not move when the note does. That is the whole point;
//   4. a first-order lift above GmChoirSpec.PresenceHz, because three cascaded resonances fall away
//      far more steeply above the top formant than a voice does;
//   5. a gain that takes back the three decibels an octave a harmonic source through a fixed filter
//      gains all by itself, so a bass and a soprano come out at the same loudness.
//
// WHAT MAKES IT A SECTION RATHER THAN A MACHINE. Every instance draws its own character from its
// SEED: the rate, depth and onset of its vibrato, and the two slow components of a pitch wander
// that never repeats inside a note. The bank puts two or three of these on one key through the
// layer's ordinary stereo unison, so a chord is a room full of people rather than one voice at
// three times the level - and because the seeds come from the voicing's own counter, the render
// still repeats exactly.
//
// WHAT IT COSTS. About thirty-five operations a sample whatever the pitch: the resonators do not
// care how many harmonics there are. That is the reason it is a filter rather than a sum of
// partials - a sum costs what the pitch says it costs, and a bass note says five hundred.
internal sealed class GmChoirOscillator : ModestOscillatorBase
{
    // How often the drift, the vibrato and the pitch are re-read, in samples. It is the
    // synthesizer's own default block, so a voice rendered a block at a time updates once a block
    // however long the buffer handed to Render turns out to be.
    internal const int ControlSamples = 64;

    // The pitch the level is measured against - middle C.
    internal const double ReferenceHz = 261.626;

    // The name is descriptive rather than one of ModestWaveforms' - this oscillator is the bank's
    // own and is never built through the public factory.
    internal const string WaveformName = "gmchoir";

    // How far the note has to move before the formants are worked out again: a twentieth, which is
    // most of a semitone. Drift and vibrato never reach it, so a held note settles its vowel once,
    // and a bend or a new note settles it again.
    private const double RebuildRatio = 0.05;

    private const double CentsPerOctave = 1200.0;

    private const double TwoPi = 2.0 * Math.PI;

    // How many harmonics the level is worked out over, and how far up. Beyond either the vowel is
    // more than forty decibels down and contributes nothing to how loud the note is; the cap is
    // what bounds the work a very low note asks for.
    private const int LoudnessHarmonicLimit = 128;

    private const double LoudnessCeilingHz = 7000.0;

    private readonly GmChoirSpec spec;
    private readonly SawOscillator source = new SawOscillator();
    private readonly float[] scratch = new float[ControlSamples];
    private readonly double[] formantHz = new double[GmChoirSpec.FormantCount];

    private readonly uint seed;

    // This singer's own character, drawn from the seed once.
    private readonly double driftRateSlow;
    private readonly double driftRateFast;
    private readonly double driftPhaseSlow;
    private readonly double driftPhaseFast;
    private readonly double vibratoRate;
    private readonly double vibratoCents;
    private readonly double vibratoOnset;
    private readonly double vibratoPhase;
    private readonly double breathScale;

    private ModestRandom noise;

    // The resonators, spelled out rather than kept in arrays: the inner loop runs per sample and a
    // set of plain fields carries no bounds checks.
    private double gain1, feedback1, damping1, first1, second1;
    private double gain2, feedback2, damping2, first2, second2;
    private double gain3, feedback3, damping3, first3, second3;
    private double gain4, feedback4, damping4, first4, second4;
    private double gain5, feedback5, damping5, first5, second5;

    private double builtForFrequency = -1.0;
    private double builtForSampleRate = -1.0;
    private double outputGain;
    private double presenceState;
    private double presenceCoefficient;
    private double breathLevel;
    private double breathFloor;
    private double breathCoefficient;
    private double elapsedSeconds;
    private double detuneCents;

    internal GmChoirOscillator(int sampleRate, GmChoirSpec choirSpec, uint voiceSeed)
    {
        spec = choirSpec;
        seed = voiceSeed == 0u ? 1u : voiceSeed;

        ModestRandom draw = new ModestRandom(seed);

        // Two slow components under a hertz. Nothing here is a round number, so two singers whose
        // seeds happen to be close still take a long time to line up.
        driftRateSlow = 0.17 + (0.21 * Next(ref draw));
        driftRateFast = 0.43 + (0.37 * Next(ref draw));
        driftPhaseSlow = Next(ref draw);
        driftPhaseFast = Next(ref draw);

        vibratoRate = spec.VibratoRateHz *
            (1.0 + (spec.VibratoRateSpread * ((2.0 * Next(ref draw)) - 1.0)));
        vibratoCents = spec.VibratoCents *
            (1.0 + (spec.VibratoDepthSpread * ((2.0 * Next(ref draw)) - 1.0)));
        vibratoOnset = spec.VibratoOnsetSeconds *
            (1.0 + (spec.VibratoOnsetSpread * ((2.0 * Next(ref draw)) - 1.0)));
        vibratoPhase = Next(ref draw);

        breathScale = 0.7 + (0.6 * Next(ref draw));

        noise = new ModestRandom(seed);

        SetSampleRate(sampleRate);
        ResetVoice(0.0);
    }

    /// <inheritdoc />
    public override string Waveform => WaveformName;

    // How far this singer is from the note it was given, in cents, as of the last control period.
    // Drift plus its own vibrato; the voicing's low-frequency oscillator and the layer's unison
    // detune are the voice's business and are not counted here.
    internal double DetuneCents => detuneCents;

    // The three resonances this singer is currently sounding, in Hz - what a test measures the
    // spectrum against.
    internal double FormantHz(int index)
    {
        Rebuild();
        return formantHz[index];
    }

    /// <inheritdoc />
    public override void SetSampleRate(int sampleRate)
    {
        base.SetSampleRate(sampleRate);
        source.SetSampleRate(sampleRate);

        presenceCoefficient = Coefficient(spec.PresenceHz, sampleRate);

        // How much of the way from the onset's breath to the sustain's one sample covers, as a
        // plain exponential settling on the floor.
        breathCoefficient = Math.Exp(-1.0 / (Math.Max(0.01, spec.BreathSeconds) * sampleRate));

        builtForSampleRate = -1.0;
    }

    /// <inheritdoc />
    public override void SetFrequency(double frequencyHz)
    {
        base.SetFrequency(frequencyHz);
        source.SetFrequency(frequencyHz);
    }

    /// <inheritdoc />
    public override void Reset(double phase)
    {
        base.Reset(phase);
        ResetVoice(phase);
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        int done = 0;

        while (done < buffer.Length)
        {
            int length = buffer.Length - done;
            if (length > ControlSamples) { length = ControlSamples; }

            RenderChunk(buffer.Slice(done, length));
            done += length;
        }
    }

    private void RenderChunk(Span<float> buffer)
    {
        Rebuild();

        detuneCents = Drift() + Vibrato();

        double frequency = Frequency <= 0.0
            ? 0.0
            : Frequency * Math.Pow(2.0, detuneCents / CentsPerOctave);

        source.SetFrequency(frequency);

        Span<float> block = new Span<float>(scratch, 0, buffer.Length);
        source.Render(block);

        double state1First = first1, state1Second = second1;
        double state2First = first2, state2Second = second2;
        double state3First = first3, state3Second = second3;
        double state4First = first4, state4Second = second4;
        double state5First = first5, state5Second = second5;
        double presence = presenceState;
        double breath = breathLevel;

        for (int i = 0; i < buffer.Length; i++)
        {
            double excitation = scratch[i] + (breath * noise.NextBipolar());
            breath = breathFloor + ((breath - breathFloor) * breathCoefficient);

            double value = (gain1 * excitation) + (feedback1 * state1First) + (damping1 * state1Second);
            state1Second = state1First;
            state1First = value;

            value = (gain2 * value) + (feedback2 * state2First) + (damping2 * state2Second);
            state2Second = state2First;
            state2First = value;

            value = (gain3 * value) + (feedback3 * state3First) + (damping3 * state3Second);
            state3Second = state3First;
            state3First = value;

            value = (gain4 * value) + (feedback4 * state4First) + (damping4 * state4Second);
            state4Second = state4First;
            state4First = value;

            value = (gain5 * value) + (feedback5 * state5First) + (damping5 * state5Second);
            state5Second = state5First;
            state5First = value;

            presence += presenceCoefficient * (value - presence);

            buffer[i] = (float)((value + (spec.Presence * (value - presence))) * outputGain);
        }

        first1 = state1First; second1 = state1Second;
        first2 = state2First; second2 = state2Second;
        first3 = state3First; second3 = state3Second;
        first4 = state4First; second4 = state4Second;
        first5 = state5First; second5 = state5Second;
        presenceState = presence;
        breathLevel = breath;

        elapsedSeconds += buffer.Length / (double)SampleRate;
    }

    // The slow wander. Two components, neither of them a whole number of anything, so the sum never
    // repeats inside a note; the two weights add to one, so DriftCents really is the widest it goes.
    private double Drift() =>
        spec.DriftCents *
            ((0.62 * Math.Sin(TwoPi * ((driftRateSlow * elapsedSeconds) + driftPhaseSlow))) +
             (0.38 * Math.Sin(TwoPi * ((driftRateFast * elapsedSeconds) + driftPhaseFast))));

    // This singer's own vibrato, which waits for its own moment and then grows into the note.
    private double Vibrato()
    {
        double since = elapsedSeconds - vibratoOnset;
        if (since <= 0.0) { return 0.0; }

        double fade = spec.VibratoFadeSeconds <= 0.0 ? 1.0 : since / spec.VibratoFadeSeconds;
        if (fade > 1.0) { fade = 1.0; }

        return vibratoCents * fade *
            Math.Sin(TwoPi * ((vibratoRate * elapsedSeconds) + vibratoPhase));
    }

    // Where the vowel sits for the note being sung, and how loud it comes out. Only the NOTE moves
    // this - drift and vibrato are far inside RebuildRatio - so a held note settles it once.
    private void Rebuild()
    {
        if (SampleRate == builtForSampleRate &&
            builtForFrequency > 0.0 &&
            Math.Abs(Frequency - builtForFrequency) <= builtForFrequency * RebuildRatio)
        {
            return;
        }

        builtForFrequency = Frequency;
        builtForSampleRate = SampleRate;

        if (Frequency <= 0.0)
        {
            gain1 = gain2 = gain3 = gain4 = gain5 = 0.0;
            outputGain = 0.0;
            return;
        }

        // Where the singer sits on the keyboard decides which end of the register the vowel comes
        // from: a bass's throat is longer than a soprano's and its formants are lower.
        double key = 69.0 + (12.0 * Math.Log2(Frequency / 440.0));
        double register =
            (key - GmChoirSpec.LowRegisterKey) /
            (GmChoirSpec.HighRegisterKey - GmChoirSpec.LowRegisterKey);

        if (register < 0.0) { register = 0.0; }
        if (register > 1.0) { register = 1.0; }

        for (int i = 0; i < GmChoirSpec.FormantCount; i++)
        {
            formantHz[i] = spec.LowRegisterHz[i] +
                (register * (spec.HighRegisterHz[i] - spec.LowRegisterHz[i]));
        }

        // What a soprano does at the top of her range: rather than sing a note above the first
        // formant, where there would be nothing left to resonate, she opens her mouth until the
        // formant sits on the note. Without it the top of the keyboard goes quiet and dull.
        if (formantHz[0] < Frequency) { formantHz[0] = Frequency; }

        // No resonance is allowed to be narrower than the gap between this note's harmonics.
        double narrowest = Frequency * spec.NarrowestBandwidthOfPitch;

        Resonator(formantHz[0], Widest(0, narrowest), out gain1, out feedback1, out damping1);
        Resonator(formantHz[1], Widest(1, narrowest), out gain2, out feedback2, out damping2);
        Resonator(formantHz[2], Widest(2, narrowest), out gain3, out feedback3, out damping3);
        Resonator(formantHz[3], Widest(3, narrowest), out gain4, out feedback4, out damping4);
        Resonator(formantHz[4], Widest(4, narrowest), out gain5, out feedback5, out damping5);

        // THE LEVEL, WORKED OUT RATHER THAN GUESSED. A fixed resonance sampled by a moving set of
        // harmonics is loud when a harmonic lands on it and quiet when two straddle it: measured,
        // two neighbouring notes came out ten decibels apart. So the note's own loudness is summed
        // over its own harmonics and divided back out, which leaves a bass and a soprano at the
        // same level and takes the lumpiness out of a scale.
        double power = HarmonicPower();

        outputGain = power <= 0.0
            ? 0.0
            : spec.Gain / Math.Sqrt(power) * Math.Pow(ReferenceHz / Frequency, spec.PitchTilt);
    }

    // The mean square this vowel makes of its own sawtooth source, harmonic by harmonic. A
    // sawtooth's harmonic n has amplitude 2/(pi*n), so the term is that times how much the cascade
    // makes of a sine at n times the pitch - and because only the SQUARE is wanted, no square root
    // is taken anywhere in the loop.
    private double HarmonicPower()
    {
        double angle = TwoPi * Frequency / SampleRate;
        (double sine, double cosine) = Math.SinCos(angle);

        double highest = SampleRate * 0.5;
        if (highest > LoudnessCeilingHz) { highest = LoudnessCeilingHz; }

        // cos(n*angle) and sin(n*angle) by Chebyshev recurrence, so the loop costs no sines at all.
        double cosinePrevious = 1.0;
        double sinePrevious = 0.0;
        double cosineCurrent = cosine;
        double sineCurrent = sine;

        double power = 0.0;

        for (int harmonic = 1;
            harmonic <= LoudnessHarmonicLimit && harmonic * Frequency < highest;
            harmonic++)
        {
            double twiceCosine = (2.0 * cosineCurrent * cosineCurrent) - 1.0;
            double twiceSine = 2.0 * sineCurrent * cosineCurrent;

            double amplitude = 2.0 / (Math.PI * harmonic);

            power += amplitude * amplitude *
                CascadeMagnitudeSquared(sineCurrent, cosineCurrent, twiceSine, twiceCosine) * 0.5;

            double cosineNext = (2.0 * cosine * cosineCurrent) - cosinePrevious;
            double sineNext = (2.0 * cosine * sineCurrent) - sinePrevious;

            cosinePrevious = cosineCurrent;
            sinePrevious = sineCurrent;
            cosineCurrent = cosineNext;
            sineCurrent = sineNext;
        }

        return power;
    }

    // A two-pole resonance with unity gain at nothing at all, so the cascade does not change the
    // weight of the bottom of the spectrum as the formants move.
    private void Resonator(
        double frequencyHz, double bandwidthHz, out double gain, out double feedback, out double damping)
    {
        double rate = SampleRate;
        double radius = Math.Exp(-Math.PI * bandwidthHz / rate);

        double highest = rate * 0.49;
        double centre = frequencyHz > highest ? highest : frequencyHz;

        damping = -(radius * radius);
        feedback = 2.0 * radius * Math.Cos(TwoPi * centre / rate);
        gain = 1.0 - feedback - damping;
    }

    private double Widest(int index, double narrowest)
    {
        double bandwidth = spec.BandwidthHz[index];
        return bandwidth < narrowest ? narrowest : bandwidth;
    }

    // The SQUARE of how much the resonators together make of a sine at an angle given as its sine
    // and cosine, and those of twice it.
    private double CascadeMagnitudeSquared(
        double sine, double cosine, double sineTwice, double cosineTwice)
    {
        return MagnitudeSquared(gain1, feedback1, damping1, sine, cosine, sineTwice, cosineTwice) *
            MagnitudeSquared(gain2, feedback2, damping2, sine, cosine, sineTwice, cosineTwice) *
            MagnitudeSquared(gain3, feedback3, damping3, sine, cosine, sineTwice, cosineTwice) *
            MagnitudeSquared(gain4, feedback4, damping4, sine, cosine, sineTwice, cosineTwice) *
            MagnitudeSquared(gain5, feedback5, damping5, sine, cosine, sineTwice, cosineTwice);
    }

    private static double MagnitudeSquared(
        double gain,
        double feedback,
        double damping,
        double sine,
        double cosine,
        double sineTwice,
        double cosineTwice)
    {
        double real = 1.0 - (feedback * cosine) - (damping * cosineTwice);
        double imaginary = (feedback * sine) + (damping * sineTwice);
        double size = (real * real) + (imaginary * imaginary);

        return size <= 0.0 ? 0.0 : gain * gain / size;
    }

    private void ResetVoice(double phase)
    {
        source.Reset(phase);
        source.SetFrequency(Frequency);

        first1 = second1 = 0.0;
        first2 = second2 = 0.0;
        first3 = second3 = 0.0;
        first4 = second4 = 0.0;
        first5 = second5 = 0.0;

        presenceState = 0.0;
        elapsedSeconds = 0.0;
        detuneCents = 0.0;

        noise.Reseed(seed);

        breathFloor = spec.BreathInSustain * breathScale;
        breathLevel = spec.BreathAtOnset * breathScale;
    }

    // A one-pole coefficient for a cutoff in Hz.
    private static double Coefficient(double hertz, int sampleRate)
    {
        double coefficient = 1.0 - Math.Exp(-TwoPi * hertz / sampleRate);
        return coefficient < 0.0 ? 0.0 : coefficient > 1.0 ? 1.0 : coefficient;
    }

    private static double Next(ref ModestRandom draw) => (draw.NextUInt32() >> 8) / 16777216.0;
}

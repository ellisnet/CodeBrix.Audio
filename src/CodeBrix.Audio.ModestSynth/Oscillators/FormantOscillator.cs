using System;
using System.Numerics;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// The <c>formant</c> waveform: a fixed vocal-sounding tone with one resonant region near 2.4 kHz
/// over the note's own fundamental.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED (round 2, item 26). The developer guide does not list this waveform at all, and the
/// reference player accepts it and IGNORES EVERY ATTRIBUTE: twenty-one spellings of a formant
/// control - <c>vowel</c>, <c>formantVowel</c>, <c>formantPosition</c>, <c>morph</c>,
/// <c>formantShift</c>, <c>numFormants</c> and the rest - at two or three values each produced
/// BIT-IDENTICAL audio in all twenty-nine cases. So there is nothing to configure: the waveform is
/// one fixed tone, and any <c>formant-*</c> attribute a preset writes is reported as unsupported.
/// </para>
/// <para>
/// ITS SPECTRUM, at 261.626 Hz and in decibels relative to the fundamental, is the reference's own
/// twenty-partial table: 0.0, -15.4, -22.8, -26.3, -28.1, -28.6, -27.3, -21.2, -10.6, -22.6, -21.9,
/// -28.9, -41.1, -48.7, -54.8, -60.3, -65.4, -68.9, -72.0, -75.3. The strong lines are the
/// fundamental and partials 8 to 11 - 2093 to 2878 Hz - which is ONE formant region centred near
/// 2.4 kHz, with a smooth -18 dB per octave rolloff above it.
/// </para>
/// <para>
/// HOW IT IS BUILT. That table is read as a SPECTRAL ENVELOPE IN HERTZ rather than as a set of
/// partial levels, so the formant stays at 2.4 kHz whatever note is played - which is what a formant
/// does, and what the name promises. Each partial of a band-limited pulse train is given the
/// envelope's value at its own frequency, interpolated in log-frequency between the measured points
/// and extrapolated above them at the measured -18 dB per octave. At 261.626 Hz that reproduces the
/// reference's table exactly; at other pitches the resonance holds still while the harmonics move
/// through it. Partials at or above Nyquist are dropped, so the tone cannot alias.
/// </para>
/// <para>
/// ITS LEVEL is measured too, and it is LOUD: the reference's formant tone reads 5.85 dB above what
/// the same player's <c>sine</c> renders at the same group volume, its fundamental alone standing
/// 8 dB above the whole sine. <see cref="ReferenceGain" /> reproduces that, so a preset that leans
/// on this waveform will want its own volume attribute.
/// </para>
/// <para>
/// THE ONE THING NOT MEASURED is the PHASE of each partial, which a magnitude spectrum cannot show.
/// The partials are given Schroeder phases, which is the arrangement with the lowest crest factor
/// for a given spectrum: the alternative - starting every partial at zero - would triple the peak
/// for exactly the same sound, and the reference's own recorded level sits far too low in its
/// limiter for its tone to be peaky.
/// </para>
/// <para>
/// HOW IT IS RENDERED, and why that is not the obvious way. The sum is
/// <c>sum over k of gains[k] * sin(2*pi*phase*(k+1) + phases[k])</c> - about eighty-four terms at
/// middle C and five hundred at the bottom of the keyboard. Calling <see cref="Math.Sin" /> once per
/// term per sample is what that formula says and it costs about 675 nanoseconds a sample at middle C,
/// which made this waveform an order of magnitude more expensive than anything else in the package.
/// Every term is a harmonic of ONE phase turning at a FIXED rate, so each is instead carried as a
/// rotating vector: partial <c>k</c> holds
/// <c>(gains[k] * cos(theta), gains[k] * sin(theta))</c> and one sample turns it by the fixed angle
/// <c>2*pi*(frequency/sampleRate)*(k+1)</c>, four multiplies and two adds with no transcendental at
/// all. The sine component IS the term the formula asks for, so the sum is the same sum, computed
/// the same way round. The rotation is re-derived from the exact master phase whenever the pitch,
/// the sample rate or the phase moves, and in any case at least every
/// <see cref="RotationRefreshSamples" /> samples, so nothing accumulates over a long held note. The
/// two forms agree to WITHIN ONE ULP OF THE FLOAT THEY ARE BOTH ROUNDED TO, which the tests fence
/// sample by sample against the naive sum, at several pitches and through frequency changes.
/// </para>
/// </remarks>
public sealed class FormantOscillator : ModestOscillatorBase
{
    /// <summary>How many partials the fixed spectrum was measured over.</summary>
    public const int MeasuredPartialCount = 20;

    /// <summary>The pitch the reference's spectrum was measured at, in Hz.</summary>
    public const double MeasuredFundamentalHz = 261.626;

    /// <summary>
    /// How fast the spectrum falls above the measured range, in decibels per octave.
    /// </summary>
    public const double RolloffDecibelsPerOctave = -18.0;

    /// <summary>
    /// The root-mean-square level of this waveform relative to a full-amplitude sine's, as a linear
    /// ratio. MEASURED at +5.85 dB.
    /// </summary>
    public const double ReferenceGain = 1.9588;

    /// <summary>The most partials that can ever sound, at the lowest pitch a note can carry.</summary>
    private const int MaximumPartials = 512;

    // How many samples a partial's rotation may run before it is put back onto the exact master
    // phase. Measured drift over ten times this many samples is still below one ulp of the float
    // the sum is rounded to, so this is a wide margin; at 44,100 Hz it is 93 milliseconds, and
    // re-deriving that often costs well under one per cent of the render.
    private const int RotationRefreshSamples = 4096;

    private const double TwoPi = 2.0 * Math.PI;

    // The measured envelope, in decibels relative to the fundamental, at 1..20 times 261.626 Hz.
    private static readonly double[] EnvelopeDecibels =
    {
        0.0, -15.4, -22.8, -26.3, -28.1, -28.6, -27.3, -21.2, -10.6, -22.6,
        -21.9, -28.9, -41.1, -48.7, -54.8, -60.3, -65.4, -68.9, -72.0, -75.3,
    };

    private readonly double[] gains = new double[MaximumPartials];
    private readonly double[] phases = new double[MaximumPartials];

    // One rotating vector per partial, and the fixed turn one sample applies to it.
    private readonly double[] partialSine = new double[MaximumPartials];
    private readonly double[] partialCosine = new double[MaximumPartials];
    private readonly double[] turnSine = new double[MaximumPartials];
    private readonly double[] turnCosine = new double[MaximumPartials];

    private int activeCount;
    private double builtForFrequency = -1.0;
    private double builtForSampleRate = -1.0;
    private bool rotationIsStale = true;
    private double rotationPhase = double.NaN;
    private int rotationSamples;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Formant;

    /// <summary>How many partials are sounding at the current pitch and sample rate.</summary>
    public int ActivePartialCount
    {
        get
        {
            Rebuild();
            return activeCount;
        }
    }

    /// <summary>
    /// The gain the fixed spectral envelope gives a partial at a frequency, relative to the
    /// envelope's own peak at the fundamental of the pitch it was measured at.
    /// </summary>
    /// <param name="frequencyHz">The partial's frequency in Hz.</param>
    /// <returns>A linear gain.</returns>
    public static double EnvelopeAt(double frequencyHz)
    {
        if (double.IsNaN(frequencyHz) || frequencyHz <= 0.0) { return 0.0; }

        double index = frequencyHz / MeasuredFundamentalHz;

        if (index <= 1.0) { return 1.0; }

        if (index >= MeasuredPartialCount)
        {
            double octaves = Math.Log2(index / MeasuredPartialCount);
            double decibels = EnvelopeDecibels[MeasuredPartialCount - 1] +
                (RolloffDecibelsPerOctave * octaves);
            return Math.Pow(10.0, decibels / 20.0);
        }

        int low = (int)index;
        double fraction = index - low;
        double blended = EnvelopeDecibels[low - 1] +
            (fraction * (EnvelopeDecibels[low] - EnvelopeDecibels[low - 1]));

        return Math.Pow(10.0, blended / 20.0);
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        Rebuild();

        double phase = Phase;
        double increment = PhaseIncrement;
        int count = activeCount;

        if (count == 0)
        {
            // Nothing is sounding - no pitch, or every partial is at or above Nyquist - but the
            // phase still has to run, so a partial that comes back mid-note comes back in phase.
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0.0f;

                phase += increment;
                if (phase >= 1.0) { phase -= Math.Floor(phase); }
            }

            Phase = phase;
            rotationIsStale = true;
            return;
        }

        // Anything that moved the phase from outside - Reset, or a note starting - leaves the
        // rotations standing somewhere the master phase no longer agrees with.
        if (phase != rotationPhase) { rotationIsStale = true; }

        int done = 0;

        while (done < buffer.Length)
        {
            if (rotationIsStale || rotationSamples >= RotationRefreshSamples) { SeedRotations(phase); }

            int length = RotationRefreshSamples - rotationSamples;
            if (length > buffer.Length - done) { length = buffer.Length - done; }

            phase = SumPartials(buffer.Slice(done, length), phase, increment, count);

            rotationSamples += length;
            done += length;
        }

        Phase = phase;
        rotationPhase = phase;
    }

    // THE SUM, one sample at a time. Partial k carries the rotating vector
    // (gains[k] * cos(theta_k), gains[k] * sin(theta_k)) with
    // theta_k = 2*pi*phase*(k+1) + phases[k], so its sine component is exactly the term
    //     gains[k] * Math.Sin((TwoPi * phase * (k + 1)) + phases[k])
    // that the naive formula computes, and turning the vector by (turnCosine[k], turnSine[k]) -
    // the angle partial k covers in one sample - advances that term to the next sample. The lane
    // loop and the tail loop do the same six operations; the lanes just take several partials at a
    // time, using the portable Vector<double> rather than anything machine-specific.
    private double SumPartials(Span<float> buffer, double phase, double increment, int count)
    {
        double[] sines = partialSine;
        double[] cosines = partialCosine;
        double[] turnSines = turnSine;
        double[] turnCosines = turnCosine;

        int width = Vector<double>.Count;
        int lanes = count - (count % width);

        for (int i = 0; i < buffer.Length; i++)
        {
            Vector<double> running = Vector<double>.Zero;
            int k = 0;

            for (; k < lanes; k += width)
            {
                Vector<double> laneSine = new Vector<double>(sines, k);
                Vector<double> laneCosine = new Vector<double>(cosines, k);
                Vector<double> laneTurnSine = new Vector<double>(turnSines, k);
                Vector<double> laneTurnCosine = new Vector<double>(turnCosines, k);

                running += laneSine;

                ((laneSine * laneTurnCosine) + (laneCosine * laneTurnSine)).CopyTo(sines, k);
                ((laneCosine * laneTurnCosine) - (laneSine * laneTurnSine)).CopyTo(cosines, k);
            }

            double sum = Vector.Sum(running);

            for (; k < count; k++)
            {
                double sine = sines[k];
                double cosine = cosines[k];

                sum += sine;

                sines[k] = (sine * turnCosines[k]) + (cosine * turnSines[k]);
                cosines[k] = (cosine * turnCosines[k]) - (sine * turnSines[k]);
            }

            buffer[i] = (float)sum;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        return phase;
    }

    // Puts every partial's rotation back where the master phase says it belongs, which is the one
    // place a sine is still computed: once per partial per refresh rather than once per partial per
    // sample. Because the angle is the naive formula's own argument, the first sample after a
    // refresh is the naive sum to the last bit.
    private void SeedRotations(double phase)
    {
        for (int k = 0; k < activeCount; k++)
        {
            (double sine, double cosine) = Math.SinCos((TwoPi * phase * (k + 1)) + phases[k]);

            partialSine[k] = gains[k] * sine;
            partialCosine[k] = gains[k] * cosine;
        }

        rotationSamples = 0;
        rotationIsStale = false;
    }

    // Works out the partial gains for the current pitch. Only pitch and rate change them, so this is
    // a no-op on every block but the first after either moves.
    private void Rebuild()
    {
        if (Frequency == builtForFrequency && SampleRate == builtForSampleRate) { return; }

        builtForFrequency = Frequency;
        builtForSampleRate = SampleRate;
        activeCount = 0;
        rotationIsStale = true;

        double nyquist = SampleRate * 0.5;
        double power = 0.0;

        for (int k = 1; k <= MaximumPartials; k++)
        {
            double frequency = k * Frequency;
            if (frequency <= 0.0 || frequency >= nyquist) { break; }

            double gain = EnvelopeAt(frequency);
            if (gain <= 1.0e-6) { continue; }

            gains[activeCount++] = gain;
            power += gain * gain;
        }

        if (power <= 0.0) { return; }

        // The measured level: the root-mean-square of the summed partials, which is
        // sqrt(sum of squares / 2), put where the reference's own tone sits relative to a sine.
        double scale = ReferenceGain / Math.Sqrt(power);
        double increment = PhaseIncrement;

        for (int k = 0; k < activeCount; k++)
        {
            gains[k] *= scale;

            // Schroeder's phase set: the lowest crest factor a given magnitude spectrum can have.
            phases[k] = -Math.PI * k * (k + 1) / activeCount;

            // How far partial k turns in one sample. Only the pitch and the rate decide it, which
            // is why it is settled here and not in the render loop.
            (double sine, double cosine) = Math.SinCos(TwoPi * increment * (k + 1));

            turnSine[k] = sine;
            turnCosine[k] = cosine;
        }
    }
}

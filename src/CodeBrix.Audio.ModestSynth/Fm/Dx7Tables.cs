using System;

namespace CodeBrix.Audio.ModestSynth.Fm;

/// <summary>
/// The numeric laws the six-operator FM oscillator is built from: the 0-99 rate and level scales,
/// the detune curve, the velocity-sensitivity curve and the rate scaling, all in the units the
/// classic six-operator hardware used and the format's <c>fmOpN...</c> attributes repeat.
/// </summary>
/// <remarks>
/// <para>
/// These are the numbers that turn a patch bank's integers into seconds and amplitudes. They are
/// public because they are worth reading, worth testing against, and - since not one of them has
/// been measured against the reference player yet - worth knowing the provenance of.
/// </para>
/// <para>
/// WHAT IS MEASURED HERE, against the reference player: the ENVELOPE RATE SCALE (round 2, item 27 -
/// about 5.5 rate units per halving of the segment time, and rate 0 CRAWLS rather than holding), the
/// DETUNE LAW (round 3, item 42 - a power law in frequency, not a fixed number of Hz), and the
/// VELOCITY-SENSITIVITY TABLE (round 2, item 27 - a five-point table whose neutral point is velocity
/// 96, not 127). The level scale and the rate scaling remain reconstructions from the published
/// behaviour of the hardware the format names; the format has no attribute for rate scaling at all.
/// </para>
/// <para>
/// LEVEL UNITS are the 0-99 scale the four-stage envelope and the hardware's output level use:
/// 99 is full amplitude and every 8 units below it halves the amplitude, so one unit is
/// 0.753 dB. RATES are the 0-99 scale where 99 is nearly instant and 0 does not move at all.
/// </para>
/// </remarks>
public static class Dx7Tables
{
    /// <summary>The lowest rate or level value: a rate of 0 does not move, a level of 0 is silence.</summary>
    public const int MinimumValue = 0;

    /// <summary>The highest rate or level value.</summary>
    public const int MaximumValue = 99;

    /// <summary>How many level units halve the amplitude - the 0-99 scale's one defining number.</summary>
    public const double LevelUnitsPerHalving = 8.0;

    /// <summary>What one level unit is worth in decibels, which is <c>6.02 / 8</c>.</summary>
    public const double DecibelsPerLevelUnit = 6.020599913279624 / LevelUnitsPerHalving;

    /// <summary>
    /// How long a rate of <see cref="MaximumValue" /> takes to move the whole 0-99 level range, in
    /// seconds.
    /// </summary>
    /// <remarks>
    /// Anchored on the measurement's two well-resolved points rather than on the hardware's published
    /// figure: rate 50 reached half its final level in 32 ms and 99 % of it in 78 ms, and rate 20 in
    /// 1.449 s and 1.991 s. Extrapolating those through <see cref="RateUnitsPerHalving" /> puts the
    /// full sweep at rate 99 here, which the measurement could only bound ("below the 12 ms analysis
    /// resolution").
    /// </remarks>
    public const double FullSweepSecondsAtMaximumRate = 0.000125;

    /// <summary>How many rate units halve the time a stage takes.</summary>
    /// <remarks>
    /// MEASURED (round 2, item 27): from rate 20 to rate 50 the time fell by a factor of 45 over
    /// 30 rate units, which is 5.45 units per halving. The hardware's own structure would have given
    /// <c>4 * 64 / 41</c> = 6.24.
    /// </remarks>
    public const double RateUnitsPerHalving = 5.5;

    /// <summary>
    /// How long a rate of <see cref="MinimumValue" /> takes to move the whole level range, in seconds.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 2, item 27): RATE 0 CRAWLS, IT DOES NOT HOLD - it reached half its final level
    /// in 2.634 s and 99 % of it in 5.481 s, where the format's own description promises a hold. The
    /// halving law would have put rate 0 at half a minute, so it is a special case rather than the
    /// bottom of the scale.
    /// </remarks>
    public const double FullSweepSecondsAtMinimumRate = 5.5;

    /// <summary>The largest magnitude the <c>fmOpNDetune</c> attribute takes.</summary>
    public const int MaximumDetune = 7;

    /// <summary>The coefficient of the measured detune law.</summary>
    /// <remarks>
    /// MEASURED (round 3, item 42): <c>offsetHz = detune * 0.005341 * f0^0.630</c>, fitted over six
    /// octaves to within 3 %. That is 2.47 cents per step at MIDI 24 falling to 0.69 at MIDI 84 - so
    /// detune is neither a fixed number of Hertz nor a fixed interval, which is why a single "Hz per
    /// step" constant could not describe it.
    /// </remarks>
    public const double DetuneCoefficient = 0.005341;

    /// <summary>The exponent of the measured detune law. See <see cref="DetuneCoefficient" />.</summary>
    public const double DetuneFrequencyExponent = 0.630;

    /// <summary>The largest value <c>fmOpNVelocitySensitivity</c> takes.</summary>
    public const int MaximumVelocitySensitivity = 7;

    /// <summary>The highest MIDI velocity.</summary>
    public const int MaximumVelocity = 127;

    /// <summary>
    /// The velocity at which <c>fmOpNVelocitySensitivity</c> costs nothing at all.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 2, item 27): both measured sensitivities pass through 0 dB at velocity 96, not
    /// at 127 - above it the operator is pushed UP rather than merely left alone.
    /// </remarks>
    public const int NeutralVelocity = 96;

    // MEASURED: the five points of the velocity table, in decibels PER UNIT of sensitivity. The
    // offsets at sensitivity 7 were 2.32 to 2.41 times those at sensitivity 3, so the table is
    // scaled by the sensitivity number rather than reshaped by it, and sensitivity 7 at velocity 1
    // is complete silence. Between the points the table is read linearly in velocity.
    private static readonly int[] VelocityPoints = [1, 32, 64, NeutralVelocity, MaximumVelocity];

    private static readonly double[] VelocityDecibelsPerUnit = [-11.2, -3.40, -1.50, 0.0, 0.74];

    /// <summary>The largest rate-scaling value.</summary>
    public const int MaximumRateScaling = 7;

    /// <summary>
    /// How many extra rate units full rate scaling adds at the very top of the keyboard.
    /// UNMEASURED, and the format has no attribute for rate scaling at all - it exists so that a
    /// patch imported from hardware can keep its key-follow. See
    /// <see cref="Fm6OpOscillator.RateScaling" />.
    /// </summary>
    public const double RateUnitsAtFullRateScaling = 36.75;

    /// <summary>
    /// How long a stage running at this rate takes to cover the whole 0-99 level range.
    /// </summary>
    /// <param name="rate">The rate, 0 to 99; values outside are clamped.</param>
    /// <returns>
    /// The time in seconds, or <see cref="double.PositiveInfinity" /> for rate 0, which does not
    /// move at all.
    /// </returns>
    /// <remarks>
    /// The law is one halving every <see cref="RateUnitsPerHalving" /> rate units, anchored at
    /// <see cref="FullSweepSecondsAtMaximumRate" /> for rate 99. MEASURED: rate 0 is NOT the hold the
    /// format's description promises - it crawls, covering the range in
    /// <see cref="FullSweepSecondsAtMinimumRate" /> seconds, far faster than the halving law's own
    /// extrapolation would give it.
    /// </remarks>
    public static double FullSweepSeconds(int rate)
    {
        rate = Clamp(rate);
        if (rate <= MinimumValue) { return FullSweepSecondsAtMinimumRate; }

        return FullSweepSecondsAtMaximumRate * Math.Pow(2.0, (MaximumValue - rate) / RateUnitsPerHalving);
    }

    /// <summary>
    /// How fast a stage running at this rate moves, in level units per second.
    /// </summary>
    /// <param name="rate">The rate, 0 to 99; values outside are clamped.</param>
    /// <returns>Level units per second; 0 for rate 0, which does not move.</returns>
    public static double LevelUnitsPerSecond(int rate)
    {
        double seconds = FullSweepSeconds(rate);
        if (double.IsPositiveInfinity(seconds) || seconds <= 0.0) { return 0.0; }

        return MaximumValue / seconds;
    }

    /// <summary>
    /// Converts a position on the 0-99 level scale to a linear amplitude.
    /// </summary>
    /// <param name="levelUnits">The level, 0 to 99; fractional values are allowed.</param>
    /// <returns>
    /// The amplitude: 1.0 at 99, half of that 8 units lower, and exactly 0 at or below 0.
    /// </returns>
    public static double LevelUnitsToAmplitude(double levelUnits)
    {
        if (double.IsNaN(levelUnits) || levelUnits <= 0.0) { return 0.0; }
        if (levelUnits >= MaximumValue) { return 1.0; }

        return Math.Pow(2.0, (levelUnits - MaximumValue) / LevelUnitsPerHalving);
    }

    /// <summary>
    /// Converts a linear amplitude back to a position on the 0-99 level scale.
    /// </summary>
    /// <param name="amplitude">The amplitude, 0 to 1.</param>
    /// <returns>The level, 0 to 99. Anything at or below silence is 0.</returns>
    public static double AmplitudeToLevelUnits(double amplitude)
    {
        if (double.IsNaN(amplitude) || amplitude <= 0.0) { return 0.0; }
        if (amplitude >= 1.0) { return MaximumValue; }

        double units = MaximumValue + (LevelUnitsPerHalving * Math.Log2(amplitude));
        return units <= 0.0 ? 0.0 : units;
    }

    /// <summary>
    /// What a detune setting shifts an operator's frequency by.
    /// </summary>
    /// <param name="detune">The <c>fmOpNDetune</c> value, -7 to +7; values outside are clamped.</param>
    /// <param name="frequencyHz">The operator's undetuned frequency in Hz.</param>
    /// <returns>The offset in Hz, added to the operator's frequency. Positive sharpens.</returns>
    /// <remarks>
    /// MEASURED (round 3, item 42) as <c>detune * 0.005341 * f0^0.630</c>: linear in the detune
    /// number and a power law in frequency. At middle C a step is 0.18 Hz, at MIDI 48 it is 0.115 Hz
    /// and at MIDI 72 it is 0.276 Hz - the offset grows by about 1.55 per octave, so it is neither
    /// constant in Hertz nor constant in cents.
    /// </remarks>
    public static double DetuneHz(int detune, double frequencyHz)
    {
        if (detune < -MaximumDetune) { detune = -MaximumDetune; }
        if (detune > MaximumDetune) { detune = MaximumDetune; }
        if (detune == 0 || !(frequencyHz > 0.0)) { return 0.0; }

        return detune * DetuneCoefficient * Math.Pow(frequencyHz, DetuneFrequencyExponent);
    }

    /// <summary>
    /// How much velocity scales an operator's level.
    /// </summary>
    /// <param name="sensitivity">The <c>fmOpNVelocitySensitivity</c> value, 0 to 7; clamped.</param>
    /// <param name="velocity">The MIDI velocity, 0 to 127; clamped.</param>
    /// <returns>
    /// A factor that multiplies the operator's level: always exactly 1 at sensitivity 0, exactly 1 at
    /// <see cref="NeutralVelocity" />, below 1 under it and ABOVE 1 over it.
    /// </returns>
    /// <remarks>
    /// MEASURED (round 2, item 27) as a fixed five-point decibel table scaled by the sensitivity
    /// number: <c>offset_dB = sensitivity * g(velocity)</c> with <c>g</c> reading -11.2, -3.40, -1.50,
    /// 0 and +0.74 dB at velocities 1, 32, 64, 96 and 127. It is not a logarithm and not a cube root,
    /// and its neutral point is 96 rather than 127.
    /// </remarks>
    public static double VelocityScale(int sensitivity, int velocity)
    {
        if (sensitivity <= 0) { return 1.0; }
        if (sensitivity > MaximumVelocitySensitivity) { sensitivity = MaximumVelocitySensitivity; }
        if (velocity < 0) { velocity = 0; }
        if (velocity > MaximumVelocity) { velocity = MaximumVelocity; }

        double decibels = sensitivity * VelocityDecibels(velocity);
        return Math.Pow(10.0, decibels / 20.0);
    }

    /// <summary>
    /// The measured velocity table, in decibels per unit of <c>fmOpNVelocitySensitivity</c>.
    /// </summary>
    /// <param name="velocity">The MIDI velocity, 0 to 127; clamped.</param>
    /// <returns>The decibel offset one unit of sensitivity applies at that velocity.</returns>
    public static double VelocityDecibels(int velocity)
    {
        if (velocity <= VelocityPoints[0]) { return VelocityDecibelsPerUnit[0]; }

        for (int i = 1; i < VelocityPoints.Length; i++)
        {
            if (velocity > VelocityPoints[i]) { continue; }

            double span = VelocityPoints[i] - VelocityPoints[i - 1];
            double position = (velocity - VelocityPoints[i - 1]) / span;

            return VelocityDecibelsPerUnit[i - 1] +
                (position * (VelocityDecibelsPerUnit[i] - VelocityDecibelsPerUnit[i - 1]));
        }

        return VelocityDecibelsPerUnit[VelocityDecibelsPerUnit.Length - 1];
    }

    /// <summary>
    /// How many rate units key scaling adds at a given pitch.
    /// </summary>
    /// <param name="rateScaling">The rate-scaling amount, 0 to 7; clamped. 0 turns it off.</param>
    /// <param name="midiNote">The note being played, 0 to 127; clamped.</param>
    /// <returns>Rate units to add to every stage's rate, 0 or more.</returns>
    /// <remarks>
    /// Envelopes run faster the higher the note is played, which is how a hardware patch keeps its
    /// attack crisp at the top of the keyboard. UNMEASURED, and off by default.
    /// </remarks>
    public static double RateScalingUnits(int rateScaling, int midiNote)
    {
        if (rateScaling <= 0) { return 0.0; }
        if (rateScaling > MaximumRateScaling) { rateScaling = MaximumRateScaling; }
        if (midiNote < 0) { midiNote = 0; }
        if (midiNote > 127) { midiNote = 127; }

        return RateUnitsAtFullRateScaling * (rateScaling / (double)MaximumRateScaling) * (midiNote / 127.0);
    }

    /// <summary>
    /// The MIDI note a frequency is closest to, used by rate scaling when all the oscillator knows
    /// is a pitch in Hz.
    /// </summary>
    /// <param name="frequencyHz">The frequency in Hz.</param>
    /// <returns>The MIDI note number, 0 to 127, in equal temperament with A4 = 440 Hz.</returns>
    public static int FrequencyToMidiNote(double frequencyHz)
    {
        if (double.IsNaN(frequencyHz) || frequencyHz <= 0.0) { return 0; }

        double note = 69.0 + (12.0 * Math.Log2(frequencyHz / 440.0));
        if (note <= 0.0) { return 0; }
        if (note >= 127.0) { return 127; }

        return (int)Math.Round(note, MidpointRounding.AwayFromZero);
    }

    private static int Clamp(int value)
        => value < MinimumValue ? MinimumValue : value > MaximumValue ? MaximumValue : value;
}

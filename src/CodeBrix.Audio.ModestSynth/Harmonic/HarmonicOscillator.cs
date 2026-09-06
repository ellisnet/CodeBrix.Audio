using System;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Harmonic;

/// <summary>
/// An additive oscillator - the <c>harmonic</c> waveform. It sums up to 64 sine partials at whole
/// multiples of the note's frequency, each with its own level, shaped by a spectral tilt, an
/// odd/even balance and a loudness compensation.
/// </summary>
/// <remarks>
/// <para>
/// WITH NOTHING SET IT IS A SINE. That is measured behaviour: the reference player renders an
/// <c>&lt;oscillator waveform="harmonic"&gt;</c> carrying no <c>harmonic*</c> attributes as a pure
/// sine at the note's frequency and at the same level as <c>waveform="sine"</c> (measurements
/// document, item 13). Since every <c>harmonicPartialNLevel</c> defaults to 0, this oscillator
/// gets there by treating "no partial has a level" as "the fundamental is at full level".
/// Set ANY partial's level and that fallback stops: what you set is what sounds.
/// </para>
/// <para>
/// NO ALIASING, EVER. A partial whose frequency reaches Nyquist is simply not summed, so sweeping a
/// note up the keyboard silences partials one at a time instead of folding them back down. The
/// active set is recomputed whenever the pitch, the sample rate or any parameter changes.
/// </para>
/// <para>
/// EVERY PARAMETER IS PER-BLOCK. <see cref="NumPartials" />, <see cref="Tilt" />,
/// <see cref="OddEvenBalance" />, <see cref="Normalization" /> and every partial level can be
/// changed between blocks and are meant to be - they are the targets of the
/// <c>OSCILLATOR_HARMONIC_NUM_PARTIALS</c>, <c>OSCILLATOR_HARMONIC_TILT</c>,
/// <c>OSCILLATOR_HARMONIC_ODD_EVEN_BALANCE</c>, <c>OSCILLATOR_HARMONIC_NORMALIZATION</c> and
/// <c>OSCILLATOR_HARMONIC_PARTIAL_1_LEVEL</c> to <c>..._64_LEVEL</c> bindings. A change is applied
/// as a step at the start of the next block; the partials keep their phase across it, so nothing
/// clicks unless a level itself jumps.
/// </para>
/// <para>
/// COST. Only partials with a non-zero gain below Nyquist are summed, and each costs a multiply, a
/// wrap and a table lookup - not a call to <see cref="Math.Sin" />. All the arithmetic runs off
/// arrays allocated when the oscillator is built, so <see cref="Render" /> allocates nothing even
/// on the block that recomputes the gains.
/// </para>
/// <para>
/// The three shaping laws are documented on their properties. NONE of them is measured: the guide
/// gives each attribute's range and direction and nothing more, and the only reference recording of
/// this waveform is the no-attribute case above. They are stated here so a consumer can rely on
/// them, and they are the first thing a later measurement pass should re-fit.
/// </para>
/// </remarks>
public sealed class HarmonicOscillator : ModestOscillatorBase
{
    /// <summary>The most partials the waveform addresses - <c>harmonicPartial64Level</c> is the last.</summary>
    public const int MaximumPartials = 64;

    /// <summary>The <c>numPartials</c> default: eight active partials.</summary>
    public const int DefaultNumPartials = 8;

    /// <summary>The <c>harmonicOddEvenBalance</c> default: neither emphasis, both families at full level.</summary>
    public const double DefaultOddEvenBalance = 0.5;

    private readonly double[] levels = new double[MaximumPartials];
    private readonly int[] activeHarmonics = new int[MaximumPartials];
    private readonly double[] activeGains = new double[MaximumPartials];

    private int numPartials = DefaultNumPartials;
    private double tilt;
    private double oddEvenBalance = DefaultOddEvenBalance;
    private double normalization;
    private int activeCount;
    private bool gainsAreStale = true;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Harmonic;

    /// <summary>
    /// <c>numPartials</c>: how many partials are active, from 1 to <see cref="MaximumPartials" />.
    /// Default <see cref="DefaultNumPartials" />. Clamped.
    /// </summary>
    /// <remarks>
    /// It is a CEILING, not a count of what sounds: a partial inside the limit whose level is 0
    /// still contributes nothing. Partials above the limit are silent whatever their level.
    /// </remarks>
    public int NumPartials
    {
        get => numPartials;
        set
        {
            int clamped = value < 1 ? 1 : value > MaximumPartials ? MaximumPartials : value;
            if (clamped == numPartials) { return; }

            numPartials = clamped;
            gainsAreStale = true;
        }
    }

    /// <summary>
    /// <c>harmonicTilt</c>: spectral tilt from -1.0 to 1.0. Positive darkens, negative brightens.
    /// Default 0.0 (no tilt). Clamped; a non-finite value is ignored.
    /// </summary>
    /// <remarks>
    /// THE LAW, MEASURED against the reference player: partial <c>k</c> is multiplied by <c>k</c> to
    /// the power of <c>-2 * tilt</c>. That is a tilt of 12 dB per octave per unit, pivoting on the
    /// fundamental, so <c>tilt = 0.5</c> gives exactly the 1/k slope of a sawtooth and <c>tilt = 1</c>
    /// the 1/k-squared slope of a triangle. The fundamental is never changed by tilt, whatever the
    /// setting.
    /// A NEGATIVE tilt boosts hard - at <c>tilt = -1</c> partial 64 comes up by a factor of 4,096,
    /// which is 72 dB - so pair it with <see cref="Normalization" /> unless you want the level to
    /// climb with it.
    /// </remarks>
    public double Tilt
    {
        get => tilt;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { return; }

            double clamped = value < -1.0 ? -1.0 : value > 1.0 ? 1.0 : value;
            if (clamped == tilt) { return; }

            tilt = clamped;
            gainsAreStale = true;
        }
    }

    /// <summary>
    /// <c>harmonicOddEvenBalance</c>: crossfades emphasis between the odd partials (0.0) and the
    /// even ones (1.0). Default <see cref="DefaultOddEvenBalance" />, which emphasises neither.
    /// Clamped; a non-finite value is ignored.
    /// </summary>
    /// <remarks>
    /// THE LAW, which is chosen rather than measured: the odd partials are multiplied by
    /// <c>min(1, 2 * (1 - balance))</c> and the even ones by <c>min(1, 2 * balance)</c>. So 0.5
    /// leaves BOTH families at full level - it is genuinely neutral, not a half-and-half mix -
    /// while 0.0 silences the even partials and 1.0 silences the odd ones, each fading linearly
    /// over its half of the range. The fundamental counts as odd.
    /// </remarks>
    public double OddEvenBalance
    {
        get => oddEvenBalance;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { return; }

            double clamped = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
            if (clamped == oddEvenBalance) { return; }

            oddEvenBalance = clamped;
            gainsAreStale = true;
        }
    }

    /// <summary>
    /// <c>harmonicNormalization</c>: how much loudness compensation to apply to the summed
    /// partials, from 0.0 (none) to 1.0 (full). Default 0.0. Clamped; a non-finite value is
    /// ignored.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 2, item 26): the law is <c>(1 - n) + n / sum</c>, where <c>sum</c> is the sum
    /// of the partial gains - a PLAIN SUM DIVIDE, blended linearly toward unity, not a root of the
    /// summed squares and not a power law. Full compensation therefore holds the summed PEAK where a
    /// single full-level partial's would be rather than the RMS. A single partial at full level needs
    /// no compensation, so normalization never changes the plain sine this waveform defaults to.
    /// </remarks>
    public double Normalization
    {
        get => normalization;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { return; }

            double clamped = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
            if (clamped == normalization) { return; }

            normalization = clamped;
            gainsAreStale = true;
        }
    }

    /// <summary>
    /// Whether no partial has been given a level, so the oscillator is running as the pure sine
    /// the reference player produces for a bare <c>harmonic</c> oscillator.
    /// </summary>
    public bool IsPureSineFallback
    {
        get
        {
            for (int i = 0; i < MaximumPartials; i++)
            {
                if (levels[i] > 0.0) { return false; }
            }

            return true;
        }
    }

    /// <summary>
    /// How many partials are actually being summed: those inside <see cref="NumPartials" />, with a
    /// non-zero gain, below Nyquist.
    /// </summary>
    public int ActivePartialCount
    {
        get
        {
            EnsureGains();
            return activeCount;
        }
    }

    /// <summary>
    /// Reads one partial's level - the <c>harmonicPartialNLevel</c> attributes.
    /// </summary>
    /// <param name="partial">Which partial, 1 (the fundamental) to <see cref="MaximumPartials" />.</param>
    /// <returns>The level, 0.0 to 1.0. Every partial starts at 0.0, the format's default.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partial" /> is outside 1 to 64.</exception>
    public double GetPartialLevel(int partial)
    {
        ValidatePartial(partial);
        return levels[partial - 1];
    }

    /// <summary>
    /// Sets one partial's level - the <c>harmonicPartialNLevel</c> attributes.
    /// </summary>
    /// <param name="partial">Which partial, 1 (the fundamental) to <see cref="MaximumPartials" />.</param>
    /// <param name="level">The level, 0.0 to 1.0. Clamped; a non-finite value is ignored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partial" /> is outside 1 to 64.</exception>
    public void SetPartialLevel(int partial, double level)
    {
        ValidatePartial(partial);
        if (double.IsNaN(level) || double.IsInfinity(level)) { return; }

        double clamped = level < 0.0 ? 0.0 : level > 1.0 ? 1.0 : level;
        if (clamped == levels[partial - 1]) { return; }

        levels[partial - 1] = clamped;
        gainsAreStale = true;
    }

    /// <summary>
    /// Sets every partial's level to 0, which puts the oscillator back on its pure-sine fallback.
    /// </summary>
    public void ClearPartialLevels()
    {
        Array.Clear(levels, 0, levels.Length);
        gainsAreStale = true;
    }

    /// <summary>
    /// The gain a partial is actually being summed with: its level after the tilt, the odd/even
    /// balance, the Nyquist mute and the normalization.
    /// </summary>
    /// <param name="partial">Which partial, 1 to <see cref="MaximumPartials" />.</param>
    /// <returns>The effective gain; 0 when the partial is not sounding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partial" /> is outside 1 to 64.</exception>
    public double GetPartialGain(int partial)
    {
        ValidatePartial(partial);
        EnsureGains();

        for (int i = 0; i < activeCount; i++)
        {
            if (activeHarmonics[i] == partial) { return activeGains[i]; }
        }

        return 0.0;
    }

    /// <inheritdoc />
    public override void SetSampleRate(int sampleRate)
    {
        base.SetSampleRate(sampleRate);
        gainsAreStale = true;
    }

    /// <inheritdoc />
    public override void SetFrequency(double frequencyHz)
    {
        base.SetFrequency(frequencyHz);
        gainsAreStale = true;
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        if (buffer.Length == 0) { return; }

        EnsureGains();

        double phase = Phase;
        double increment = PhaseIncrement;
        int count = activeCount;

        if (count == 0)
        {
            // Everything is muted - by numPartials, by the levels, or by Nyquist - but the phase
            // still has to run, so a partial that comes back mid-note comes back in phase.
            buffer.Clear();
            phase += increment * buffer.Length;
            Phase = WrapPhase(phase);
            return;
        }

        int[] harmonics = activeHarmonics;
        double[] gains = activeGains;

        for (int i = 0; i < buffer.Length; i++)
        {
            double sum = 0.0;
            for (int p = 0; p < count; p++)
            {
                double partialPhase = harmonics[p] * phase;
                partialPhase -= Math.Floor(partialPhase);
                sum += gains[p] * SineTable.LookupWrapped(partialPhase);
            }

            buffer[i] = (float)sum;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
    }

    private void EnsureGains()
    {
        if (!gainsAreStale) { return; }

        bool anyLevelSet = false;
        for (int i = 0; i < MaximumPartials; i++)
        {
            if (levels[i] > 0.0) { anyLevelSet = true; break; }
        }

        double oddGain = 2.0 * (1.0 - oddEvenBalance);
        if (oddGain > 1.0) { oddGain = 1.0; }

        double evenGain = 2.0 * oddEvenBalance;
        if (evenGain > 1.0) { evenGain = 1.0; }

        double nyquist = SampleRate * 0.5;
        double fundamental = Frequency;
        double sumOfGains = 0.0;
        activeCount = 0;

        for (int k = 1; k <= numPartials; k++)
        {
            double level = anyLevelSet ? levels[k - 1] : (k == 1 ? 1.0 : 0.0);
            if (level <= 0.0) { continue; }

            if (fundamental > 0.0 && k * fundamental >= nyquist) { continue; }

            double gain = level;
            // MEASURED (round 2, item 26): a partial's tilt gain is h^(-2 * tilt), which is TWELVE
            // decibels per octave per unit of tilt, not the six the first reading assumed.
            if (tilt != 0.0) { gain *= Math.Pow(k, -2.0 * tilt); }
            gain *= (k & 1) == 1 ? oddGain : evenGain;

            if (gain <= 0.0) { continue; }

            activeHarmonics[activeCount] = k;
            activeGains[activeCount] = gain;
            activeCount++;
            sumOfGains += gain;
        }

        // MEASURED (round 2, item 26): harmonicNormalization is (1 - n) + n / sum(levels), a plain
        // sum divide blended linearly toward unity. The sum used here is the sum of the EFFECTIVE
        // gains, after tilt and the odd/even balance: the measurement's own cases had neither, so the
        // two readings agree there, and taking the effective gains is what keeps a tilted patch from
        // drifting in level.
        if (normalization > 0.0 && sumOfGains > 0.0)
        {
            double applied = (1.0 - normalization) + (normalization / sumOfGains);
            for (int i = 0; i < activeCount; i++) { activeGains[i] *= applied; }
        }

        gainsAreStale = false;
    }

    private static void ValidatePartial(int partial)
    {
        if (partial < 1 || partial > MaximumPartials)
        {
            throw new ArgumentOutOfRangeException(nameof(partial), partial,
                "A harmonic oscillator has partials 1 to " + MaximumPartials + ".");
        }
    }
}

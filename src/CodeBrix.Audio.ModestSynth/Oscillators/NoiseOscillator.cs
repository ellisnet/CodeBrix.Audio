using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// White noise - the <c>noise</c> waveform, also spelled <c>white_noise</c>. Percussion textures,
/// hi-hats, wind, and grit layered under a pitched sound.
/// </summary>
/// <remarks>
/// <para>
/// Every sample is drawn independently and uniformly from [-<see cref="Amplitude" />,
/// <see cref="Amplitude" />), and the draw is then passed through the ANTI-IMAGING ROLLOFF the
/// reference player's own noise carries. There is no pitch, so
/// <see cref="IModestOscillator.SetFrequency" /> changes nothing about the sound - it is accepted
/// and recorded so a voice can drive every oscillator the same way.
/// </para>
/// <para>
/// THE ROLLOFF IS MEASURED (round 2, item 29). A Welch power spectrum of the reference's noise,
/// smoothed to third-octave bands, is flat to within 0.4 dB from 63 Hz to 8 kHz and then falls
/// away: -6.07 dB at 16 kHz and -26.99 dB at 20 kHz. TWO cascaded biquads - a double zero at
/// Nyquist over a resonant pole pair, applied twice - reproduce that table to within 0.8 dB at every
/// band. The pole is a fraction of the sample rate, so the shape holds at any rate, and the filter's
/// 2.66 dB of noise power is given back by <see cref="RolloffCompensation" /> so that the measured
/// RMS survives it.
/// </para>
/// <para>
/// THE ONE RESIDUAL. The same recording's POWER CENTROID was reported at 8877 Hz, and this filter
/// puts it at 7769 - 1.16 dB darker, inside the 2 dB the plan asks of an oscillator's brightness but
/// not on the nose. The two reference figures cannot both be met: read literally, the band table
/// alone implies a centroid near 6.5 kHz, so the reference's true response between 8 and 16 kHz must
/// fall later and faster than a straight line through its own bands. The band table is the direct
/// measurement and is what this fit follows.
/// </para>
/// <para>
/// The default level is MEASURED rather than guessed. Recorded from the reference player, a
/// <c>noise</c> oscillator's RMS sits 2.68 dB below a full-amplitude <c>sine</c> oscillator's,
/// which is what <see cref="ReferenceAmplitude" /> reproduces. (Measurements document, round 2
/// item 29, refining round 1's 2.30 dB from the same recordings measured over a longer window.) Set
/// <see cref="Amplitude" /> to 1.0 for a full-scale noise source instead.
/// </para>
/// <para>
/// The sequence is deterministic: the same <see cref="Seed" /> renders the same samples, every
/// run, on every platform. <see cref="IModestOscillator.Reset" /> restarts it from the seed and
/// ignores the phase it is handed, because noise has no phase. Give layered voices DIFFERENT
/// seeds; identical seeds render identical noise, which sums to one louder copy rather than to a
/// wider sound.
/// </para>
/// </remarks>
public sealed class NoiseOscillator : ModestOscillatorBase
{
    /// <summary>The seed a new oscillator starts with.</summary>
    public const uint DefaultSeed = 0x5EED1234u;

    /// <summary>
    /// The peak amplitude that puts this oscillator's RMS 2.68 dB below a full-amplitude sine's,
    /// which is where the reference player's noise oscillator sits. Uniform noise over
    /// [-A, A) has an RMS of A / sqrt(3), so the resulting RMS is about 0.519.
    /// </summary>
    public const double ReferenceAmplitude = 0.8996;

    /// <summary>Where the rolloff's pole pair sits, as a fraction of the sample rate.</summary>
    /// <remarks>
    /// Fitted to the measured third-octave table: 16.05 kHz at 44.1 kHz. The ZEROS sit at Nyquist
    /// itself, and the whole biquad runs twice.
    /// </remarks>
    public const double RolloffPoleFraction = 16050.0 / 44100.0;

    /// <summary>How far the rolloff's poles sit from the unit circle.</summary>
    public const double RolloffPoleRadius = 0.58;

    /// <summary>
    /// What the rolloff filter's own loss is given back as, so that the measured RMS survives it.
    /// </summary>
    /// <remarks>
    /// The cascade's power gain over a flat spectrum is 0.7361, which is 2.66 dB; this is its inverse
    /// in amplitude. It does not depend on the sample rate, because the response is a function of
    /// frequency OVER the sample rate and the noise is flat across the whole band.
    /// </remarks>
    public const double RolloffCompensation = 1.1655362;

    private static readonly double RolloffB0 = RolloffGain();
    private static readonly double RolloffB1 = RolloffB0 * 2.0;
    private static readonly double RolloffA1 =
        -2.0 * RolloffPoleRadius * Math.Cos(2.0 * Math.PI * RolloffPoleFraction);
    private static readonly double RolloffA2 = RolloffPoleRadius * RolloffPoleRadius;

    private uint seed = DefaultSeed;
    private double amplitude = ReferenceAmplitude;
    private ModestRandom random = new ModestRandom(DefaultSeed);
    private double rolloffX1;
    private double rolloffX2;
    private double rolloffY1;
    private double rolloffY2;
    private double rolloffX3;
    private double rolloffX4;
    private double rolloffY3;
    private double rolloffY4;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Noise;

    /// <summary>
    /// The peak amplitude of the noise, from 0.0 to 1.0. Defaults to
    /// <see cref="ReferenceAmplitude" />; set 1.0 for full scale. Clamped; a non-finite value is
    /// ignored.
    /// </summary>
    public double Amplitude
    {
        get => amplitude;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { return; }
            amplitude = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }
    }

    /// <summary>
    /// The seed the noise sequence starts from. Setting it restarts the sequence immediately, as
    /// well as fixing where <see cref="IModestOscillator.Reset" /> will restart it.
    /// </summary>
    public uint Seed
    {
        get => seed;
        set
        {
            seed = value;
            random.Reseed(value);
        }
    }

    /// <summary>
    /// Restarts the noise sequence from <see cref="Seed" />.
    /// </summary>
    /// <param name="phase">Ignored: noise has no phase.</param>
    public override void Reset(double phase)
    {
        base.Reset(phase);
        random.Reseed(seed);
        rolloffX1 = 0.0;
        rolloffX2 = 0.0;
        rolloffY1 = 0.0;
        rolloffY2 = 0.0;
        rolloffX3 = 0.0;
        rolloffX4 = 0.0;
        rolloffY3 = 0.0;
        rolloffY4 = 0.0;
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        double level = amplitude * RolloffCompensation;
        double x1 = rolloffX1;
        double x2 = rolloffX2;
        double y1 = rolloffY1;
        double y2 = rolloffY2;
        double x3 = rolloffX3;
        double x4 = rolloffX4;
        double y3 = rolloffY3;
        double y4 = rolloffY4;

        for (int i = 0; i < buffer.Length; i++)
        {
            double x = level * random.NextBipolar();

            double y = (RolloffB0 * x) + (RolloffB1 * x1) + (RolloffB0 * x2) -
                       (RolloffA1 * y1) - (RolloffA2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;

            double z = (RolloffB0 * y) + (RolloffB1 * x3) + (RolloffB0 * x4) -
                       (RolloffA1 * y3) - (RolloffA2 * y4);
            x4 = x3;
            x3 = y;
            y4 = y3;
            y3 = z;

            buffer[i] = (float)z;
        }

        rolloffX1 = x1;
        rolloffX2 = x2;
        rolloffY1 = y1;
        rolloffY2 = y2;
        rolloffX3 = x3;
        rolloffX4 = x4;
        rolloffY3 = y3;
        rolloffY4 = y4;
    }

    // The gain that puts the rolloff biquad at unity at direct current, so that only its shape near
    // Nyquist is felt. The numerator is (1, 2, 1) - a double zero at Nyquist - so its direct-current
    // response is 4, and the denominator's is 1 + a1 + a2.
    private static double RolloffGain()
    {
        double pole = 1.0 +
            (-2.0 * RolloffPoleRadius * Math.Cos(2.0 * Math.PI * RolloffPoleFraction)) +
            (RolloffPoleRadius * RolloffPoleRadius);

        return pole / 4.0;
    }
}

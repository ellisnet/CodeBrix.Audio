using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// A plucked string - the <c>pluck1</c> waveform. Unlike the cycling shapes, it is excited once at
/// note-on and then decays on its own, which is what makes it suit guitars, basses, harps, koto
/// and the rest of the plucked family.
/// </summary>
/// <remarks>
/// <para>
/// It is a Karplus-Strong waveguide: a delay line one period long, fed back through a low-pass, so
/// high partials die away faster than low ones exactly as they do on a real string. The line is
/// filled at note-on with the excitation <see cref="PluckType" /> asks for, and
/// <see cref="Damping" /> sets how long what is left takes to fade.
/// </para>
/// <para>
/// Two things about it are calibrated against recordings of the reference player rather than
/// guessed: how long a given <see cref="Damping" /> rings, and how bright the string is. On the
/// five pluck1 cases the measurements document (item 12) records, this model's power-weighted
/// spectral centroid sits within 1.4 dB of the reference's, and its -30 dB decay time at
/// <c>damping="0.1"</c> within a tenth of a second. What that took is a four-stage low-pass in
/// the loop rather than the textbook single averaging stage, and an excitation shaped like a
/// string picked a tenth of the way along rather than one picked in the middle.
/// </para>
/// <para>
/// Pitch is tuned with a first-order all-pass inside the loop, so the fundamental lands on the
/// requested frequency rather than on the nearest whole number of samples.
/// </para>
/// <para>
/// Deterministic: the same <see cref="Seed" />, <see cref="PluckType" /> and pitch produce the same
/// pluck every time. Give layered voices different seeds, or different reset phases, so they do
/// not render the same string twice.
/// </para>
/// </remarks>
public sealed class Pluck1Oscillator : ModestOscillatorBase
{
    /// <summary>The <c>damping</c> value a new oscillator starts at, matching the format's default.</summary>
    public const double DefaultDamping = 0.5;

    /// <summary>The <c>pluckType</c> value a new oscillator starts at, matching the format's default.</summary>
    public const double DefaultPluckType = 0.5;

    /// <summary>The seed a new oscillator starts with.</summary>
    public const uint DefaultSeed = 0x51A17EEDu;

    /// <summary>
    /// The lowest pitch the string can be tuned to, in Hz - below MIDI note 0, so every note a
    /// keyboard can send is in range.
    /// </summary>
    public const double MinimumFrequency = 8.0;

    /// <summary>
    /// The highest pitch the string can be tuned to, as a fraction of the sample rate. A third of
    /// 44,100 is 14.7 kHz, which is above MIDI note 127.
    /// </summary>
    public const double MaximumFrequencyFraction = 1.0 / 3.0;

    /// <summary>
    /// The fraction of its amplitude the string keeps on each trip round the waveguide at
    /// <c>damping="0"</c> - the most heavily damped setting.
    /// </summary>
    /// <remarks>
    /// MEASURED, with <see cref="MaximumLoopGain" /> and <see cref="DampingCurveExponent" />, from
    /// the reference's decay envelopes at <c>damping="0.1"</c>, <c>"0.5"</c> and <c>"0.9"</c>
    /// (round 2, item 29). See <see cref="Damping" /> for the law.
    /// </remarks>
    public const double MinimumLoopGain = 0.96702;

    /// <summary>
    /// The fraction of its amplitude the string keeps on each trip round the waveguide at
    /// <c>damping="1"</c> - the least damped setting.
    /// </summary>
    public const double MaximumLoopGain = 0.996734;

    /// <summary>
    /// How sharply the loss per trip falls as <c>damping</c> rises.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 2, item 29). The reference's -60 dB times are 0.62 s at <c>damping="0.1"</c>,
    /// 1.03 s at <c>"0.5"</c> and 4.3 s at <c>"0.9"</c>, each at its own pitch. Turning those into a
    /// loss per trip and fitting gives an exponent of 2.93 on <c>(1 - damping)</c>, so the loss is
    /// CUBIC in the distance below full damping rather than linear in the gain: a linear reading is
    /// more than twice too fast in the middle of the range.
    /// </remarks>
    public const double DampingCurveExponent = 3.0;

    // Below this the double arithmetic is producing denormals, which are slow and inaudible alike.
    private const double DenormalFloor = 1e-25;

    // How dark the string is. Each stage is a two-point average - half a sample of delay and a
    // cos(w/2) magnitude - so the cascade is a linear-phase low-pass whose order sets how quickly
    // the upper partials die. Four stages, with the pick a tenth of the way along the string, put
    // the power-weighted spectral centroid within 1.4 dB of the reference player's on all five of
    // its measured pluck1 cases (measurements document, item 12); one stage, the textbook
    // Karplus-Strong loop, is 12 to 19 dB too bright on the same cases.
    internal const int MaximumLoopFilterOrder = 8;
    internal const int DefaultLoopFilterOrder = 4;
    internal const double DefaultPickPosition = 0.10;

    // A loop that never loses anything would ring for ever - or grow - so cap what the
    // averaging-filter compensation below is allowed to ask for.
    private const double LoopGainCeiling = 0.9995;

    private float[] line;
    private int writeIndex;
    private int delaySamples = 1;
    private double allpassCoefficient;
    private double allpassLastInput;
    private double allpassLastOutput;
    private readonly double[] averageLastInputs = new double[MaximumLoopFilterOrder];
    private int loopFilterOrder = DefaultLoopFilterOrder;
    private double pickPosition = DefaultPickPosition;
    private double loopGain;
    private double decaySeconds = 1.0;
    private double blockPeak;

    private double damping = DefaultDamping;
    private double pluckType = DefaultPluckType;
    private uint seed = DefaultSeed;
    private ModestRandom random = new ModestRandom(DefaultSeed);

    /// <summary>
    /// Where along the string the excitation peaks, 0 to 1 - the brightness of a pluck.
    /// Calibration only; see <see cref="DefaultPickPosition" />.
    /// </summary>
    internal double PickPosition
    {
        get => pickPosition;
        set => pickPosition = value;
    }

    /// <summary>
    /// How many two-point averaging stages sit in the feedback loop. Calibration only; see
    /// <see cref="DefaultLoopFilterOrder" />.
    /// </summary>
    internal int LoopFilterOrder
    {
        get => loopFilterOrder;
        set { loopFilterOrder = value; UpdateTuning(); }
    }

    /// <summary>Creates a string tuned to the base class defaults and ready to be plucked.</summary>
    public Pluck1Oscillator()
    {
        line = new float[RequiredCapacity(DefaultSampleRate)];
        UpdateTuning();
    }

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Pluck1;

    /// <summary>
    /// The format's <c>damping</c> attribute: how long the string rings, from 0.0 to 1.0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0.0 is heavily damped and short, 1.0 is barely damped and long. It sets how much of its
    /// amplitude the string keeps on each trip round the waveguide, straight-line between
    /// <see cref="MinimumLoopGain" /> and <see cref="MaximumLoopGain" />, so the -60 dB time it
    /// produces depends on the pitch the way a real string's does: a low note rings longer than a
    /// high one at the same setting. <see cref="DecayTimeSeconds" /> reports what the current
    /// setting and pitch come to. Values outside 0 to 1 are clamped. Takes effect on the next
    /// rendered sample, not only at note-on.
    /// </para>
    /// <para>
    /// The two end points are calibrated against the reference player, not guessed: recorded at
    /// <c>damping="0.1"</c> on an 82.4 Hz note, its string fell 30 dB in 1.56 s, which is a -60 dB
    /// time of 3.1 s and a loop gain of 0.9733 (measurements document, item 12). The same document
    /// records <c>damping="0.5"</c> and <c>damping="0.9"</c> as still ringing above -30 dB after
    /// two seconds, which these end points also satisfy - but they are lower bounds, so the upper
    /// half of the range is DIRECTION-measured rather than value-measured.
    /// </para>
    /// </remarks>
    public double Damping
    {
        get => damping;
        set
        {
            if (double.IsNaN(value)) { return; }
            damping = Clamp01(value);
            UpdateTuning();
        }
    }

    /// <summary>
    /// The format's <c>pluckType</c> attribute: what the string is struck with, from 0.0 to 1.0.
    /// </summary>
    /// <remarks>
    /// 0.0 excites the string with a smooth triangle, which is soft and mellow; 1.0 excites it with
    /// a noise burst, which is bright and aggressive; values between blend the two linearly. The
    /// default is <see cref="DefaultPluckType" />. Values outside 0 to 1 are clamped into it. Only
    /// read at note-on, so changing it mid-note does nothing until the next
    /// <see cref="IModestOscillator.Reset" />.
    /// </remarks>
    public double PluckType
    {
        get => pluckType;
        set
        {
            if (double.IsNaN(value)) { return; }
            pluckType = Clamp01(value);
        }
    }

    /// <summary>
    /// The seed the excitation noise is drawn from. Only read at note-on.
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
    /// How long the string currently takes to fade by 60 dB, in seconds - what
    /// <see cref="Damping" /> comes to at the pitch it is currently tuned to. Higher pitches ring
    /// for less time at the same damping, as they do on a real instrument.
    /// </summary>
    public double DecayTimeSeconds => decaySeconds;

    /// <summary>The level below which the string counts as having stopped, as a peak amplitude.</summary>
    /// <remarks>
    /// 100 dB below full scale. A waveguide never reaches exactly zero, so a plucked string needs a
    /// floor to be able to say it has finished; this one is far below anything a listener or a
    /// 24-bit file can hold.
    /// </remarks>
    public const double SilenceThreshold = 1e-5;

    /// <inheritdoc />
    /// <remarks>
    /// A plucked string decays whether the key is held or not, so this reports on the STRING and
    /// ignores the key: it is true once the last block rendered peaked below
    /// <see cref="SilenceThreshold" />, and true before the first <see cref="Reset" /> because a
    /// string that was never plucked has nothing to play.
    /// </remarks>
    public override bool IsFinished => blockPeak < SilenceThreshold;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is not positive.</exception>
    public override void SetSampleRate(int sampleRate)
    {
        base.SetSampleRate(sampleRate);

        int required = RequiredCapacity(sampleRate);
        if (line.Length < required)
        {
            // The only allocation in the whole class, and it happens before rendering starts.
            line = new float[required];
        }
        else
        {
            Array.Clear(line, 0, line.Length);
        }

        writeIndex = 0;
        UpdateTuning();
    }

    /// <summary>
    /// Tunes the string.
    /// </summary>
    /// <param name="frequencyHz">
    /// The pitch in Hz, from <see cref="MinimumFrequency" /> up to
    /// <see cref="MaximumFrequencyFraction" /> of the sample rate.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="frequencyHz" /> is outside that range or is not a finite number. A string
    /// shorter than a handful of samples has no pitch to speak of, which is why the upper limit is
    /// there.
    /// </exception>
    /// <remarks>
    /// <para>Retuning does not restart the note; the ringing string simply changes length.</para>
    /// <para>
    /// The range covers every MIDI note at every ordinary sample rate. Pitch is accurate to better
    /// than a twentieth of a percent up to about 4 kHz; above that the string is only a few samples
    /// long, and the low-pass in its own feedback loop silences it within milliseconds - which is
    /// what a plucked string an inch long would do too.
    /// </para>
    /// </remarks>
    public override void SetFrequency(double frequencyHz)
    {
        double maximum = SampleRate * MaximumFrequencyFraction;
        if (double.IsNaN(frequencyHz) || double.IsInfinity(frequencyHz)
            || frequencyHz < MinimumFrequency || frequencyHz > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), frequencyHz,
                "A plucked string can be tuned from " + MinimumFrequency.ToString("0.#") +
                " Hz up to a third of the sample rate.");
        }

        base.SetFrequency(frequencyHz);
        UpdateTuning();
    }

    /// <summary>
    /// Plucks the string: fills the waveguide with a fresh excitation and starts it ringing.
    /// </summary>
    /// <param name="phase">
    /// Where in the string the excitation starts, in cycles - the <c>randomPhase</c> behaviour.
    /// A plucked string has no running phase to randomise, so this rotates the excitation instead,
    /// which is what decorrelates layered plucks of the same string.
    /// </param>
    public override void Reset(double phase)
    {
        base.Reset(phase);

        blockPeak = 1.0;
        random.Reseed(seed);
        Array.Clear(line, 0, line.Length);

        int length = delaySamples + 1;
        int rotation = (int)Math.Round(Phase * length);
        if (rotation >= length) { rotation = 0; }

        double sum = 0.0;
        double blend = pluckType;

        for (int i = 0; i < length; i++)
        {
            double position = (double)i / length;

            // The shape a real string takes when it is pulled aside and let go: a triangle with its
            // apex at the pick position and zero at both ends, so it meets itself where the delay
            // line wraps and a pluckType of 0 introduces no step of its own.
            double triangle = position < pickPosition
                ? position / pickPosition
                : (1.0 - position) / (1.0 - pickPosition);
            double noise = random.NextBipolar();
            double value = ((1.0 - blend) * triangle) + (blend * noise);

            int target = i + rotation;
            if (target >= length) { target -= length; }
            line[target] = (float)value;
            sum += value;
        }

        // A string with a standing DC component decays as an audible thump; take the mean out.
        float mean = (float)(sum / length);
        for (int i = 0; i < length; i++)
        {
            line[i] -= mean;
        }

        writeIndex = length;
        if (writeIndex >= line.Length) { writeIndex = 0; }

        allpassLastInput = 0.0;
        allpassLastOutput = 0.0;
        Array.Clear(averageLastInputs, 0, averageLastInputs.Length);
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        float[] delayLine = line;
        int capacity = delayLine.Length;
        int write = writeIndex;
        int delay = delaySamples;
        double coefficient = allpassCoefficient;
        double lastInput = allpassLastInput;
        double lastOutput = allpassLastOutput;
        double[] averages = averageLastInputs;
        int order = loopFilterOrder;
        double gain = loopGain;
        double peak = 0.0;

        for (int i = 0; i < buffer.Length; i++)
        {
            int read = write - delay;
            if (read < 0) { read += capacity; }

            double raw = delayLine[read];

            // First-order all-pass, y[n] = c*x[n] + x[n-1] - c*y[n-1]: the fractional part of the
            // delay, with no effect on the magnitude of anything travelling through it.
            double tuned = (coefficient * raw) + lastInput - (coefficient * lastOutput);
            lastInput = raw;
            lastOutput = tuned;

            // A cascade of two-point averages: half a sample of delay each at every frequency, and
            // the low-pass that gives a plucked string its darkening decay.
            double filtered = tuned;
            for (int stage = 0; stage < order; stage++)
            {
                double input = filtered;
                filtered = 0.5 * (input + averages[stage]);
                averages[stage] = input;
            }

            double value = gain * filtered;
            if (value > -DenormalFloor && value < DenormalFloor) { value = 0.0; }

            delayLine[write] = (float)value;
            write++;
            if (write >= capacity) { write = 0; }

            if (value > peak) { peak = value; }
            else if (-value > peak) { peak = -value; }

            buffer[i] = (float)value;
        }

        writeIndex = write;
        allpassLastInput = lastInput;
        allpassLastOutput = lastOutput;
        blockPeak = peak;
    }

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

    private static int RequiredCapacity(int sampleRate)
        => (int)Math.Ceiling(sampleRate / MinimumFrequency) + 4;

    private void UpdateTuning()
    {
        double frequency = Frequency;
        if (frequency < MinimumFrequency) { frequency = MinimumFrequency; }

        // One period, less the half sample each averaging stage already contributes.
        double total = (SampleRate / frequency) - (0.5 * loopFilterOrder);

        int integerPart = (int)Math.Floor(total);
        double fraction = total - integerPart;

        // An all-pass is ill-behaved at fractional delays close to zero; borrow a whole sample.
        if (fraction < 0.1 && integerPart > 1)
        {
            integerPart -= 1;
            fraction += 1.0;
        }

        int maximum = line.Length - 2;
        if (integerPart > maximum) { integerPart = maximum; }
        if (integerPart < 1) { integerPart = 1; }

        delaySamples = integerPart;

        // The all-pass coefficient that puts its PHASE DELAY at the fundamental exactly on the
        // fractional part - the tuning formula from the plucked-string literature. The simpler
        // (1-f)/(1+f) is the same expression in the limit of a low frequency, and it drifts sharp
        // by several percent once the string is only a dozen samples long.
        double omega = 2.0 * Math.PI * frequency / SampleRate;
        double denominator = Math.Sin((1.0 + fraction) * omega * 0.5);
        allpassCoefficient = Math.Abs(denominator) < 1e-9
            ? (1.0 - fraction) / (1.0 + fraction)
            : Math.Sin((1.0 - fraction) * omega * 0.5) / denominator;

        // MEASURED (round 2, item 29): the loss per trip - the decades of amplitude lost, not the
        // gain - falls as the CUBE of (1 - damping). The averaging filter takes a bite of its own at
        // the fundamental, so the gain compensates for it and the loss per trip is the one damping
        // asked for, up to the ceiling above which the loop would stop losing anything at all.
        double leastLoss = -Math.Log10(MaximumLoopGain);
        double mostLoss = -Math.Log10(MinimumLoopGain);
        double loss = leastLoss +
            ((mostLoss - leastLoss) * Math.Pow(1.0 - damping, DampingCurveExponent));
        double perLoop = Math.Pow(10.0, -loss);

        double averageMagnitude = Math.Pow(Math.Cos(Math.PI * frequency / SampleRate), loopFilterOrder);
        if (averageMagnitude < 1e-3) { averageMagnitude = 1e-3; }

        double gain = perLoop / averageMagnitude;
        if (gain > LoopGainCeiling) { gain = LoopGainCeiling; }
        if (gain < 0.0) { gain = 0.0; }
        loopGain = gain;

        // Report the -60 dB time the loop ACTUALLY has, ceiling and all, rather than the one the
        // damping value alone would imply.
        double actualPerLoop = gain * averageMagnitude;
        decaySeconds = actualPerLoop >= 1.0
            ? double.PositiveInfinity
            : 3.0 / (frequency * -Math.Log10(actualPerLoop));
    }
}

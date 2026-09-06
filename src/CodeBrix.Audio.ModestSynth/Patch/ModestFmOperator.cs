using System;

namespace CodeBrix.Audio.ModestSynth.Patch;

/// <summary>
/// One of the six FM operators of the <c>fm6op</c> waveform, with the parameters the
/// <c>fmOpN...</c> group attributes carry.
/// </summary>
/// <remarks>
/// <para>
/// The properties mirror the attribute names one for one, minus the <c>fmOpN</c> prefix:
/// <c>fmOp3Ratio</c> is <see cref="Ratio" /> on operator 3, and so on. Defaults are the format's
/// documented defaults, so a freshly created operator behaves as one whose attributes were all
/// left out.
/// </para>
/// <para>
/// Values outside a documented range are clamped rather than rejected, which is how a preset's
/// out-of-range attribute is treated everywhere in the family.
/// </para>
/// <para>
/// ONE DEFAULT DIFFERS FROM THE FORMAT'S ATTRIBUTE TABLE. That table gives every operator a
/// default <see cref="Level" /> of 1.0; here only operator 1 does, and operators 2 to 6 start
/// silent. Two things say the table is wrong: the reference player renders a PURE SINE for an
/// oscillator carrying no <c>fm*</c> attributes, which six operators at full level through
/// algorithm 1 could not produce, and the format's own tutorial tells you to "gradually raise
/// fmOp2Level from 0 to 1 to hear FM modulation build from a sine wave", which only makes sense
/// if operator 2 starts at 0. A preset that names its levels is unaffected either way.
/// </para>
/// </remarks>
public sealed class ModestFmOperator
{
    /// <summary>The number of operators an <c>fm6op</c> oscillator has.</summary>
    public const int OperatorCount = 6;

    /// <summary>The lowest and highest <c>fmOpNDetune</c> value, in the hardware's own units.</summary>
    public const int MinimumDetune = -7;

    /// <summary>The highest <c>fmOpNDetune</c> value.</summary>
    public const int MaximumDetune = 7;

    /// <summary>The highest <c>fmOpNVelocitySensitivity</c> value; 0 is no velocity response.</summary>
    public const int MaximumVelocitySensitivity = 7;

    /// <summary>The highest value a four-stage envelope rate or level can take.</summary>
    public const int MaximumEnvelopeValue = 99;

    /// <summary>
    /// The <c>fmOpNRelease</c> and <c>fmOpNAttack</c> value that means "no envelope of my own -
    /// let the group's envelope decide".
    /// </summary>
    public const double OuterEnvelopeSentinel = -1.0;

    private double ratio = 1.0;
    private int detune;
    private double fixedFrequency = 440.0;
    private double level;
    private int velocitySensitivity;
    private double feedback;
    private double attack;
    private double decay;
    private double sustain = 1.0;
    private double release = OuterEnvelopeSentinel;
    private int egRate1 = MaximumEnvelopeValue;
    private int egRate2 = MaximumEnvelopeValue;
    private int egRate3;
    private int egRate4 = MaximumEnvelopeValue;
    private int egLevel1 = MaximumEnvelopeValue;
    private int egLevel2 = MaximumEnvelopeValue;
    private int egLevel3 = MaximumEnvelopeValue;
    private int egLevel4;

    /// <summary>Creates an operator carrying every documented default.</summary>
    /// <param name="number">Which operator this is, 1 to <see cref="OperatorCount" />.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 6.</exception>
    public ModestFmOperator(int number)
    {
        if (number < 1 || number > OperatorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number,
                "An fm6op oscillator has operators 1 to " + OperatorCount + ".");
        }

        Number = number;

        // Operator 1 alone starts at full level. The format's attribute table says every operator
        // defaults to 1.0, but its own worked examples say otherwise ("gradually raise fmOp2Level
        // from 0 to 1 to hear FM modulation build FROM A SINE WAVE") and so does the measurement:
        // an fm6op oscillator with no fm* attributes renders a pure sine, which it could not do
        // with six operators at full level. See the class remarks.
        level = number == 1 ? 1.0 : 0.0;

        // MEASURED (round 2, item 27): fmOpNRatio DEFAULTS TO N, not to 1. In algorithm 32 with six
        // carriers at level 1.0 and no ratios written at all, the spectrum is six equal lines at 1,
        // 2, 3, 4, 5 and 6 times the note frequency.
        ratio = number;
    }

    /// <summary>Which operator this is, 1 to <see cref="OperatorCount" />.</summary>
    public int Number { get; }

    /// <summary>
    /// <c>fmOpNRatio</c>: what the played note's frequency is multiplied by. 2.0 is an octave up,
    /// 0.5 an octave down. DEFAULTS TO THIS OPERATOR'S OWN NUMBER, which is measured: with no ratios
    /// written, algorithm 32's six carriers land on 1, 2, 3, 4, 5 and 6 times the note frequency.
    /// Ignored when <see cref="Mode" /> is <see cref="ModestFmOperatorMode.Fixed" />. Negative and
    /// non-finite values are ignored.
    /// </summary>
    public double Ratio
    {
        get => ratio;
        set { if (IsFinite(value) && value >= 0.0) { ratio = value; } }
    }

    /// <summary>
    /// <c>fmOpNDetune</c>: the hardware's pitch detune, from <see cref="MinimumDetune" /> to
    /// <see cref="MaximumDetune" />. Positive sharpens, negative flattens, 0 is no detune, and the
    /// offset it produces is larger at low pitches than at high ones. Default 0. Clamped.
    /// </summary>
    public int Detune
    {
        get => detune;
        set => detune = value < MinimumDetune ? MinimumDetune : value > MaximumDetune ? MaximumDetune : value;
    }

    /// <summary>
    /// <c>fmOpNMode</c>: whether the operator tracks the keyboard or runs at a fixed frequency.
    /// Default <see cref="ModestFmOperatorMode.Ratio" />.
    /// </summary>
    public ModestFmOperatorMode Mode { get; set; } = ModestFmOperatorMode.Ratio;

    /// <summary>
    /// <c>fmOpNFixedFreq</c>: the absolute frequency in Hz used when <see cref="Mode" /> is
    /// <see cref="ModestFmOperatorMode.Fixed" />. Default 440.0. Negative and non-finite values are
    /// ignored.
    /// </summary>
    public double FixedFrequency
    {
        get => fixedFrequency;
        set { if (IsFinite(value) && value >= 0.0) { fixedFrequency = value; } }
    }

    /// <summary>
    /// <c>fmOpNLevel</c>: how much the operator contributes, from 0.0 to 1.0 - as audio when the
    /// algorithm makes it a carrier, as modulation when it makes it a modulator. Default 1.0 on
    /// operator 1 and 0.0 on operators 2 to 6, so a patch that names no levels is the plain sine
    /// the reference player renders. Clamped.
    /// </summary>
    public double Level
    {
        get => level;
        set { if (IsFinite(value)) { level = Clamp01(value); } }
    }

    /// <summary>
    /// <c>fmOpNVelocitySensitivity</c>: how strongly MIDI velocity scales
    /// <see cref="Level" />, from 0 (no response) to <see cref="MaximumVelocitySensitivity" />.
    /// Default 0. Clamped.
    /// </summary>
    public int VelocitySensitivity
    {
        get => velocitySensitivity;
        set => velocitySensitivity = value < 0 ? 0
            : value > MaximumVelocitySensitivity ? MaximumVelocitySensitivity
            : value;
    }

    /// <summary>
    /// <c>fmOpNFeedback</c>: how much of its own output the operator folds back into its phase
    /// input, from 0.0 to 1.0. Default 0.0. Only the operator the active algorithm routes as the
    /// feedback source is audible, which on the classic topologies is operator 6. Clamped.
    /// </summary>
    public double Feedback
    {
        get => feedback;
        set { if (IsFinite(value)) { feedback = Clamp01(value); } }
    }

    /// <summary>
    /// <c>fmOpNAttack</c>: the operator's own attack time in seconds. Default 0.0.
    /// <see cref="OuterEnvelopeSentinel" /> means the operator has no envelope of its own and is
    /// gated by the group's. Non-finite values are ignored.
    /// </summary>
    public double Attack
    {
        get => attack;
        set { if (IsFinite(value)) { attack = value < 0.0 ? OuterEnvelopeSentinel : value; } }
    }

    /// <summary>
    /// <c>fmOpNDecay</c>: the operator's own decay time in seconds. Default 0.0. Negative and
    /// non-finite values are ignored.
    /// </summary>
    public double Decay
    {
        get => decay;
        set { if (IsFinite(value) && value >= 0.0) { decay = value; } }
    }

    /// <summary>
    /// <c>fmOpNSustain</c>: the operator's own sustain level, from 0.0 to 1.0. Default 1.0. Clamped.
    /// </summary>
    public double Sustain
    {
        get => sustain;
        set { if (IsFinite(value)) { sustain = Clamp01(value); } }
    }

    /// <summary>
    /// <c>fmOpNRelease</c>: the operator's own release time in seconds. Default
    /// <see cref="OuterEnvelopeSentinel" />, which hands the release to the group's envelope.
    /// Non-finite values are ignored.
    /// </summary>
    public double Release
    {
        get => release;
        set { if (IsFinite(value)) { release = value < 0.0 ? OuterEnvelopeSentinel : value; } }
    }

    /// <summary>
    /// <c>fmOpNEgType</c>: which envelope the operator runs. Default
    /// <see cref="ModestFmEnvelopeType.Adsr" />.
    /// </summary>
    public ModestFmEnvelopeType EnvelopeType { get; set; } = ModestFmEnvelopeType.Adsr;

    /// <summary>
    /// <c>fmOpNEgRate1</c>: the first rate of the four-stage envelope, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Higher is faster. Default 99. Clamped.
    /// </summary>
    public int EgRate1
    {
        get => egRate1;
        set => egRate1 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgRate2</c>: the second rate of the four-stage envelope, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 99. Clamped.
    /// </summary>
    public int EgRate2
    {
        get => egRate2;
        set => egRate2 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgRate3</c>: the third rate of the four-stage envelope, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 0, which holds the envelope at
    /// <see cref="EgLevel3" /> for as long as the key is down. Clamped.
    /// </summary>
    public int EgRate3
    {
        get => egRate3;
        set => egRate3 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgRate4</c>: the release rate of the four-stage envelope, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 99. Clamped.
    /// </summary>
    public int EgRate4
    {
        get => egRate4;
        set => egRate4 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgLevel1</c>: the level the first stage rises to, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 99. Clamped.
    /// </summary>
    public int EgLevel1
    {
        get => egLevel1;
        set => egLevel1 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgLevel2</c>: the level the second stage moves to, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 99. Clamped.
    /// </summary>
    public int EgLevel2
    {
        get => egLevel2;
        set => egLevel2 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgLevel3</c>: the sustain level of the four-stage envelope, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 99. Clamped.
    /// </summary>
    public int EgLevel3
    {
        get => egLevel3;
        set => egLevel3 = ClampEnvelope(value);
    }

    /// <summary>
    /// <c>fmOpNEgLevel4</c>: the level the release stage falls to, 0 to
    /// <see cref="MaximumEnvelopeValue" />. Default 0. Clamped.
    /// </summary>
    public int EgLevel4
    {
        get => egLevel4;
        set => egLevel4 = ClampEnvelope(value);
    }

    /// <summary>Copies every parameter of this operator onto another one.</summary>
    /// <param name="target">The operator to overwrite; its <see cref="Number" /> is left alone.</param>
    internal void CopyTo(ModestFmOperator target)
    {
        target.ratio = ratio;
        target.detune = detune;
        target.Mode = Mode;
        target.fixedFrequency = fixedFrequency;
        target.level = level;
        target.velocitySensitivity = velocitySensitivity;
        target.feedback = feedback;
        target.attack = attack;
        target.decay = decay;
        target.sustain = sustain;
        target.release = release;
        target.EnvelopeType = EnvelopeType;
        target.egRate1 = egRate1;
        target.egRate2 = egRate2;
        target.egRate3 = egRate3;
        target.egRate4 = egRate4;
        target.egLevel1 = egLevel1;
        target.egLevel2 = egLevel2;
        target.egLevel3 = egLevel3;
        target.egLevel4 = egLevel4;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

    private static int ClampEnvelope(int value)
        => value < 0 ? 0 : value > MaximumEnvelopeValue ? MaximumEnvelopeValue : value;
}

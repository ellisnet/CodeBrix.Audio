using System.Globalization;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One operator of the six-operator FM oscillator, as written with the <c>fmOpN...</c> attributes on
/// a <c>&lt;group&gt;</c>.
/// </summary>
/// <remarks>
/// Every property is unset until the preset writes it, so an operator can inherit from an enclosing
/// scope. The documented defaults - ratio 1.0, detune 0, ratio mode, fixed frequency 440 Hz, level
/// 1.0, velocity sensitivity 0, feedback 0, ADSR 0/0/1/-1, DX7 rates 99/99/0/99 and levels
/// 99/99/99/0 - are applied when the zone is resolved, not here.
/// </remarks>
public sealed class DecentSamplerFmOperator
{
    /// <summary>Creates an operator with nothing set.</summary>
    /// <param name="number">The operator number, 1 to 6.</param>
    internal DecentSamplerFmOperator(int number)
    {
        Number = number;
        EgRates = new double?[4];
        EgLevels = new double?[4];
    }

    /// <summary>The operator number, 1 to 6.</summary>
    public int Number { get; }

    /// <summary>Frequency ratio against the played note (<c>fmOpNRatio</c>). Default 1.0.</summary>
    public double? Ratio { get; internal set; }

    /// <summary>DX7 detune, -7 to 7 (<c>fmOpNDetune</c>). Default 0.</summary>
    public double? Detune { get; internal set; }

    /// <summary>Whether the frequency is a ratio or a fixed value (<c>fmOpNMode</c>). Default ratio.</summary>
    public DecentSamplerFmOperatorMode? Mode { get; internal set; }

    /// <summary>Fixed frequency in Hz when the mode is fixed (<c>fmOpNFixedFreq</c>). Default 440.</summary>
    public double? FixedFrequency { get; internal set; }

    /// <summary>
    /// Output or modulation level, 0 to 1 (<c>fmOpNLevel</c>). Defaults to 1.0 on operator 1 and to
    /// 0.0 on operators 2 to 6, which is why an <c>fm6op</c> oscillator carrying no <c>fm*</c>
    /// attributes sounds a pure sine.
    /// </summary>
    public double? Level { get; internal set; }

    /// <summary>Velocity sensitivity, 0 to 7 (<c>fmOpNVelocitySensitivity</c>). Default 0.</summary>
    public double? VelocitySensitivity { get; internal set; }

    /// <summary>Self-feedback amount, 0 to 1 (<c>fmOpNFeedback</c>). Default 0.</summary>
    public double? Feedback { get; internal set; }

    /// <summary>Attack time in seconds (<c>fmOpNAttack</c>). Default 0.</summary>
    public double? Attack { get; internal set; }

    /// <summary>Decay time in seconds (<c>fmOpNDecay</c>). Default 0.</summary>
    public double? Decay { get; internal set; }

    /// <summary>Sustain level, 0 to 1 (<c>fmOpNSustain</c>). Default 1.0.</summary>
    public double? Sustain { get; internal set; }

    /// <summary>
    /// Release time in seconds (<c>fmOpNRelease</c>). Default -1, the sentinel that hands the release
    /// to the group's own envelope.
    /// </summary>
    public double? Release { get; internal set; }

    /// <summary>Which envelope shape the operator uses (<c>fmOpNEgType</c>). Default ADSR.</summary>
    public DecentSamplerFmEnvelopeType? EnvelopeType { get; internal set; }

    /// <summary>
    /// The DX7 envelope rates R1 to R4, 0 to 99 (<c>fmOpNEgRate1</c> to <c>fmOpNEgRate4</c>), indexed
    /// from zero. Defaults 99, 99, 0, 99.
    /// </summary>
    public double?[] EgRates { get; }

    /// <summary>
    /// The DX7 envelope levels L1 to L4, 0 to 99 (<c>fmOpNEgLevel1</c> to <c>fmOpNEgLevel4</c>),
    /// indexed from zero. Defaults 99, 99, 99, 0.
    /// </summary>
    public double?[] EgLevels { get; }

    /// <summary>Whether the preset wrote anything at all for this operator.</summary>
    public bool IsEmpty
    {
        get
        {
            if (Ratio.HasValue || Detune.HasValue || Mode.HasValue || FixedFrequency.HasValue ||
                Level.HasValue || VelocitySensitivity.HasValue || Feedback.HasValue || Attack.HasValue ||
                Decay.HasValue || Sustain.HasValue || Release.HasValue || EnvelopeType.HasValue)
            {
                return false;
            }

            for (var i = 0; i < 4; i++)
            {
                if (EgRates[i].HasValue || EgLevels[i].HasValue)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => "fmOp" + Number.ToString(CultureInfo.InvariantCulture);
}

using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One ARIA variator (<c>varNN_*</c>): a modulation value computed from several controllers, applied
/// to one or more targets with per-target depths.
/// </summary>
/// <remarks>
/// Each input (<c>varNN_onccX</c>, shaped by <c>varNN_curveccX</c>) contributes a 0..1 value. With
/// <c>varNN_mod=mult</c> the inputs multiply - the corpus idiom for "velocity tracking whose amount a
/// controller scales"; with <c>add</c> they sum, clamped to 0..1. The combined value scales each
/// target depth: <see cref="Cutoff"/> in cents, <see cref="EqGain"/> in dB and <see cref="EqFrequency"/>
/// in Hz per band.
/// </remarks>
public sealed class SfzVariator
{
    internal SfzVariator(int number, bool multiply, IReadOnlyList<SfzCcModulation> inputs)
    {
        Number = number;
        Multiply = multiply;
        Inputs = inputs;
        EqGain = new float[3];
        EqFrequency = new float[3];
    }

    /// <summary>The variator number as written (<c>var01</c> is 1).</summary>
    public int Number { get; }

    /// <summary>Whether the inputs multiply (<c>varNN_mod=mult</c>) rather than add.</summary>
    public bool Multiply { get; }

    /// <summary>
    /// The controller inputs. Each entry's depth is its input weight and its curve shapes the
    /// controller value, exactly as written by <c>varNN_onccX</c> / <c>varNN_curveccX</c>.
    /// </summary>
    public IReadOnlyList<SfzCcModulation> Inputs { get; }

    /// <summary>Filter cutoff depth in cents (<c>varNN_cutoff</c>), 0 when untargeted.</summary>
    public float Cutoff { get; internal set; }

    /// <summary>Per-band EQ gain depth in dB (<c>varNN_eqXgain</c>), indexed by band - 1.</summary>
    public IReadOnlyList<float> EqGain { get; }

    /// <summary>Per-band EQ frequency depth in Hz (<c>varNN_eqXfreq</c>), indexed by band - 1.</summary>
    public IReadOnlyList<float> EqFrequency { get; }

    internal void SetEqGain(int band, float value)
    {
        if (1 <= band && band <= 3)
        {
            ((float[])EqGain)[band - 1] = value;
        }
    }

    internal void SetEqFrequency(int band, float value)
    {
        if (1 <= band && band <= 3)
        {
            ((float[])EqFrequency)[band - 1] = value;
        }
    }
}

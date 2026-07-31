using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// A modulation envelope block: the SFZ v1 filter envelope (<c>fileg_*</c>) or pitch envelope
/// (<c>pitcheg_*</c>). A DAHDSR contour whose output, scaled by <see cref="Depth"/> cents, moves the
/// filter cutoff or the pitch.
/// </summary>
/// <remarks>
/// Stage times are seconds and the sustain is a percentage, exactly like the amplifier envelope. The
/// envelope's segments are linear, and its output runs 0 to 1 before the depth scaling, so a depth of
/// 1200 sweeps one octave. <c>vel2depth</c> adds depth proportional to note-on velocity; stage CC
/// modulations latch at note start, like the amplifier envelope's.
/// </remarks>
public sealed class SfzModEnvelope
{
    private static readonly IReadOnlyList<SfzCcModulation> empty = [];

    internal SfzModEnvelope()
    {
        Sustain = 100f;
        DelayCc = empty;
        AttackCc = empty;
        HoldCc = empty;
        DecayCc = empty;
        SustainCc = empty;
        ReleaseCc = empty;
        DepthCc = empty;
    }

    /// <summary>Seconds before the envelope starts (<c>*eg_delay</c>).</summary>
    public float Delay { get; internal set; }

    /// <summary>Attack time in seconds (<c>*eg_attack</c>).</summary>
    public float Attack { get; internal set; }

    /// <summary>Seconds at full level before the decay (<c>*eg_hold</c>).</summary>
    public float Hold { get; internal set; }

    /// <summary>Decay time in seconds (<c>*eg_decay</c>).</summary>
    public float Decay { get; internal set; }

    /// <summary>Sustain level as a percentage of full depth (<c>*eg_sustain</c>, default 100).</summary>
    public float Sustain { get; internal set; }

    /// <summary>Release time in seconds (<c>*eg_release</c>).</summary>
    public float Release { get; internal set; }

    /// <summary>The envelope depth in cents (<c>fileg_depth</c> / <c>pitcheg_depth</c>).</summary>
    public float Depth { get; internal set; }

    /// <summary>Extra depth in cents at full note-on velocity (<c>*eg_vel2depth</c>).</summary>
    public float Vel2Depth { get; internal set; }

    /// <summary>CC modulations of <see cref="Delay"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> DelayCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Attack"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> AttackCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Hold"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> HoldCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Decay"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> DecayCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Sustain"/>, in percentage points, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> SustainCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Release"/>, in seconds, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> ReleaseCc { get; internal set; }

    /// <summary>CC modulations of <see cref="Depth"/>, in cents, latched at note start.</summary>
    public IReadOnlyList<SfzCcModulation> DepthCc { get; internal set; }
}

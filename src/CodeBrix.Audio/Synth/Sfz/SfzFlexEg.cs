using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One SFZ v2 flexible envelope (<c>egN_*</c>): an arbitrary sequence of timed points whose level,
/// scaled by a target depth, modulates pitch, filter cutoff or amplitude.
/// </summary>
/// <remarks>
/// <para>
/// Each point X carries <c>egN_timeX</c> (seconds from the previous point) and <c>egN_levelX</c>
/// (-1 to 1). The envelope moves linearly between points. When <c>egN_sustain</c> names a point, the
/// envelope holds that point's level while the note is held and runs the remaining points on release;
/// without one it simply runs through its points once.
/// </para>
/// <para>
/// The classic use in the corpus this engine was measured against: a two-point envelope from level -1
/// to 0 with <c>egN_pitch_oncc140</c> (key delta) as depth - a legato portamento that glides each note
/// in from the previous one.
/// </para>
/// </remarks>
public sealed class SfzFlexEg
{
    private static readonly IReadOnlyList<SfzCcModulation> empty = [];

    internal SfzFlexEg(int number, IReadOnlyList<float> times, IReadOnlyList<float> levels)
    {
        Number = number;
        Times = times;
        Levels = levels;
        PitchCc = empty;
        CutoffCc = empty;
        AmplitudeCc = empty;
    }

    /// <summary>The envelope number as written (<c>eg06</c> is 6).</summary>
    public int Number { get; }

    /// <summary>Seconds from the previous point, per point (<c>egN_timeX</c>).</summary>
    public IReadOnlyList<float> Times { get; }

    /// <summary>The level at each point, -1 to 1 (<c>egN_levelX</c>).</summary>
    public IReadOnlyList<float> Levels { get; }

    /// <summary>The point held while the note is held (<c>egN_sustain</c>), or <see langword="null"/>.</summary>
    public int? SustainPoint { get; internal set; }

    /// <summary>Pitch depth in cents (<c>egN_pitch</c>).</summary>
    public float Pitch { get; internal set; }

    /// <summary>CC modulations of the pitch depth, in cents (<c>egN_pitch_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> PitchCc { get; internal set; }

    /// <summary>Filter cutoff depth in Hz (<c>egN_cutoff</c>).</summary>
    public float Cutoff { get; internal set; }

    /// <summary>CC modulations of the cutoff depth, in Hz (<c>egN_cutoff_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> CutoffCc { get; internal set; }

    /// <summary>Amplitude depth as a percentage of full scale (<c>egN_amplitude</c>).</summary>
    public float Amplitude { get; internal set; }

    /// <summary>CC modulations of the amplitude depth, in percentage points (<c>egN_amplitude_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> AmplitudeCc { get; internal set; }
}

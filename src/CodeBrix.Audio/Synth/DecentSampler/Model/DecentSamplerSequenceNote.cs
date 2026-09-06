namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;note&gt;</c> inside a sequence.
/// </summary>
public sealed class DecentSamplerSequenceNote : DecentSamplerElement
{
    /// <summary>Where the note starts, in beats from the start of the sequence (<c>position</c>).</summary>
    public double? Position { get; internal set; }

    /// <summary>The note's velocity, 0 to 1 (<c>velocity</c>).</summary>
    public double? Velocity { get; internal set; }

    /// <summary>The MIDI note number (<c>note</c>).</summary>
    public int? Note { get; internal set; }

    /// <summary>How long the note is held, in beats (<c>length</c>).</summary>
    public double? Length { get; internal set; }
}

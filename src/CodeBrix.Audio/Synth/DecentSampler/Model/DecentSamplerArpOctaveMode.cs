namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How the arpeggiator weaves octave copies into its pattern.
/// </summary>
public enum DecentSamplerArpOctaveMode
{
    /// <summary>The held pattern repeats once per octave (<c>replayPerOctave</c>). The default.</summary>
    ReplayPerOctave,

    /// <summary>Every note across every octave is merged into one pitch-sorted list (<c>interleaveByPitch</c>).</summary>
    InterleaveByPitch,
}

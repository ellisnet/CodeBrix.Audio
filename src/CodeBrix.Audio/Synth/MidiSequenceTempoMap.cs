namespace CodeBrix.Audio.Synth;

// The tempo map a MidiSequence keeps alongside its flattened message stream.
//
// Loading a sequence APPLIES the tempo map and then throws it away: every message comes out
// stamped with an absolute TimeSpan, and the tempo events themselves are not in Messages. That is
// what playback wants and what anything musical cannot work without - "how long is a beat here"
// has no answer once only seconds survive. So the loader now also records what it applied, and
// this is where that recording is exposed.
//
// The field is filled in by MidiSequence.MergeTracks (in MidiSequence.cs, the ported file); it
// lives here so the port stays as close to upstream as it can.
//
// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
public sealed partial class MidiSequence
{
    private MidiTempoMap tempoMap;

    /// <summary>
    /// The tempo map applied while the sequence was loaded: every tempo change in the file, with
    /// the beat position each one falls on.
    /// </summary>
    /// <remarks>
    /// Never null and never empty. A file with no tempo event of its own reports one entry at time
    /// zero carrying the MIDI default of 120 BPM.
    /// </remarks>
    public MidiTempoMap TempoMap => tempoMap;
}

using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;sequence&gt;</c>: a pattern of notes measured in beats.
/// </summary>
public sealed class DecentSamplerNoteSequence : DecentSamplerElement
{
    private readonly List<DecentSamplerSequenceNote> _notes = [];

    /// <summary>The sequence's 0-based position, which a binding uses as its <c>seqIndex</c>.</summary>
    public int Index { get; internal set; }

    /// <summary>A descriptive name (<c>name</c>). Display only.</summary>
    public string Name { get; internal set; }

    /// <summary>The length of the sequence in beats (<c>length</c>).</summary>
    public double? Length { get; internal set; }

    /// <summary>The playback rate (<c>rate</c>). Default 1.</summary>
    public double? Rate { get; internal set; }

    /// <summary>The notes, in document order.</summary>
    public IReadOnlyList<DecentSamplerSequenceNote> Notes => _notes;

    internal void Add(DecentSamplerSequenceNote note)
    {
        _notes.Add(note);
        AddChild(note);
    }
}

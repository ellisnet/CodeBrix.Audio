using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;noteSequences&gt;</c> element: pre-authored note patterns a key or a control can play.
/// </summary>
public sealed class DecentSamplerNoteSequencesElement : DecentSamplerElement
{
    private readonly List<DecentSamplerNoteSequence> _sequences = [];

    /// <summary>The sequences, in document order. The first is sequence 0 for a binding's <c>seqIndex</c>.</summary>
    public IReadOnlyList<DecentSamplerNoteSequence> Sequences => _sequences;

    internal void Add(DecentSamplerNoteSequence sequence)
    {
        sequence.Index = _sequences.Count;
        _sequences.Add(sequence);
        AddChild(sequence);
    }
}

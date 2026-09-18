namespace CodeBrix.Audio.Abc;

/// <summary>
/// One thing inside a bar: a note, a rest, a chord, a grace group, a tuplet group, an inline field
/// change or an instrument directive.
/// </summary>
/// <remarks>
/// The elements of a bar are in the order they were written, and everything that takes time is here;
/// what does not take time - slurs, decorations, chord symbols, annotations, beams - is not modelled,
/// because this is a reader for playback and none of those change a single tick. Each KIND that was
/// dropped is named once in the tune's <see cref="AbcTune.Problems"/>.
/// </remarks>
public abstract class AbcElement
{
    /// <summary>Creates the element.</summary>
    protected AbcElement()
    {
    }
}

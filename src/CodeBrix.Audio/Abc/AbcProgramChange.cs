namespace CodeBrix.Audio.Abc;

/// <summary>
/// An instrument change asked for by a <c>%%MIDI program</c> directive.
/// </summary>
/// <remarks>
/// <para>
/// Abc's own instrumentation directive is <c>%%MIDI voice ... instrument=n</c>, numbered from one,
/// but the directive real tunes actually carry is abc2midi's <c>%%MIDI program [c] n</c>, where
/// <c>n</c> is 0 to 127 - the number a program-change message carries, so program 0 is the acoustic
/// grand piano. That is the form this reader honours, and
/// <see cref="CodeBrix.Audio.Midi.GeneralMidiProgram"/> names every value of it.
/// </para>
/// <para>
/// The element sits where the directive stood, so the instrument changes at that point in the
/// music rather than only at the start of the voice.
/// </para>
/// </remarks>
public sealed class AbcProgramChange : AbcElement
{
    /// <summary>
    /// Creates a program change.
    /// </summary>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <param name="channel">The channel the directive named, 1 to 16, or 0 for the voice's own.</param>
    public AbcProgramChange(int program, int channel)
    {
        Program = program;
        Channel = channel;
    }

    /// <summary>The program number, 0 to 127, as a program-change message carries it.</summary>
    public int Program { get; }

    /// <summary>
    /// The channel the directive named, 1 to 16, or 0 when it named none and the voice's own
    /// channel is meant.
    /// </summary>
    public int Channel { get; }

    /// <summary>Describes the program change.</summary>
    /// <returns>For example <c>"%%MIDI program 40"</c>.</returns>
    public override string ToString() =>
        Channel == 0 ? $"%%MIDI program {Program}" : $"%%MIDI program {Channel} {Program}";
}

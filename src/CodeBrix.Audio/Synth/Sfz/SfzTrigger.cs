namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// When an SFZ region sounds, from the <c>trigger</c> opcode.
/// </summary>
public enum SfzTrigger
{
    /// <summary><c>attack</c> (the default) - the region plays on note-on.</summary>
    Attack = 0,

    /// <summary>
    /// <c>release</c> - the region plays on note-off, using the velocity of the note-on that started the
    /// note. Release samples (piano damper noise, drum chokes) use this, usually with <c>rt_decay</c> so
    /// the release layer gets quieter the longer the note was held.
    /// </summary>
    Release,

    /// <summary><c>first</c> - the region plays on note-on only when no other note is held.</summary>
    First,

    /// <summary><c>legato</c> - the region plays on note-on only when at least one other note is held.</summary>
    Legato
}

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// How many players or singers a program puts on ONE note, where it offers a choice.
/// </summary>
/// <remarks>
/// <para>
/// It is set PER PROGRAM, through <see cref="GeneralMidiAdjustment.Ensemble" />, and MOST PROGRAMS
/// HAVE ONLY ONE SECTION: a celesta is one celesta however this is set, and asking for
/// <see cref="Full" /> there changes nothing at all rather than being refused. The programs that do
/// offer a larger section are the ones where a listener hears the difference as a number of people
/// rather than as a different instrument - the choral voices today, and the natural place for a
/// string section to gain one later.
/// </para>
/// <para>
/// A larger section costs more to render, because it is more oscillators on the same note. See the
/// package's AGENT-README for what it costs on the programs that offer it.
/// </para>
/// </remarks>
public enum GeneralMidiEnsemble
{
    /// <summary>
    /// The section the bank carries - the default, and what every program sounds when nothing says
    /// otherwise.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The larger section, on a program that has one; on every other program it is exactly
    /// <see cref="Standard" />.
    /// </summary>
    Full,
}

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// What a stem of a given name plays through when the export gives no better answer: a General
/// MIDI program, a MIDI channel, and whether the stem is percussion.
/// </summary>
/// <remarks>
/// These are defaults, not facts about a particular export. When a stem carries a MIDI file, the
/// program change and channel in that file win; this fills the gaps for a stem whose MIDI is
/// missing, and it is what makes an audio-only stem playable through an instrument at all.
/// </remarks>
public readonly struct SunoStemDefault
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SunoStemDefault"/> structure.
    /// </summary>
    /// <param name="name">The stem name, spelled as the exporter spells it.</param>
    /// <param name="gmProgram">The General MIDI program number, 0 to 127.</param>
    /// <param name="channel">The MIDI channel, 1 to 16; 10 for percussion.</param>
    /// <param name="isPercussion">Whether the stem is a percussion part.</param>
    public SunoStemDefault(string name, int gmProgram, int channel, bool isPercussion)
    {
        Name = name;
        GmProgram = gmProgram;
        Channel = channel;
        IsPercussion = isPercussion;
    }

    /// <summary>The stem name, spelled as the exporter spells it - "Backing Vocals", not "backing vocals".</summary>
    public string Name { get; }

    /// <summary>The General MIDI program number, 0 to 127.</summary>
    public int GmProgram { get; }

    /// <summary>The MIDI channel, 1 to 16. Percussion stems use 10.</summary>
    public int Channel { get; }

    /// <summary>Whether the stem is a percussion part, and so belongs on channel 10.</summary>
    public bool IsPercussion { get; }
}

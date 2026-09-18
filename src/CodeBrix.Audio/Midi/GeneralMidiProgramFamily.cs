namespace CodeBrix.Audio.Midi;

/// <summary>
/// The sixteen families the General MIDI Level 1 instrument patch map is grouped into.
/// </summary>
/// <remarks>
/// Each family holds exactly eight consecutive programs: family <c>n</c> holds the programs
/// <c>8n</c> to <c>8n+7</c>, so <see cref="Piano"/> holds programs 0 to 7 and
/// <see cref="SoundEffects"/> holds programs 120 to 127. <see cref="GeneralMidi.FamilyOf"/>
/// answers the family of a program, and <see cref="GeneralMidi.DisplayName(GeneralMidiProgramFamily)"/>
/// gives the family name as the specification writes it.
/// </remarks>
public enum GeneralMidiProgramFamily
{
    /// <summary>Piano. Programs 0 to 7 (display numbers 1 to 8).</summary>
    Piano = 0,

    /// <summary>Chromatic Percussion. Programs 8 to 15 (display numbers 9 to 16).</summary>
    ChromaticPercussion = 1,

    /// <summary>Organ. Programs 16 to 23 (display numbers 17 to 24).</summary>
    Organ = 2,

    /// <summary>Guitar. Programs 24 to 31 (display numbers 25 to 32).</summary>
    Guitar = 3,

    /// <summary>Bass. Programs 32 to 39 (display numbers 33 to 40).</summary>
    Bass = 4,

    /// <summary>Strings. Programs 40 to 47 (display numbers 41 to 48).</summary>
    Strings = 5,

    /// <summary>Ensemble. Programs 48 to 55 (display numbers 49 to 56).</summary>
    Ensemble = 6,

    /// <summary>Brass. Programs 56 to 63 (display numbers 57 to 64).</summary>
    Brass = 7,

    /// <summary>Reed. Programs 64 to 71 (display numbers 65 to 72).</summary>
    Reed = 8,

    /// <summary>Pipe. Programs 72 to 79 (display numbers 73 to 80).</summary>
    Pipe = 9,

    /// <summary>Synth Lead. Programs 80 to 87 (display numbers 81 to 88).</summary>
    SynthLead = 10,

    /// <summary>Synth Pad. Programs 88 to 95 (display numbers 89 to 96).</summary>
    SynthPad = 11,

    /// <summary>Synth Effects. Programs 96 to 103 (display numbers 97 to 104).</summary>
    SynthEffects = 12,

    /// <summary>Ethnic. Programs 104 to 111 (display numbers 105 to 112).</summary>
    Ethnic = 13,

    /// <summary>Percussive. Programs 112 to 119 (display numbers 113 to 120).</summary>
    Percussive = 14,

    /// <summary>Sound Effects. Programs 120 to 127 (display numbers 121 to 128).</summary>
    SoundEffects = 15,
}

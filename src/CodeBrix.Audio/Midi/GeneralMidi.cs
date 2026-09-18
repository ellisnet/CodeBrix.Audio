using System;

namespace CodeBrix.Audio.Midi;

/// <summary>
/// The General MIDI Level 1 sound set by name: what each program and each percussion note is
/// called, which family a program belongs to, and which channel percussion plays on.
/// </summary>
/// <remarks>
/// <para>
/// General MIDI Level 1 fixes a portable set of 128 melodic sounds and one percussion kit, so a
/// file written for one instrument plays recognisably on another. The names here are the ones the
/// MIDI Association publishes, returned verbatim - spelling, capitalisation and parentheses
/// included - so a user interface can show them without a table of its own.
/// </para>
/// <para>
/// Nothing here renders sound. It names what a program number means; whether an instrument answers
/// with the sound the name describes is the instrument's business. A SoundFont, an SFZ instrument
/// or a Decent Sampler preset carries its own names, and those are what its presets are actually
/// called.
/// </para>
/// </remarks>
public static class GeneralMidi
{
    /// <summary>
    /// The channel General MIDI reserves for percussion, counted the way this library's
    /// <see cref="MidiEvent.Channel"/> counts channels: 1 to 16.
    /// </summary>
    /// <remarks>
    /// A MIDI message on the wire carries the channel 0-based, where this same channel is 9. Use
    /// this constant wherever a <see cref="MidiEvent"/> is being built or examined, and 9 only when
    /// packing a status byte by hand.
    /// </remarks>
    public const int PercussionChannel = 10;

    /// <summary>The number of programs the General MIDI Level 1 patch map defines.</summary>
    public const int ProgramCount = 128;

    /// <summary>The lowest note number the General MIDI Level 1 percussion key map defines.</summary>
    public const int LowestPercussionNote = 35;

    /// <summary>The highest note number the General MIDI Level 1 percussion key map defines.</summary>
    public const int HighestPercussionNote = 81;

    /// <summary>The number of programs in each family.</summary>
    public const int ProgramsPerFamily = 8;

    private static readonly string[] ProgramNames =
    [
        "Acoustic Grand Piano",
        "Bright Acoustic Piano",
        "Electric Grand Piano",
        "Honky-tonk Piano",
        "Electric Piano 1",
        "Electric Piano 2",
        "Harpsichord",
        "Clavi",
        "Celesta",
        "Glockenspiel",
        "Music Box",
        "Vibraphone",
        "Marimba",
        "Xylophone",
        "Tubular Bells",
        "Dulcimer",
        "Drawbar Organ",
        "Percussive Organ",
        "Rock Organ",
        "Church Organ",
        "Reed Organ",
        "Accordion",
        "Harmonica",
        "Tango Accordion",
        "Acoustic Guitar (nylon)",
        "Acoustic Guitar (steel)",
        "Electric Guitar (jazz)",
        "Electric Guitar (clean)",
        "Electric Guitar (muted)",
        "Overdriven Guitar",
        "Distortion Guitar",
        "Guitar harmonics",
        "Acoustic Bass",
        "Electric Bass (finger)",
        "Electric Bass (pick)",
        "Fretless Bass",
        "Slap Bass 1",
        "Slap Bass 2",
        "Synth Bass 1",
        "Synth Bass 2",
        "Violin",
        "Viola",
        "Cello",
        "Contrabass",
        "Tremolo Strings",
        "Pizzicato Strings",
        "Orchestral Harp",
        "Timpani",
        "String Ensemble 1",
        "String Ensemble 2",
        "SynthStrings 1",
        "SynthStrings 2",
        "Choir Aahs",
        "Voice Oohs",
        "Synth Voice",
        "Orchestra Hit",
        "Trumpet",
        "Trombone",
        "Tuba",
        "Muted Trumpet",
        "French Horn",
        "Brass Section",
        "SynthBrass 1",
        "SynthBrass 2",
        "Soprano Sax",
        "Alto Sax",
        "Tenor Sax",
        "Baritone Sax",
        "Oboe",
        "English Horn",
        "Bassoon",
        "Clarinet",
        "Piccolo",
        "Flute",
        "Recorder",
        "Pan Flute",
        "Blown Bottle",
        "Shakuhachi",
        "Whistle",
        "Ocarina",
        "Lead 1 (square)",
        "Lead 2 (sawtooth)",
        "Lead 3 (calliope)",
        "Lead 4 (chiff)",
        "Lead 5 (charang)",
        "Lead 6 (voice)",
        "Lead 7 (fifths)",
        "Lead 8 (bass + lead)",
        "Pad 1 (new age)",
        "Pad 2 (warm)",
        "Pad 3 (polysynth)",
        "Pad 4 (choir)",
        "Pad 5 (bowed)",
        "Pad 6 (metallic)",
        "Pad 7 (halo)",
        "Pad 8 (sweep)",
        "FX 1 (rain)",
        "FX 2 (soundtrack)",
        "FX 3 (crystal)",
        "FX 4 (atmosphere)",
        "FX 5 (brightness)",
        "FX 6 (goblins)",
        "FX 7 (echoes)",
        "FX 8 (sci-fi)",
        "Sitar",
        "Banjo",
        "Shamisen",
        "Koto",
        "Kalimba",
        "Bag pipe",
        "Fiddle",
        "Shanai",
        "Tinkle Bell",
        "Agogo",
        "Steel Drums",
        "Woodblock",
        "Taiko Drum",
        "Melodic Tom",
        "Synth Drum",
        "Reverse Cymbal",
        "Guitar Fret Noise",
        "Breath Noise",
        "Seashore",
        "Bird Tweet",
        "Telephone Ring",
        "Helicopter",
        "Applause",
        "Gunshot",
    ];

    private static readonly string[] FamilyNames =
    [
        "Piano",
        "Chromatic Percussion",
        "Organ",
        "Guitar",
        "Bass",
        "Strings",
        "Ensemble",
        "Brass",
        "Reed",
        "Pipe",
        "Synth Lead",
        "Synth Pad",
        "Synth Effects",
        "Ethnic",
        "Percussive",
        "Sound Effects",
    ];

    // Indexed by note number minus LowestPercussionNote.
    private static readonly string[] PercussionNames =
    [
        "Acoustic Bass Drum",
        "Bass Drum 1",
        "Side Stick",
        "Acoustic Snare",
        "Hand Clap",
        "Electric Snare",
        "Low Floor Tom",
        "Closed Hi Hat",
        "High Floor Tom",
        "Pedal Hi-Hat",
        "Low Tom",
        "Open Hi-Hat",
        "Low-Mid Tom",
        "Hi-Mid Tom",
        "Crash Cymbal 1",
        "High Tom",
        "Ride Cymbal 1",
        "Chinese Cymbal",
        "Ride Bell",
        "Tambourine",
        "Splash Cymbal",
        "Cowbell",
        "Crash Cymbal 2",
        "Vibraslap",
        "Ride Cymbal 2",
        "Hi Bongo",
        "Low Bongo",
        "Mute Hi Conga",
        "Open Hi Conga",
        "Low Conga",
        "High Timbale",
        "Low Timbale",
        "High Agogo",
        "Low Agogo",
        "Cabasa",
        "Maracas",
        "Short Whistle",
        "Long Whistle",
        "Short Guiro",
        "Long Guiro",
        "Claves",
        "Hi Wood Block",
        "Low Wood Block",
        "Mute Cuica",
        "Open Cuica",
        "Mute Triangle",
        "Open Triangle",
    ];

    /// <summary>
    /// Gets the family a General MIDI program belongs to.
    /// </summary>
    /// <param name="program">The program to look up.</param>
    /// <returns>The family holding that program.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is not one of the
    /// 128 defined programs.</exception>
    public static GeneralMidiProgramFamily FamilyOf(GeneralMidiProgram program)
    {
        int value = (int)program;
        if (value < 0 || value >= ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "Program must be one of the 128 General MIDI Level 1 programs.");
        }

        return (GeneralMidiProgramFamily)(value / ProgramsPerFamily);
    }

    /// <summary>
    /// Gets the official name of a General MIDI program, exactly as the MIDI Association lists it.
    /// </summary>
    /// <param name="program">The program to name.</param>
    /// <returns>The published name, for example <c>"Acoustic Grand Piano"</c> or
    /// <c>"Lead 1 (square)"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is not one of the
    /// 128 defined programs.</exception>
    public static string DisplayName(GeneralMidiProgram program)
    {
        int value = (int)program;
        if (value < 0 || value >= ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "Program must be one of the 128 General MIDI Level 1 programs.");
        }

        return ProgramNames[value];
    }

    /// <summary>
    /// Gets the official name of a General MIDI percussion sound, exactly as the MIDI Association
    /// lists it.
    /// </summary>
    /// <param name="percussion">The percussion note to name.</param>
    /// <returns>The published name, for example <c>"Acoustic Bass Drum"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percussion"/> is outside the
    /// note numbers 35 to 81 the percussion key map defines.</exception>
    public static string DisplayName(GeneralMidiPercussion percussion)
    {
        int note = (int)percussion;
        if (note < LowestPercussionNote || note > HighestPercussionNote)
        {
            throw new ArgumentOutOfRangeException(nameof(percussion), percussion,
                "Percussion note must be in the range 35 to 81.");
        }

        return PercussionNames[note - LowestPercussionNote];
    }

    /// <summary>
    /// Gets the name of a General MIDI instrument family, exactly as the MIDI Association lists it.
    /// </summary>
    /// <param name="family">The family to name.</param>
    /// <returns>The published name, for example <c>"Chromatic Percussion"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not one of the
    /// sixteen defined families.</exception>
    public static string DisplayName(GeneralMidiProgramFamily family)
    {
        int value = (int)family;
        if (value < 0 || value >= FamilyNames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(family), family,
                "Family must be one of the sixteen General MIDI Level 1 instrument families.");
        }

        return FamilyNames[value];
    }
}

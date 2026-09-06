namespace CodeBrix.Audio.Synth.Mpe;

/// <summary>
/// How a synthesizer reads the MIDI Polyphonic Expression zones of the music it is given.
/// </summary>
/// <remarks>
/// <para>
/// A file recorded from an expressive controller spreads one performance across many MIDI channels:
/// each note lives on its own MEMBER channel and bends, brightens and swells there without touching
/// its neighbours, while a MASTER channel carries the gestures that apply to the whole zone. The
/// MIDI file usually says so with an MPE Configuration Message (RPN 6), but the common exporters
/// leave it out - which is what the other members of this enumeration are for.
/// </para>
/// <para>
/// Whatever the mode, an MPE Configuration Message arriving in the music is always honoured and
/// reconfigures the zones live.
/// </para>
/// </remarks>
public enum MpeMode
{
    /// <summary>
    /// No zones. Every channel behaves as ordinary MIDI: a pitch bend reaches two semitones unless
    /// RPN 0 says otherwise, and controllers are per channel with no master combining. The default.
    /// </summary>
    Off,

    /// <summary>
    /// A lower zone: channel 1 is the master, channel 2 upward are its members. This is what an
    /// expressive controller uses out of the box, and what a MIDI export of one normally needs.
    /// </summary>
    LowerZone,

    /// <summary>An upper zone: channel 16 is the master, channel 15 downward are its members.</summary>
    UpperZone,

    /// <summary>Both zones at once, splitting the fourteen middle channels between them.</summary>
    Both,

    /// <summary>
    /// Work it out from the music. A configuration message configures the zones exactly; failing
    /// that, notes spread over channels 2 to 16 with per-channel pitch bends, and nothing on channel
    /// 1, are read as a lower zone.
    /// </summary>
    Auto,
}

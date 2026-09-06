namespace CodeBrix.Audio.Synth.Mpe;

/// <summary>
/// A synthesizer that reads the MIDI Polyphonic Expression content of the music it plays: zones,
/// per-channel bend ranges, per-note expression and the lift velocity.
/// </summary>
/// <remarks>
/// <para>
/// A performance recorded from an expressive controller spreads each note onto its own MIDI channel
/// so that it can bend, brighten and swell alone, and a Standard MIDI File carries all of it. Every
/// instrument format in this package plays such a file the same way, and this is the surface they
/// share, so a host can offer MPE controls without knowing which format is loaded.
/// </para>
/// <para>
/// Channel numbers on this interface are 0-based, as MIDI messages carry them.
/// <see cref="MpeZoneInfo"/> reports them 1-based, as the specification and controller manuals write
/// them.
/// </para>
/// </remarks>
public interface IMpeSynthesizer
{
    /// <summary>
    /// How the synthesizer reads the MPE zones of the music it is given.
    /// <see cref="Mpe.MpeMode.Off"/> by default, which plays every channel as ordinary MIDI.
    /// </summary>
    /// <remarks>
    /// Changing this reconfigures the zones at once and forgets any configuration message the music
    /// has already delivered. An MPE Configuration Message arriving later is honoured in every mode
    /// but <see cref="Mpe.MpeMode.Off"/>, which is the way to insist a file is played as plain MIDI.
    /// </remarks>
    MpeMode MpeMode { get; set; }

    /// <summary>
    /// How far a member channel's pitch bend reaches when the music never says, in semitones.
    /// Forty-eight by default, which is what expressive controllers ship with.
    /// </summary>
    /// <remarks>
    /// RPN 0 in the music overrides this. Master channels and channels outside every zone keep
    /// MIDI's own two semitones unless RPN 0 says otherwise.
    /// </remarks>
    double MpeMemberBendRange { get; set; }

    /// <summary>
    /// How many member channels the lower zone holds in an explicit mode. Zero, the default, means
    /// fifteen when only the lower zone is on and seven when both zones are.
    /// </summary>
    int MpeLowerZoneMemberCount { get; set; }

    /// <summary>The upper zone's equivalent of <see cref="MpeLowerZoneMemberCount"/>.</summary>
    int MpeUpperZoneMemberCount { get; set; }

    /// <summary>
    /// The lower zone as it currently stands: master channel 1, its members, and the bend ranges in
    /// force. Reads back what a configuration message in the music, or automatic detection, decided.
    /// </summary>
    MpeZoneInfo MpeLowerZone { get; }

    /// <summary>The upper zone as it currently stands, with master channel 16.</summary>
    MpeZoneInfo MpeUpperZone { get; }

    /// <summary>
    /// The note-off ("lift") velocity of the last note-off for a key on a channel, 0 to 127.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <returns>The release velocity, or 0 when that key has not been released.</returns>
    int ReleaseVelocity(int channel, int key);
}

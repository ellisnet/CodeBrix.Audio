namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// Defaults the Decent Sampler developer guide does not write down, settled by measuring the reference
/// player.
/// </summary>
/// <remarks>
/// They are public because they change how a minimal preset sounds, and a host that reports a zone's
/// effective settings needs the same numbers the engine plays with.
/// </remarks>
public static class DecentSamplerDefaults
{
    /// <summary>
    /// The amplitude release a zone gets when the preset declares none anywhere: HALF A SECOND, not
    /// zero. Measured (item 4) - a preset that sets no release still rings.
    /// </summary>
    public const double ReleaseSeconds = 0.5;

    /// <summary>The amplitude attack a zone gets when the preset declares none: immediate.</summary>
    public const double AttackSeconds = 0.0;

    /// <summary>The amplitude decay a zone gets when the preset declares none.</summary>
    public const double DecaySeconds = 0.0;

    /// <summary>The sustain level a zone gets when the preset declares none: full level.</summary>
    public const double SustainLevel = 1.0;

    /// <summary>
    /// How much velocity drives volume when <c>ampVelTrack</c> is absent: fully. Measured (item 2); the
    /// guide gives no default.
    /// </summary>
    public const double AmpVelTrack = 1.0;

    /// <summary>
    /// The largest linear volume the format honours, 16.0 (+24.08 dB). Anything above is clamped, which
    /// matches the ceiling the guide gives for the gain effect's level. Measured (item 3).
    /// </summary>
    public const double MaximumVolume = 16.0;

    /// <summary>
    /// The tempo the reference standalone runs at with no host, and the tempo a
    /// <see cref="TempoSource"/> reports before a transport sets one. Measured (item 8).
    /// </summary>
    public const double BeatsPerMinute = 120.0;

    /// <summary>
    /// The choke time a stolen voice, or a voice silenced with <c>silencingMode="fast"</c>, fades over.
    /// Measured (round 2, item 25): the reference player chokes a fast-silenced voice inside about two
    /// milliseconds, not the five the SFZ voice uses for <c>off_mode=fast</c>.
    /// </summary>
    public const double FastSilencingSeconds = 0.002;
}

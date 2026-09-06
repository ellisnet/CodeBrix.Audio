using System.Globalization;

namespace CodeBrix.Audio.Synth.Mpe;

/// <summary>
/// What one MIDI Polyphonic Expression zone currently looks like: which channel is its master, which
/// channels are its members, and how far a bend reaches on each.
/// </summary>
/// <remarks>
/// Channel numbers are 1-BASED here, as the MPE specification and every controller's manual write
/// them: the lower zone's master is channel 1 and the upper zone's is channel 16. The MIDI messages
/// themselves carry 0-based channels, so a host reading this against a raw message must add one.
/// </remarks>
public readonly struct MpeZoneInfo
{
    /// <summary>Builds a zone description.</summary>
    /// <param name="isActive">Whether the zone is switched on.</param>
    /// <param name="masterChannel">The master channel, 1 to 16.</param>
    /// <param name="firstMemberChannel">The member channel nearest the master, 1 to 16.</param>
    /// <param name="memberCount">How many member channels the zone holds, 0 to 15.</param>
    /// <param name="masterBendRange">How far the master channel's bend reaches, in semitones.</param>
    /// <param name="memberBendRange">How far a member channel's bend reaches, in semitones.</param>
    public MpeZoneInfo(
        bool isActive,
        int masterChannel,
        int firstMemberChannel,
        int memberCount,
        double masterBendRange,
        double memberBendRange)
    {
        IsActive = isActive;
        MasterChannel = masterChannel;
        FirstMemberChannel = firstMemberChannel;
        MemberCount = memberCount;
        MasterBendRange = masterBendRange;
        MemberBendRange = memberBendRange;
    }

    /// <summary>Whether the zone is switched on. An inactive zone's other members are meaningless.</summary>
    public bool IsActive { get; }

    /// <summary>The master channel, 1 to 16. The lower zone's is 1 and the upper zone's is 16.</summary>
    public int MasterChannel { get; }

    /// <summary>
    /// The member channel next to the master, 1 to 16: channel 2 for the lower zone and channel 15 for
    /// the upper one. Members run away from the master, so the upper zone's run downward.
    /// </summary>
    public int FirstMemberChannel { get; }

    /// <summary>How many member channels the zone holds, 0 to 15. Zero switches the zone off.</summary>
    public int MemberCount { get; }

    /// <summary>How far the master channel's own pitch bend reaches, in semitones. Two by default.</summary>
    public double MasterBendRange { get; }

    /// <summary>
    /// How far a member channel's pitch bend reaches, in semitones. Forty-eight by default, which is
    /// what expressive controllers ship with.
    /// </summary>
    public double MemberBendRange { get; }

    /// <summary>Whether a 1-based MIDI channel is one of this zone's members.</summary>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <returns><see langword="true"/> when the zone is active and the channel is a member.</returns>
    public bool ContainsMember(int channel)
    {
        if (!IsActive || MemberCount <= 0)
        {
            return false;
        }

        return MasterChannel == 1
            ? channel >= 2 && channel <= 1 + MemberCount
            : channel <= 15 && channel >= 16 - MemberCount;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        !IsActive
            ? "inactive"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"master {MasterChannel}, {MemberCount} member(s) from {FirstMemberChannel}, bend {MemberBendRange}/{MasterBendRange} semitones");
}

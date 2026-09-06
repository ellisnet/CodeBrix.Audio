using CodeBrix.Audio.Synth.Mpe;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The shared MIDI Polyphonic Expression contract: the registered-parameter state machine, the zone
/// model, and the rules that combine a member channel with its zone master.
/// </summary>
/// <remarks>
/// Channels are 0-based here, as MIDI messages carry them, so "channel 0" is the channel a musician
/// calls channel 1 and the lower zone's master.
/// </remarks>
public class MpeChannelStateTests
{
    [Fact]
    public void a_bend_reaches_two_semitones_until_a_registered_parameter_says_otherwise()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        Bend(state, 3, 1.0);

        //Assert
        state.BendSemitones(3).Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void RPN_0_sets_the_bend_range_in_semitones_and_cents()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        SelectRpn(state, 3, 0, 0);
        state.ControlChange(3, 6, 12);
        state.ControlChange(3, 38, 50);
        Bend(state, 3, 1.0);

        //Assert
        state.BendRange(3).Should().BeApproximately(12.5, 0.001);
        state.BendSemitones(3).Should().BeApproximately(12.5, 0.002);
    }

    [Fact]
    public void the_null_registered_parameter_stops_data_entry_reaching_anything()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        SelectRpn(state, 3, 0, 0);
        state.ControlChange(3, 6, 12);
        SelectRpn(state, 3, 127, 127);
        state.ControlChange(3, 6, 2);

        //Assert
        state.BendRange(3).Should().BeApproximately(12.0, 0.001);
    }

    [Fact]
    public void a_non_registered_parameter_selection_also_stops_data_entry()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        SelectRpn(state, 3, 0, 0);
        state.ControlChange(3, 6, 12);
        state.ControlChange(3, 99, 0);
        state.ControlChange(3, 98, 1);
        state.ControlChange(3, 6, 2);

        //Assert
        state.BendRange(3).Should().BeApproximately(12.0, 0.001);
    }

    [Fact]
    public void data_increment_and_decrement_nudge_the_selected_parameter()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        SelectRpn(state, 3, 0, 0);
        state.ControlChange(3, 6, 12);
        state.ControlChange(3, 96, 127);
        state.ControlChange(3, 96, 127);
        state.ControlChange(3, 97, 127);

        //Assert
        state.BendRange(3).Should().BeApproximately(13.0, 0.001);
    }

    [Fact]
    public void coarse_and_fine_tuning_shift_the_channel()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        SelectRpn(state, 5, 0, 2);
        state.ControlChange(5, 6, 67);

        SelectRpn(state, 5, 0, 1);
        state.ControlChange(5, 6, 96);
        state.ControlChange(5, 38, 0);

        //Assert
        // Three semitones up from the coarse tuning, plus half a semitone from the fine one.
        state.TuningSemitones(5).Should().BeApproximately(3.5, 0.01);
    }

    [Fact]
    public void a_configuration_message_on_channel_one_opens_a_lower_zone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        Configure(state, 0, 7);

        //Assert
        state.LowerZone.IsActive.Should().BeTrue();
        state.LowerZone.MasterChannel.Should().Be(1);
        state.LowerZone.MemberCount.Should().Be(7);
        state.LowerZone.MemberBendRange.Should().Be(MpeChannelState.DefaultMemberBendRange);
        state.MasterOf(4).Should().Be(0);
        state.MasterOf(8).Should().Be(-1);
    }

    [Fact]
    public void a_configuration_message_on_channel_sixteen_opens_an_upper_zone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        Configure(state, 15, 5);

        //Assert
        state.UpperZone.IsActive.Should().BeTrue();
        state.UpperZone.MasterChannel.Should().Be(16);
        state.UpperZone.MemberCount.Should().Be(5);
        state.MasterOf(14).Should().Be(15);
        state.MasterOf(10).Should().Be(15);
        state.MasterOf(9).Should().Be(-1);
    }

    [Fact]
    public void zero_members_switches_a_zone_off()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };
        Configure(state, 0, 7);

        //Act
        Configure(state, 0, 0);

        //Assert
        state.LowerZone.IsActive.Should().BeFalse();
        state.MasterOf(3).Should().Be(-1);
    }

    [Fact]
    public void both_zones_can_be_open_at_once()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        Configure(state, 0, 5);
        Configure(state, 15, 5);

        //Assert
        state.LowerZone.IsActive.Should().BeTrue();
        state.UpperZone.IsActive.Should().BeTrue();
        state.MasterOf(3).Should().Be(0);
        state.MasterOf(12).Should().Be(15);
        state.MasterOf(7).Should().Be(-1);
    }

    [Fact]
    public void a_second_zone_that_would_overlap_shrinks_the_first()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        Configure(state, 0, 15);
        Configure(state, 15, 6);

        //Assert
        state.LowerZone.MemberCount.Should().Be(8);
        state.UpperZone.MemberCount.Should().Be(6);
    }

    [Fact]
    public void mode_off_ignores_a_configuration_message()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Off };

        //Act
        Configure(state, 0, 7);

        //Assert
        state.LowerZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void a_member_bends_over_the_member_range_and_adds_its_masters_bend()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        Bend(state, 2, 0.5);
        Bend(state, 0, 1.0);

        //Assert
        // Half of 48 semitones from the member, plus the master's full two.
        state.BendSemitones(2).Should().BeApproximately(26.0, 0.01);
        state.BendSemitones(0).Should().BeApproximately(2.0, 0.01);
    }

    [Fact]
    public void RPN_0_on_a_member_configures_every_member_of_the_zone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        SelectRpn(state, 1, 0, 0);
        state.ControlChange(1, 6, 24);

        //Assert
        state.BendRange(1).Should().BeApproximately(24.0, 0.001);
        state.BendRange(7).Should().BeApproximately(24.0, 0.001);
        state.BendRange(0).Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void RPN_0_on_a_master_configures_only_that_master()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        SelectRpn(state, 0, 0, 0);
        state.ControlChange(0, 6, 12);

        //Assert
        state.BendRange(0).Should().BeApproximately(12.0, 0.001);
        state.BendRange(3).Should().Be(MpeChannelState.DefaultMemberBendRange);
    }

    [Fact]
    public void auto_infers_a_lower_zone_from_notes_and_bends_above_channel_one()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        state.NoteOn(1, 60);
        state.NoteOn(2, 64);
        Bend(state, 1, 0.25);

        //Assert
        state.LowerZone.IsActive.Should().BeTrue();
        state.LowerZone.MemberCount.Should().Be(15);
    }

    [Fact]
    public void auto_infers_a_lower_zone_when_the_bends_come_before_the_notes()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        Bend(state, 1, 0.0);
        Bend(state, 2, 0.0);
        state.NoteOn(1, 60);

        //Assert
        state.LowerZone.IsActive.Should().BeTrue();
    }

    [Fact]
    public void auto_leaves_an_ordinary_file_alone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        state.NoteOn(0, 60);
        state.NoteOn(1, 64);
        state.NoteOn(2, 67);
        Bend(state, 1, 0.5);

        //Assert
        state.LowerZone.IsActive.Should().BeFalse();
        state.BendSemitones(1).Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void auto_leaves_a_single_bending_channel_alone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Auto };

        //Act
        state.NoteOn(1, 60);
        Bend(state, 1, 0.5);

        //Assert
        state.LowerZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void explicit_member_counts_pin_the_layout()
    {
        //Arrange
        var state = new MpeChannelState { LowerZoneMemberCount = 4, Mode = MpeMode.LowerZone };

        //Act
        var zone = state.LowerZone;

        //Assert
        zone.MemberCount.Should().Be(4);
        state.MasterOf(4).Should().Be(0);
        state.MasterOf(5).Should().Be(-1);
    }

    [Fact]
    public void both_zones_split_the_middle_channels_by_default()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Both };

        //Act
        var lower = state.LowerZone;
        var upper = state.UpperZone;

        //Assert
        lower.MemberCount.Should().Be(7);
        upper.MemberCount.Should().Be(7);
        state.MasterOf(7).Should().Be(0);
        state.MasterOf(8).Should().Be(15);
    }

    [Fact]
    public void a_masters_switch_controller_applies_across_the_zone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        state.ControlChange(0, 64, 127);

        //Assert
        state.Controller(3, 64).Should().Be(127);
        state.RawController(3, 64).Should().Be(0);
    }

    [Fact]
    public void a_members_own_continuous_controller_wins_over_its_masters()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        state.ControlChange(0, 11, 40);
        var inherited = state.Controller(3, 11);
        state.ControlChange(3, 11, 100);

        //Assert
        inherited.Should().Be(40);
        state.Controller(3, 11).Should().Be(100);
    }

    [Fact]
    public void pressure_adds_the_masters_channel_pressure()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        state.ProcessMessage(3, 0xD0, 64, 0);
        state.ProcessMessage(0, 0xD0, 32, 0);

        //Assert
        state.Pressure(3, 60).Should().BeApproximately((64 + 32) / 127.0, 0.001);
    }

    [Fact]
    public void polyphonic_pressure_is_read_as_the_notes_own_pressure()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.Off };

        //Act
        state.NoteOn(4, 60);
        state.NoteOn(4, 64);
        state.ProcessMessage(4, 0xD0, 20, 0);
        state.ProcessMessage(4, 0xA0, 60, 100);

        //Assert
        state.Pressure(4, 60).Should().BeApproximately(100 / 127.0, 0.001);
        state.Pressure(4, 64).Should().BeApproximately(20 / 127.0, 0.001);
    }

    [Fact]
    public void timbre_reads_the_master_as_an_offset_around_its_centre()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        state.ControlChange(3, 74, 64);
        var alone = state.Timbre(3);

        state.ControlChange(0, 74, 96);

        //Assert
        alone.Should().BeApproximately(64 / 127.0, 0.001);
        state.Timbre(3).Should().BeApproximately((64 / 127.0) + (32 / 127.0), 0.001);
    }

    [Fact]
    public void the_newest_note_owns_a_member_channels_expression()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };

        //Act
        state.NoteOn(2, 60);
        var firstAlone = state.OwnsChannelExpression(2, 60);

        state.NoteOn(2, 67);
        var firstAfterSecond = state.OwnsChannelExpression(2, 60);
        var secondOwns = state.OwnsChannelExpression(2, 67);

        state.NoteOff(2, 67, 40);
        var firstAgain = state.OwnsChannelExpression(2, 60);

        //Assert
        firstAlone.Should().BeTrue();
        firstAfterSecond.Should().BeFalse();
        secondOwns.Should().BeTrue();
        firstAgain.Should().BeTrue();
    }

    [Fact]
    public void the_note_off_velocity_is_captured()
    {
        //Arrange
        var state = new MpeChannelState();

        //Act
        state.ProcessMessage(4, 0x90, 60, 100);
        state.ProcessMessage(4, 0x80, 60, 37);

        //Assert
        state.ReleaseVelocity(4, 60).Should().Be(37);
    }

    [Fact]
    public void reset_all_controllers_leaves_the_zone_and_the_bend_ranges_alone()
    {
        //Arrange
        var state = new MpeChannelState { Mode = MpeMode.LowerZone };
        SelectRpn(state, 1, 0, 0);
        state.ControlChange(1, 6, 24);
        Bend(state, 3, 1.0);

        //Act
        state.ControlChange(3, 121, 127);

        //Assert
        state.BendSemitones(3).Should().Be(0.0);
        state.BendRange(3).Should().BeApproximately(24.0, 0.001);
        state.LowerZone.IsActive.Should().BeTrue();
    }

    private static void Bend(MpeChannelState state, int channel, double normalized)
    {
        var value = (int)System.Math.Round((normalized * 8192.0) + 8192.0);
        value = System.Math.Clamp(value, 0, 16383);
        state.SetPitchBend(channel, value & 0x7F, (value >> 7) & 0x7F);
    }

    private static void SelectRpn(MpeChannelState state, int channel, int msb, int lsb)
    {
        state.ControlChange(channel, 101, msb);
        state.ControlChange(channel, 100, lsb);
    }

    private static void Configure(MpeChannelState state, int channel, int memberCount)
    {
        SelectRpn(state, channel, 0, 6);
        state.ControlChange(channel, 6, memberCount);
    }
}

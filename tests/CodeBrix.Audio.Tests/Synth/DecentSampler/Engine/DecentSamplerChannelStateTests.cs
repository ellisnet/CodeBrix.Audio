using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The per-channel MIDI state the zone filters, the controller triggers and the later modulator and MPE
/// phases all read: controller values, pitch bend and its range, the sustain pedal, pressure, and which
/// keys are down.
/// </summary>
public class DecentSamplerChannelStateTests
{
    [Fact]
    public void a_fresh_channel_holds_nothing()
    {
        //Arrange
        //Act
        var state = new DecentSamplerChannelState();

        //Assert
        state.GetController(1).Should().Be(0);
        state.PitchBend.Should().Be(0.0);
        state.BendSemitones.Should().Be(2.0);
        state.HeldKeyCount.Should().Be(0);
        state.IsSustainDown.Should().BeFalse();
        state.ChannelPressure.Should().Be(0);
    }

    [Fact]
    public void every_controller_is_stored()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        for (var controller = 0; controller < 128; controller++)
        {
            state.SetController(controller, controller);
        }

        //Assert
        state.GetController(0).Should().Be(0);
        state.GetController(11).Should().Be(11);
        state.GetController(127).Should().Be(127);
        state.GetController(200).Should().Be(0);
    }

    [Fact]
    public void a_controller_value_is_clamped_to_the_midi_range()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.SetController(7, 900);

        //Assert
        state.GetController(7).Should().Be(127);
    }

    [Theory]
    [InlineData(0, 0, -1.0)]
    [InlineData(0, 64, 0.0)]
    [InlineData(127, 127, 1.0)]
    public void pitch_bend_is_fourteen_bit(int low, int high, double expected)
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.SetPitchBend(low, high);

        //Assert
        state.PitchBend.Should().BeApproximately(expected, 0.0002);
        state.BendInSemitones.Should().BeApproximately(expected * 2.0, 0.0005);
    }

    [Fact]
    public void the_bend_range_scales_the_wheel()
    {
        //Arrange
        var state = new DecentSamplerChannelState();
        state.SetPitchBend(127, 127);

        //Act
        state.BendSemitones = 48.0;

        //Assert
        state.BendInSemitones.Should().BeApproximately(48.0, 0.01);
    }

    [Fact]
    public void the_sustain_pedal_reads_from_controller_sixty_four()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.SetController(64, 63);
        var up = state.IsSustainDown;
        state.SetController(64, 64);
        var down = state.IsSustainDown;

        //Assert
        up.Should().BeFalse();
        down.Should().BeTrue();
    }

    [Fact]
    public void channel_and_polyphonic_pressure_are_stored()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.SetChannelPressure(90);
        state.SetPolyPressure(60, 40);

        //Assert
        state.ChannelPressure.Should().Be(90);
        state.GetPolyPressure(60).Should().Be(40);
        state.GetPolyPressure(61).Should().Be(0);
    }

    [Fact]
    public void keys_are_counted_and_remembered()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.KeyDown(60, 100, 480);
        state.KeyDown(64, 90, 960);

        //Assert
        state.HeldKeyCount.Should().Be(2);
        state.IsKeyHeld(60).Should().BeTrue();
        state.NoteOnVelocity(64).Should().Be(90);
        state.NoteOnFrame(60).Should().Be(480);

        state.KeyUp(60);
        state.HeldKeyCount.Should().Be(1);
        state.IsKeyHeld(60).Should().BeFalse();
    }

    [Fact]
    public void a_repeated_key_down_does_not_double_count()
    {
        //Arrange
        var state = new DecentSamplerChannelState();

        //Act
        state.KeyDown(60, 100, 0);
        state.KeyDown(60, 110, 64);

        //Assert
        state.HeldKeyCount.Should().Be(1);
        state.NoteOnVelocity(60).Should().Be(110);
    }

    [Fact]
    public void a_sustained_key_remembers_when_its_key_came_up()
    {
        //Arrange
        var state = new DecentSamplerChannelState();
        state.KeyDown(60, 100, 0);
        state.KeyUp(60);

        //Act
        state.SustainKey(60, 44100);

        //Assert
        state.IsKeySustained(60).Should().BeTrue();
        state.SustainedFrame(60).Should().Be(44100);

        state.ClearSustainedKey(60);
        state.IsKeySustained(60).Should().BeFalse();
    }

    [Fact]
    public void resetting_the_controllers_leaves_the_keys_alone()
    {
        //Arrange
        var state = new DecentSamplerChannelState();
        state.KeyDown(60, 100, 0);
        state.SetController(11, 90);
        state.SetPitchBend(127, 127);
        state.SetChannelPressure(70);

        //Act
        state.ResetControllers();

        //Assert - which keys are down is a fact about the player's hands, not controller state.
        state.GetController(11).Should().Be(0);
        state.PitchBend.Should().Be(0.0);
        state.ChannelPressure.Should().Be(0);
        state.IsKeyHeld(60).Should().BeTrue();
    }

    [Fact]
    public void releasing_every_key_at_once_empties_the_count()
    {
        //Arrange
        var state = new DecentSamplerChannelState();
        state.KeyDown(60, 100, 0);
        state.KeyDown(64, 100, 0);

        //Act
        state.ReleaseAllKeys();

        //Assert
        state.HeldKeyCount.Should().Be(0);
        state.IsKeyHeld(60).Should().BeFalse();
    }

    [Fact]
    public void a_full_reset_clears_everything()
    {
        //Arrange
        var state = new DecentSamplerChannelState();
        state.KeyDown(60, 100, 0);
        state.SetController(64, 127);
        state.BendSemitones = 48.0;

        //Act
        state.Reset();

        //Assert
        state.HeldKeyCount.Should().Be(0);
        state.IsSustainDown.Should().BeFalse();
        state.BendSemitones.Should().Be(DecentSamplerChannelState.DefaultBendSemitones);
    }
}

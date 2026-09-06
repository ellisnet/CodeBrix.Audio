using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Which zones a note-on selects: enabled groups and tags, key and velocity ranges, controller filters,
/// the five trigger modes, and previousNotes / legatoInterval.
/// </summary>
/// <remarks>
/// Every zone plays a constant-valued sample of its own, so the level of the render says exactly which
/// zones sounded: 0.1 alone, 0.2 alone, or 0.3 for both together.
/// </remarks>
public class DecentSamplerZoneMatchingTests
{
    [Fact]
    public void a_note_outside_the_key_range_selects_nothing()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group><sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="64" /></group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(59);

        //Assert
        level.Should().Be(0.0);
    }

    [Fact]
    public void a_note_outside_the_velocity_range_selects_nothing()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" loVel="64" hiVel="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var quiet = world.Play(60, velocity: 63);
        var loud = world.Play(60, velocity: 64);

        //Assert
        quiet.Should().Be(0.0);
        loud.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void a_disabled_group_selects_nothing()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group enabled="false" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group ampVelTrack="0">
                  <sample path="Samples/b.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.2, 0.002);
    }

    [Fact]
    public void a_controller_filter_gates_the_zone_on_the_last_value()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" loCC64="90" hiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var pedalUp = world.Play(60);
        var pedalDown = world.Play(60, controller: 64, controllerValue: 100);

        //Assert
        pedalUp.Should().Be(0.0);
        pedalDown.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void trigger_first_only_plays_when_nothing_is_held()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="first" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var first = world.Measure(synthesizer);

        synthesizer.NoteOn(0, 64, 100);
        var second = world.Measure(synthesizer);

        //Assert - the second note adds nothing, because a key was already down.
        first.Should().BeApproximately(0.1, 0.002);
        second.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void trigger_legato_only_plays_when_something_is_held()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="legato" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var alone = world.Measure(synthesizer);

        synthesizer.NoteOn(0, 64, 100);
        var overlapping = world.Measure(synthesizer);

        //Assert
        alone.Should().Be(0.0);
        overlapping.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void trigger_release_does_not_fire_on_a_note_on()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="release" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().Be(0.0);
    }

    [Fact]
    public void trigger_continuous_starts_at_the_first_render_without_a_note()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="continuous" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        var level = world.Measure(synthesizer);

        //Assert
        level.Should().BeApproximately(0.1, 0.002);
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void previousNotes_only_lets_the_named_predecessor_through()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="legato" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="64" loNote="64" hiNote="64" previousNotes="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var wrongPredecessor = PlaySequence(world, 61, 64);
        var rightPredecessor = PlaySequence(world, 62, 64);

        //Assert
        wrongPredecessor.Should().Be(0.0);
        rightPredecessor.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void legatoInterval_measures_from_the_previous_note_to_this_one()
    {
        //Arrange - the guide's own example: a zone with legatoInterval -2 plays only after a note two
        //semitones above it.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="legato" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" legatoInterval="-2" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var fromAbove = PlaySequence(world, 62, 60);
        var fromBelow = PlaySequence(world, 58, 60);

        //Assert
        fromAbove.Should().BeApproximately(0.1, 0.002);
        fromBelow.Should().Be(0.0);
    }

    [Fact]
    public void two_zones_on_one_key_both_sound()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0"><sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" /></group>
                <group ampVelTrack="0"><sample path="Samples/b.wav" rootNote="60" loNote="60" hiNote="60" /></group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.3, 0.002);
    }

    [Fact]
    public void the_sustain_pedal_holds_the_note_off()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" release="0.01">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(0, 60, 100);
        world.Measure(synthesizer);

        //Act
        synthesizer.NoteOff(0, 60);
        var whileHeld = world.Measure(synthesizer, blocks: 16);

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 0);
        var (afterLift, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16);

        //Assert - the 10 ms release has long finished by the tail of the window.
        whileHeld.Should().BeApproximately(0.1, 0.002);
        DecentSamplerRenderProbe.Rms(afterLift, afterLift.Length - 256, 256).Should().BeLessThan(0.001);
    }

    private static double PlaySequence(MatchingWorld world, int firstKey, int secondKey)
    {
        var synthesizer = world.NewSynthesizer();

        synthesizer.NoteOn(0, firstKey, 100);
        world.Measure(synthesizer);
        synthesizer.NoteOn(0, secondKey, 100);

        return world.Measure(synthesizer);
    }

    private static MatchingWorld Build(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/a.wav", value: 0.1f);
        fixtures.WriteConstantWav("Samples/b.wav", value: 0.2f);
        fixtures.WriteConstantWav("Samples/c.wav", value: 0.4f);

        return new MatchingWorld(fixtures, fixtures.LoadPreset(preset));
    }

    // A loaded preset plus the fixtures that built it, so a test disposes both at once. The measurement
    // is deliberately RMS over whole blocks: the mixer ramps gains across a block, so a peak reading
    // would report the level before the change as often as after it.
    private sealed class MatchingWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : System.IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer() =>
            DecentSamplerRenderProbe.Synthesizer(instrument);

        public double Play(int key, int velocity = 100, int controller = -1, int controllerValue = 0)
        {
            var synthesizer = NewSynthesizer();

            if (controller >= 0)
            {
                synthesizer.ProcessMidiMessage(0, 0xB0, controller, controllerValue);
            }

            synthesizer.NoteOn(0, key, velocity);
            return Measure(synthesizer);
        }

        public double Measure(DecentSamplerSynthesizer synthesizer, int blocks = 4)
        {
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, blocks);
            return DecentSamplerRenderProbe.Rms(left);
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

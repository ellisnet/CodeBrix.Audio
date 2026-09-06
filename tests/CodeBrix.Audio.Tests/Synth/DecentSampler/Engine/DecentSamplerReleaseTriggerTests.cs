using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Release triggers: the zone that fires on note-off, both documented forms of releaseTriggerDecay, and
/// the controller-triggered zones that fire on a CC crossing into range.
/// </summary>
/// <remarks>
/// <para>
/// Measured (plan section 7 item 10a): the decibel form is a rate in dB per second of hold with its
/// sign ignored, and the LINEAR form is the fraction LOST per second, so the gain is
/// (1 - value) ^ heldSeconds. The release layer is a constant sample of its own so its level reads
/// directly.
/// </para>
/// <para>
/// MEASURED (round 2, item 25): a release trigger only fires on a key that ALSO STARTED A VOICE, so
/// every preset here carries a silent attack group on the same key - which is exactly the shape the
/// measurement had to use before its own release layer would sound at all.
/// </para>
/// </remarks>
public class DecentSamplerReleaseTriggerTests
{
    [Fact]
    public void a_release_zone_fires_on_the_note_off()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group trigger="release" ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayAndRelease(heldSeconds: 0.1, tailSeconds: 0.3);

        //Assert
        Level(render, 0.05).Should().BeLessThan(0.001);
        Level(render, 0.2).Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void with_no_decay_the_hold_time_does_not_matter()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group trigger="release" ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var shortHold = LevelAfterRelease(world, heldSeconds: 0.1);
        var longHold = LevelAfterRelease(world, heldSeconds: 2.0);

        //Assert
        shortHold.Should().BeApproximately(0.5, 0.01);
        longHold.Should().BeApproximately(0.5, 0.01);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void the_decibel_form_is_a_rate_per_second_of_hold(double held)
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group trigger="release" releaseTriggerDecay="6dB" ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var level = LevelAfterRelease(world, held);

        //Assert
        level.Should().BeApproximately(0.5 * Math.Pow(10.0, -6.0 * held / 20.0), 0.01);
    }

    [Fact]
    public void a_positive_and_a_negative_decibel_rate_behave_alike()
    {
        //Arrange
        using var positive = Build(DecayPreset("3dB"));
        using var negative = Build(DecayPreset("-3dB"));

        //Act
        var fromPositive = LevelAfterRelease(positive, heldSeconds: 2.0);
        var fromNegative = LevelAfterRelease(negative, heldSeconds: 2.0);

        //Assert
        fromNegative.Should().BeApproximately(fromPositive, 0.005);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void the_linear_form_is_the_fraction_lost_per_second(double held)
    {
        //Arrange
        using var world = Build(DecayPreset("0.5"));

        //Act
        var level = LevelAfterRelease(world, held);

        //Assert - measured: gain = (1 - value)^heldSeconds, so 0.5 loses 6.02 dB a second, not 10.5.
        level.Should().BeApproximately(0.5 * Math.Pow(0.5, held), 0.01);
    }

    [Fact]
    public void a_release_zone_is_not_released_by_the_note_off_that_started_it()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group trigger="release" ampVelTrack="0" release="0.001">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayAndRelease(heldSeconds: 0.05, tailSeconds: 0.4);

        //Assert - it plays on well past the note-off that fired it.
        Level(render, 0.3).Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void a_controller_zone_fires_when_the_value_enters_its_range()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60"
                          onLoCC64="90" onHiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 20);
        var (belowRange, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 100);
        var (inRange, _2) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Peak(belowRange).Should().Be(0.0);
        DecentSamplerRenderProbe.Rms(inRange).Should().BeGreaterThan(0.1);
    }

    // MEASURED (round 2, item 25): a controller-triggered zone plays at FULL velocity, whatever the
    // controller's own value is. Before that measurement the controller value stood in for velocity,
    // which made a zone with velocity tracking quieter the lower the knob was.
    [Fact]
    public void a_controller_zone_plays_at_full_velocity_whatever_the_controller_says()
    {
        //Arrange - ampVelTrack="1" makes the level report the velocity the zone was started at.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="1">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60"
                          onLoCC64="90" onHiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var quiet = world.NewSynthesizer();
        var loud = world.NewSynthesizer();

        //Act - the same zone entered at the bottom of its range and at the top of it.
        quiet.ProcessMidiMessage(0, 0xB0, 64, 90);
        var (atNinety, _) = DecentSamplerRenderProbe.RenderBlocks(quiet, 8);

        loud.ProcessMidiMessage(0, 0xB0, 64, 127);
        var (atFull, _2) = DecentSamplerRenderProbe.RenderBlocks(loud, 8);

        //Assert - the same level, and it is the level a velocity-127 note would give.
        var quietLevel = DecentSamplerRenderProbe.Rms(atNinety, 128, 256);
        var loudLevel = DecentSamplerRenderProbe.Rms(atFull, 128, 256);

        quietLevel.Should().BeApproximately(loudLevel, 1e-6);
        quietLevel.Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void a_controller_zone_does_not_fire_again_while_the_value_stays_in_range()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60"
                          onLoCC64="90" onHiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 110);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Assert - one voice, not two: the range was already entered.
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void a_controller_zone_ignores_note_ons()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60"
                          onLoCC64="90" onHiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Peak(left).Should().Be(0.0);
    }

    [Fact]
    public void a_release_zone_on_a_key_with_no_attack_zone_never_fires()
    {
        //Arrange
        // MEASURED (round 2, item 25): a group holding ONLY a trigger="release" zone, on a key with no
        // attack zone anywhere, never fires - the measurement lost a whole launch to it. Adding a
        // silent attack zone on the same key makes it fire at once.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group trigger="release" ampVelTrack="0">
                  <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayAndRelease(heldSeconds: 0.1, tailSeconds: 0.3);

        //Assert
        DecentSamplerRenderProbe.Peak(render).Should().Be(0.0);
    }

    [Fact]
    public void a_release_trigger_under_a_pedal_measures_its_hold_to_the_pedal_lift()
    {
        //Arrange
        // MEASURED (round 2, item 25): a key held 1.0 s under a pedal lifted at 3.0 s fired AT THE
        // PEDAL LIFT and decayed by 0.5^3 = 0.125, the same as a key held 3.0 s with no pedal at all,
        // and 12 dB below what measuring the hold to the key release would give.
        using var world = Build(DecayPreset("0.5"));

        var synthesizer = world.NewSynthesizer();
        var rate = DecentSamplerEngineFixtures.SampleRate;

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 1.0);

        synthesizer.NoteOff(0, 60);
        var (underPedal, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 2.0);

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 0);
        var (afterLift, _2) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert
        DecentSamplerRenderProbe.Peak(underPedal).Should().Be(0.0);

        var level = DecentSamplerRenderProbe.Rms(afterLift, rate / 10, rate / 10);
        level.Should().BeApproximately(0.5 * Math.Pow(0.5, 3.0), 0.01);
    }

    private static string DecayPreset(string decay) =>
        $"""
        <DecentSampler>
          <groups>
            <group ampVelTrack="0">
              <sample path="Samples/silent.wav" rootNote="60" loNote="60" hiNote="60" />
            </group>
            <group trigger="release" releaseTriggerDecay="{decay}" ampVelTrack="0">
              <sample path="Samples/rel.wav" rootNote="60" loNote="60" hiNote="60" />
            </group>
          </groups>
        </DecentSampler>
        """;

    private static double LevelAfterRelease(ReleaseWorld world, double heldSeconds)
    {
        var render = world.PlayAndRelease(heldSeconds, tailSeconds: 0.2);
        return Level(render, heldSeconds + 0.1);
    }

    private static double Level(float[] samples, double seconds, double window = 0.01)
    {
        var rate = DecentSamplerEngineFixtures.SampleRate;
        var length = (int)(window * rate);
        var offset = Math.Clamp((int)(seconds * rate) - length / 2, 0, samples.Length - length);

        return DecentSamplerRenderProbe.Rms(samples, offset, length);
    }

    private static ReleaseWorld Build(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/rel.wav", value: 0.5f, frames: 44100 * 3);
        fixtures.WriteConstantWav("Samples/silent.wav", value: 0.0f, frames: 44100 * 5);

        return new ReleaseWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class ReleaseWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer() =>
            DecentSamplerRenderProbe.Synthesizer(instrument);

        // One continuous render across the whole note, so the returned buffer's time axis starts at the
        // note-on: a window at heldSeconds + 0.1 is a tenth of a second into the release layer.
        public float[] PlayAndRelease(double heldSeconds, double tailSeconds)
        {
            var synthesizer = NewSynthesizer();
            synthesizer.NoteOn(0, 60, 100);

            var heldBlocks = DecentSamplerRenderProbe.BlocksFor(synthesizer, heldSeconds);
            var (held, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, heldBlocks);

            synthesizer.NoteOff(0, 60);
            var (tail, _2) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, tailSeconds);

            var whole = new float[held.Length + tail.Length];
            Array.Copy(held, whole, held.Length);
            Array.Copy(tail, 0, whole, held.Length, tail.Length);

            return whole;
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

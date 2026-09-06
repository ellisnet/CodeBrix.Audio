using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Note delays and pattern retriggers, in all three of the format's time units, driven by the
/// synthesizer's tempo source.
/// </summary>
public class DecentSamplerTimingTests
{
    [Fact]
    public void a_delay_in_seconds_holds_the_zone_back()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" delay="0.2" delayUnit="seconds">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.4);

        //Assert
        Level(render, 0.1).Should().BeLessThan(0.001);
        Level(render, 0.3).Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void a_delay_in_beats_follows_the_tempo()
    {
        //Arrange - one beat at 120 BPM is half a second.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" delay="1" delayUnit="beats">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var atOneTwenty = world.PlayFor(0.8);
        var atSixty = world.PlayFor(0.8, tempo => tempo.BeatsPerMinute = 60.0);

        //Assert - at 60 BPM the same beat is a whole second, so the note has not started yet.
        Level(atOneTwenty, 0.4).Should().BeLessThan(0.001);
        Level(atOneTwenty, 0.7).Should().BeApproximately(0.5, 0.01);
        Level(atSixty, 0.7).Should().BeLessThan(0.001);
    }

    [Fact]
    public void a_delay_in_samples_is_frame_accurate()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" delay="8820" delayUnit="samples">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.4);

        //Assert - 8820 frames is 200 ms at 44.1 kHz.
        Level(render, 0.15).Should().BeLessThan(0.001);
        Level(render, 0.3).Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void zones_with_different_delays_build_a_pattern()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" delay="0" />
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" delay="0.2" />
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" delay="0.4" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.7);

        //Assert - three 50 ms blips, 200 ms apart.
        Level(render, 0.02).Should().BeApproximately(0.5, 0.02);
        Level(render, 0.12).Should().BeLessThan(0.001);
        Level(render, 0.22).Should().BeApproximately(0.5, 0.02);
        Level(render, 0.32).Should().BeLessThan(0.001);
        Level(render, 0.42).Should().BeApproximately(0.5, 0.02);
    }

    [Fact]
    public void retrigger_repeats_the_pattern_while_the_key_is_held()
    {
        //Arrange - a 50 ms blip retriggered every beat, which is 500 ms at 120 BPM.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" retriggerEnabled="true" retriggerInterval="1"
                       retriggerIntervalUnit="beats">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(1.7);

        //Assert
        Level(render, 0.02).Should().BeApproximately(0.5, 0.02);
        Level(render, 0.25).Should().BeLessThan(0.001);
        Level(render, 0.52).Should().BeApproximately(0.5, 0.02);
        Level(render, 0.75).Should().BeLessThan(0.001);
        Level(render, 1.02).Should().BeApproximately(0.5, 0.02);
        Level(render, 1.52).Should().BeApproximately(0.5, 0.02);
    }

    [Fact]
    public void retrigger_stops_at_the_note_off()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" retriggerEnabled="true" retriggerInterval="0.25"
                       retriggerIntervalUnit="seconds">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);
        synthesizer.NoteOff(0, 60);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.8);

        //Assert - no further blips after the key came up.
        DecentSamplerRenderProbe.Rms(left, 8820, 8820).Should().BeLessThan(0.001);
    }

    [Fact]
    public void retrigger_off_by_default_plays_the_zone_once()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(1.2);

        //Assert
        Level(render, 0.02).Should().BeApproximately(0.5, 0.02);
        Level(render, 0.6).Should().BeLessThan(0.001);
        Level(render, 1.1).Should().BeLessThan(0.001);
    }

    private static double Level(float[] samples, double seconds, double window = 0.01)
    {
        var rate = DecentSamplerEngineFixtures.SampleRate;
        var length = (int)(window * rate);
        var offset = Math.Clamp((int)(seconds * rate) - length / 2, 0, samples.Length - length);

        return DecentSamplerRenderProbe.Rms(samples, offset, length);
    }

    private static TimingWorld Build(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 44100 * 2);
        fixtures.WriteConstantWav("Samples/blip.wav", value: 0.5f, frames: 2205);

        return new TimingWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class TimingWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer() =>
            DecentSamplerRenderProbe.Synthesizer(instrument);

        public float[] PlayFor(double seconds, Action<TempoSource> configureTempo = null)
        {
            var synthesizer = NewSynthesizer();
            configureTempo?.Invoke(synthesizer.TempoSource);

            synthesizer.NoteOn(0, 60, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, seconds);
            return left;
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

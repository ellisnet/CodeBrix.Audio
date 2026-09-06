using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Loops: marker precedence, the loopEnabled default, start and end offsets, and both crossfade curves,
/// all against the measured reference behaviour (plan section 7 item 11).
/// </summary>
/// <remarks>
/// The samples are constant-valued sections, so the level of a window says which part of the file is
/// sounding: section 0 is 0.2, section 1 is 0.5, section 2 is 0.8.
/// </remarks>
public class DecentSamplerLoopTests
{
    private const int SectionFrames = 4410; // 100 ms at 44.1 kHz.

    [Fact]
    public void explicit_loop_points_hold_the_zone_inside_them()
    {
        //Arrange - loop the middle section.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/sections.wav" rootNote="60" loNote="60" hiNote="60"
                          loopStart="4410" loopEnd="8819" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.8);

        //Assert - the third section never arrives.
        Level(render, 0.05).Should().BeApproximately(0.2, 0.01);
        Level(render, 0.35).Should().BeApproximately(0.5, 0.01);
        Level(render, 0.75).Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void a_sample_with_no_loop_plays_once_and_stops()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/sections.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.6);

        //Assert - three 100 ms sections and then nothing.
        Level(render, 0.25).Should().BeApproximately(0.8, 0.01);
        Level(render, 0.45).Should().BeLessThan(0.001);
    }

    [Fact]
    public void an_embedded_smpl_marker_turns_looping_on_by_itself()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/marked.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.8);

        //Assert - the marker names the middle section, which is 16000/32767 of full scale.
        Level(render, 0.35).Should().BeApproximately(16000.0 / 32767.0, 0.01);
        Level(render, 0.75).Should().BeApproximately(16000.0 / 32767.0, 0.01);
    }

    [Fact]
    public void loopEnabled_false_beats_an_embedded_marker()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/marked.wav" rootNote="60" loNote="60" hiNote="60" loopEnabled="false" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.6);

        //Assert
        Level(render, 0.25).Should().BeApproximately(24000.0 / 32767.0, 0.01);
        Level(render, 0.45).Should().BeLessThan(0.001);
    }

    [Fact]
    public void explicit_loop_points_override_an_embedded_marker()
    {
        //Arrange - the marker names the middle section; the attributes name the last one.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/marked.wav" rootNote="60" loNote="60" hiNote="60"
                          loopStart="8820" loopEnd="13229" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.8);

        //Assert
        Level(render, 0.75).Should().BeApproximately(24000.0 / 32767.0, 0.01);
    }

    [Fact]
    public void a_start_offset_skips_into_the_sample()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/sections.wav" rootNote="60" loNote="60" hiNote="60" start="8820" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.2);

        //Assert - the render begins in the third section.
        Level(render, 0.02).Should().BeApproximately(0.8, 0.01);
    }

    [Fact]
    public void an_end_offset_stops_the_zone_early()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/sections.wav" rootNote="60" loNote="60" hiNote="60" end="4409" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        var render = world.PlayFor(0.3);

        //Assert - only the first section plays.
        Level(render, 0.05).Should().BeApproximately(0.2, 0.01);
        Level(render, 0.2).Should().BeLessThan(0.001);
    }

    [Fact]
    public void the_equal_power_crossfade_is_the_square_root_pair()
    {
        //Arrange - the audio before the loop is 0.25, the loop body is 1.0, so the crossfade midpoint
        //reads sqrt(0.5)*1.0 + sqrt(0.5)*0.25 = 0.8839.
        using var world = Build(CrossfadePreset("equal_power"), crossfadeFixture: true);

        //Act
        var render = world.PlayFor(0.5);

        //Assert
        Level(render, 0.175, 0.006).Should().BeApproximately(0.8839, 0.03);
        Level(render, 0.275, 0.006).Should().BeApproximately(0.8839, 0.03);
    }

    [Fact]
    public void the_linear_crossfade_is_the_straight_pair()
    {
        //Arrange - the same seam read with the linear law: 0.5*1.0 + 0.5*0.25 = 0.625.
        using var world = Build(CrossfadePreset("linear"), crossfadeFixture: true);

        //Act
        var render = world.PlayFor(0.5);

        //Assert
        Level(render, 0.175, 0.006).Should().BeApproximately(0.625, 0.03);
    }

    [Fact]
    public void the_crossfade_default_is_equal_power()
    {
        //Arrange
        using var world = Build(CrossfadePreset(null), crossfadeFixture: true);

        //Act
        var render = world.PlayFor(0.5);

        //Assert
        Level(render, 0.175, 0.006).Should().BeApproximately(0.8839, 0.03);
    }

    [Fact]
    public void a_crossfade_does_not_shorten_the_loop()
    {
        //Arrange
        using var world = Build(CrossfadePreset("equal_power"), crossfadeFixture: true);

        //Act
        var render = world.PlayFor(0.6);

        //Assert - the seam recurs every 100 ms, the loop's own length, not sooner.
        Level(render, 0.125, 0.004).Should().BeApproximately(1.0, 0.03);
        Level(render, 0.225, 0.004).Should().BeApproximately(1.0, 0.03);
        Level(render, 0.325, 0.004).Should().BeApproximately(1.0, 0.03);
    }

    private static string CrossfadePreset(string mode)
    {
        var modeAttribute = mode == null ? string.Empty : $""" loopCrossfadeMode="{mode}" """;

        return $"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/crossfade.wav" rootNote="60" loNote="60" hiNote="60"
                          loopStart="4410" loopEnd="8819" loopCrossfade="2205"{modeAttribute}/>
                </group>
              </groups>
            </DecentSampler>
            """;
    }

    private static double Level(float[] samples, double seconds, double window = 0.01)
    {
        var rate = DecentSamplerEngineFixtures.SampleRate;
        var length = (int)(window * rate);
        var offset = Math.Clamp((int)(seconds * rate) - length / 2, 0, samples.Length - length);

        return DecentSamplerRenderProbe.Rms(samples, offset, length);
    }

    private static LoopWorld Build(string preset, bool crossfadeFixture = false)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();

        fixtures.WriteSectionedWav("Samples/sections.wav", SectionFrames, 0.2f, 0.5f, 0.8f);
        fixtures.WriteSmplLoopWav("Samples/marked.wav", SectionFrames, 4410, 8819);

        if (crossfadeFixture)
        {
            // 100 ms at 0.25 in front of the loop, then a long run of 1.0 to loop over.
            fixtures.WriteSectionedWav("Samples/crossfade.wav", SectionFrames, 0.25f, 1.0f, 1.0f, 1.0f);
        }

        return new LoopWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class LoopWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public float[] PlayFor(double seconds)
        {
            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
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

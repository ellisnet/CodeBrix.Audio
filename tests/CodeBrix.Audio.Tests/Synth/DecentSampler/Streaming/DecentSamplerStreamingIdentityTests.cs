using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// The acceptance test for streaming: the same instrument, the same notes, one decoded into memory and
/// one streamed from disk, must produce the same numbers. Not approximately - exactly.
/// </summary>
/// <remarks>
/// This is what licenses the streamed path to exist at all. It is also the fence around the one idea
/// the design turns on: the reader wraps the loop and the audio thread plays a straight line, which has
/// to hand the oscillator the same sequence of frames its in-memory sibling reads by wrapping its own
/// position.
/// </remarks>
public class DecentSamplerStreamingIdentityTests
{
    private const string SinePreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="{MODE}">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    private const string LoopedPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="{MODE}">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127"
                      loopStart="1000" loopEnd="2999" loopEnabled="true" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_streamed_one_shot_matches_the_decoded_one_sample_for_sample()
    {
        //Arrange
        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(SinePreset, "memory"), 60, 400);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(SinePreset, "disk_streaming"), 60, 400);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
        DecentSamplerRenderProbe.Rms(streamed.Left).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void a_streamed_sustained_loop_matches_across_many_seams()
    {
        //Arrange - a 2,000-frame loop rendered for 25,600 frames crosses the seam a dozen times.
        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "memory"), 60, 400);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "disk_streaming"), 60, 400);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
        DecentSamplerRenderProbe.Rms(streamed.Left, 20000, 5000).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void a_streamed_note_that_starts_past_the_preload_head_matches()
    {
        //Arrange - the head is 64 frames and the zone starts at 20,000, so every frame comes off disk.
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="{MODE}">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" start="20000" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(preset, "memory", 64), 60, 200);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(preset, "disk_streaming", 64), 60, 200);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
        DecentSamplerRenderProbe.Rms(streamed.Left, 0, 1000).Should().BeGreaterThan(0.05);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("equal_power")]
    public void a_streamed_loop_crossfade_matches(string crossfadeMode)
    {
        //Arrange - the fading-in audio sits a whole loop length behind the read head, which is the one
        //place the streamed path cannot use its ring buffer.
        var preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="{MODE}">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127"
                          loopStart="4000" loopEnd="7999" loopEnabled="true"
                          loopCrossfade="500" loopCrossfadeMode="{CROSSFADE}" />
                </group>
              </groups>
            </DecentSampler>
            """.Replace("{CROSSFADE}", crossfadeMode, StringComparison.Ordinal);

        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(preset, "memory", 128), 60, 500);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(preset, "disk_streaming", 128), 60, 500);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
        DecentSamplerRenderProbe.Rms(streamed.Left, 20000, 5000).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void a_streamed_stereo_zone_matches_on_both_channels()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="{MODE}">
                  <sample path="Samples/stereo.wav" rootNote="60" loNote="0" hiNote="127"
                          loopStart="500" loopEnd="1499" loopEnabled="true" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteStereoConstantWav("Samples/stereo.wav", 0.4f, -0.7f, frames: 20000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(preset, "memory"), 60, 300);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(preset, "disk_streaming"), 60, 300);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
        DecentSamplerStreamingWorld.FirstDifference(memory.Right, streamed.Right).Should().Be(-1);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(60)]
    [InlineData(76)]
    public void a_streamed_zone_resamples_the_same_way_at_every_pitch(int note)
    {
        //Arrange - the resampling ratio is where a streamed read head is easiest to get wrong, because
        //it advances by a fraction of a frame per output sample.
        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "memory"), note, 300);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "disk_streaming"), note, 300);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
    }

    [Fact]
    public void a_streamed_zone_at_a_different_recorded_rate_matches()
    {
        //Arrange - a 96 kHz library played at 44.1 kHz, which is the case the plan named.
        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 60000, sampleRate: 96000);

        //Act
        var memory = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "memory"), 60, 300);
        var streamed = DecentSamplerStreamingWorld.Play(world.Load(LoopedPreset, "disk_streaming"), 60, 300);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(memory.Left, streamed.Left).Should().Be(-1);
    }

    [Fact]
    public void the_instrument_reports_what_it_streamed()
    {
        //Arrange
        using var world = DecentSamplerStreamingWorld.Create();
        world.Fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        var streamed = world.Load(SinePreset, "disk_streaming");

        //Assert
        streamed.StreamedSampleCount.Should().Be(1);
        streamed.MemoryPolicySummary.Should().Contain("1 streamed");
        streamed.DecodedByteCount.Should().BeLessThan(40000 * sizeof(float));
    }
}

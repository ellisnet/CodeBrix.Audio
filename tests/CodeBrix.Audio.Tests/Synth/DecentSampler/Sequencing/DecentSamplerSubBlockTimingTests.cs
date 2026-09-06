using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// The voice runtime's sub-block start offset, which is what makes a generated note land on its own
/// frame. It also fixes the format's own <c>delay</c> attribute: a delay that is not a whole number of
/// render blocks used to be rounded down to the block boundary before it.
/// </summary>
public class DecentSamplerSubBlockTimingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(4409)]
    public void a_delay_in_samples_starts_on_the_exact_frame(int delayFrames)
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001"
                       delay="{{delayFrames}}" delayUnit="samples">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.Render(128));

        //Assert
        onsets.Should().HaveCount(1);
        onsets[0].Should().Be(delayFrames);
    }

    [Fact]
    public void a_sub_block_start_does_not_skip_any_of_the_sample()
    {
        //Arrange - three 128-frame sections at three levels, started 37 frames into the first block.
        using var world = SequencingWorld.Build(
            """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001"
                       delay="37" delayUnit="samples">
                  <sample path="Samples/steps.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" />
                </group>
              </groups>
            </DecentSampler>
            """,
            prepare: fixtures => fixtures.WriteSectionedWav("Samples/steps.wav", 128, 0.1f, 0.5f, 0.9f));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.Render(8);

        //Assert - every section is its full 128 frames long, just moved along by 37. A source that
        // ran on through the silence in front of the note would shorten the first section instead.
        render[36].Should().Be(0f);
        render[37].Should().BeApproximately(0.1f, 1e-5f);
        render[164].Should().BeApproximately(0.1f, 1e-5f);
        render[165].Should().BeApproximately(0.5f, 1e-5f);
        render[292].Should().BeApproximately(0.5f, 1e-5f);
        render[293].Should().BeApproximately(0.9f, 1e-5f);
    }

    [Fact]
    public void the_frames_before_the_delay_are_silent()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001"
                       delay="37" delayUnit="samples">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" />
                </group>
              </groups>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.Render(4);

        //Assert - digital silence up to the frame, and the sample's own level from it.
        DecentSamplerRenderProbe.Peak(render, 0, 37).Should().Be(0.0);
        DecentSamplerRenderProbe.Rms(render, 37, 128)
            .Should().BeApproximately(SequencingWorld.BlipLevel, 1e-6);
    }
}

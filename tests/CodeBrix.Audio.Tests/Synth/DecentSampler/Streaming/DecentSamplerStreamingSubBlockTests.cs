using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// The streamed voice source under a SUB-BLOCK start. <c>IVoiceSource.Render</c> may be called with
/// fewer frames than a whole block on a voice's first sounding block - a note delay, or a sequenced or
/// arpeggiated note - and a source that ignores the count runs on through the silence in front of the
/// note and swallows that much of the sample.
/// </summary>
/// <remarks>
/// The in-memory source was given its frame count when the sub-block start landed; the streamed source
/// was written before that and was not. These tests are the fence.
/// </remarks>
public class DecentSamplerStreamingSubBlockTests
{
    private const string StreamedSteps = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001"
                   playbackMode="disk_streaming" delay="37" delayUnit="samples">
              <sample path="Samples/steps.wav" rootNote="60" loNote="0" hiNote="127"
                      pitchKeyTrack="0" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_streamed_sub_block_start_does_not_skip_any_of_the_sample()
    {
        //Arrange - three 128-frame sections at three levels, started 37 frames into the first block.
        using var world = SequencingWorld.Build(
            StreamedSteps,
            configure: settings => settings.StreamingMode = DecentSamplerStreamingMode.Offline,
            prepare: fixtures => fixtures.WriteSectionedWav("Samples/steps.wav", 128, 0.1f, 0.5f, 0.9f));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.Render(8);

        //Assert - every section is its full 128 frames long, just moved along by 37.
        world.Instrument.StreamedSampleCount.Should().Be(1);
        render[36].Should().Be(0f);
        render[37].Should().BeApproximately(0.1f, 1e-5f);
        render[164].Should().BeApproximately(0.1f, 1e-5f);
        render[165].Should().BeApproximately(0.5f, 1e-5f);
        render[292].Should().BeApproximately(0.5f, 1e-5f);
        render[293].Should().BeApproximately(0.9f, 1e-5f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(100)]
    public void a_streamed_zone_starts_on_the_exact_frame(int delayFrames)
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001"
                       playbackMode="disk_streaming" delay="{{delayFrames}}" delayUnit="samples">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" />
                </group>
              </groups>
            </DecentSampler>
            """,
            configure: settings => settings.StreamingMode = DecentSamplerStreamingMode.Offline);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.Render(128));

        //Assert
        onsets.Should().HaveCount(1);
        onsets[0].Should().Be(delayFrames);
    }

    [Fact]
    public void a_streamed_sub_block_start_matches_the_decoded_one_sample_for_sample()
    {
        //Arrange - the same preset twice, one held in memory and one streamed, both delayed 37 frames.
        using var memoryWorld = SequencingWorld.Build(
            StreamedSteps.Replace("disk_streaming", "memory"),
            configure: settings => settings.StreamingMode = DecentSamplerStreamingMode.Offline,
            prepare: fixtures => fixtures.WriteSectionedWav("Samples/steps.wav", 128, 0.1f, 0.5f, 0.9f));

        using var streamedWorld = SequencingWorld.Build(
            StreamedSteps,
            configure: settings => settings.StreamingMode = DecentSamplerStreamingMode.Offline,
            prepare: fixtures => fixtures.WriteSectionedWav("Samples/steps.wav", 128, 0.1f, 0.5f, 0.9f));

        //Act
        memoryWorld.Synthesizer.NoteOn(0, 60, 100);
        streamedWorld.Synthesizer.NoteOn(0, 60, 100);

        var decoded = memoryWorld.Render(8);
        var streamed = streamedWorld.Render(8);

        //Assert
        DecentSamplerStreamingWorld.FirstDifference(decoded, streamed).Should().Be(-1);
        DecentSamplerRenderProbe.Peak(streamed).Should().BeGreaterThan(0.5);
    }
}

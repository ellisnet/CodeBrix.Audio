using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The other half of the feature-list contract: what the sampler engine has earned the right to call
/// Implemented. The negative half - that no phase claims a feature it has not reached - lives in
/// <see cref="DecentSamplerSupportedFeaturesTests"/>.
/// </summary>
public class DecentSamplerImplementedFeatureTests
{
    [Theory]
    [InlineData("groups")]
    [InlineData("group")]
    [InlineData("sample")]
    [InlineData("tags")]
    [InlineData("tag")]
    public void the_sound_elements_the_engine_plays_are_implemented(string element) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Element, string.Empty, element)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("sample", "path")]
    [InlineData("sample", "rootNote")]
    [InlineData("sample", "previousNotes")]
    [InlineData("sample", "legatoInterval")]
    [InlineData("sample", "loopCrossfade")]
    [InlineData("sample", "loopCrossfadeMode")]
    [InlineData("sample", "releaseTriggerDecay")]
    [InlineData("sample", "onLoCCN")]
    [InlineData("sample", "output1Target")]
    [InlineData("sample", "output8Volume")]
    [InlineData("group", "enabled")]
    [InlineData("group", "ampVelTrack")]
    [InlineData("group", "silencedByTags")]
    [InlineData("group", "retriggerInterval")]
    [InlineData("groups", "glideTime")]
    [InlineData("groups", "globalTuning")]
    [InlineData("tag", "polyphony")]
    public void the_attributes_the_engine_honours_are_implemented(string element, string attribute) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Attribute, element, attribute)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("trigger", "continuous")]
    [InlineData("seqMode", "true_random")]
    [InlineData("glideMode", "always")]
    [InlineData("silencingMode", "normal")]
    [InlineData("loopCrossfadeMode", "equal_power")]
    [InlineData("delayUnit", "beats")]
    [InlineData("outputNTarget", "MAIN_OUTPUT")]
    [InlineData("outputNTarget", "NO_OUTPUT")]
    public void the_enumeration_values_the_engine_acts_on_are_implemented(string attribute, string value) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.EnumerationValue, attribute, value)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("outputNTarget", "BUS_1")]
    [InlineData("outputNTarget", "BUS_16")]
    [InlineData("outputNTarget", "AUX_STEREO_OUTPUT_1")]
    [InlineData("outputNTarget", "AUX_STEREO_OUTPUT_16")]
    public void bus_and_auxiliary_routing_is_implemented(string attribute, string value) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.EnumerationValue, attribute, value)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("groups")]
    [InlineData("group")]
    [InlineData("sample")]
    public void streaming_playback_is_implemented_at_every_level(string element) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Attribute, element, "playbackMode")
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("memory")]
    [InlineData("disk_streaming")]
    [InlineData("auto")]
    public void the_playback_mode_values_are_implemented(string value) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.EnumerationValue, "playbackMode", value)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);

    [Theory]
    [InlineData("forward_loop")]
    [InlineData("ping_pong_loop")]
    public void the_animation_playback_mode_values_are_still_only_parsed(string value) =>
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.EnumerationValue, "playbackMode", value)
            .Should().Be(DecentSamplerFeatureStatus.Parsed);

    [Fact]
    public void the_list_now_holds_both_statuses()
    {
        //Arrange
        //Act
        var implemented = DecentSamplerSupportedFeatures.Features
            .Count(feature => feature.Status == DecentSamplerFeatureStatus.Implemented);

        //Assert
        implemented.Should().BeGreaterThan(100);
        implemented.Should().BeLessThan(DecentSamplerSupportedFeatures.Features.Count);
    }
}

using System;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The LIVE tag state: what a TAG_ENABLED or TAG_VOLUME binding does to the voices carrying that tag.
/// </summary>
/// <remarks>
/// The sampler engine reads tags through a seam so that an instrument with no bindings at all still
/// behaves - it reads the parsed <c>&lt;tag&gt;</c> elements then. These tests pin the other half: an
/// instrument WITH a binding engine reads the values a binding has moved.
/// </remarks>
public class DecentSamplerLiveTagStateTests
{
    [Fact]
    public void a_tag_enabled_binding_silences_the_tag_at_the_next_note_on()
    {
        //Arrange
        using var world = TagWorld.Build(Preset("TAG_ENABLED", "0"));

        //Act
        var level = world.Play();

        //Assert - the button's initial state fired the binding at load, so the layer never sounds.
        world.TagState("mic1").Enabled.Should().BeFalse();
        level.Should().Be(0.0);
    }

    [Fact]
    public void turning_a_tag_back_on_lets_it_sound_again()
    {
        //Arrange
        using var world = TagWorld.Build(Preset("TAG_ENABLED", "0"));
        var silent = world.Play();

        //Act
        world.Control.SetValue(1.0);
        var restored = world.Play();

        //Assert
        silent.Should().Be(0.0);
        restored.Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_tag_volume_binding_changes_the_level_of_the_tag_s_voices()
    {
        //Arrange
        using var world = TagWorld.Build(Preset("TAG_VOLUME", "0.25"));

        //Act
        var level = world.Play();

        //Assert
        world.TagState("mic1").Volume.Should().Be(0.25);
        level.Should().BeApproximately(0.125, 0.002);
    }

    [Fact]
    public void a_tag_volume_binding_moves_a_note_that_is_already_sounding()
    {
        //Arrange
        using var world = TagWorld.Build(Preset("TAG_VOLUME", "1.0"));

        //Act
        var before = world.PlayThenChange(1.0, 0.25);

        //Assert
        before.Before.Should().BeApproximately(0.5, 0.002);
        before.After.Should().BeApproximately(0.125, 0.01);
    }

    [Fact]
    public void an_instrument_with_no_bindings_still_reads_its_own_tag_elements()
    {
        //Arrange
        using var world = TagWorld.Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" tags="mic1" />
                </group>
              </groups>
              <tags>
                <tag name="mic1" volume="0.5" />
              </tags>
            </DecentSampler>
            """);

        //Act
        var level = world.Play();

        //Assert
        level.Should().BeApproximately(0.25, 0.002);
    }

    private static string Preset(string parameter, string value) => $"""
        <DecentSampler>
          <ui width="812" height="375">
            <tab>
              <labeled-knob x="0" y="0" width="60" height="60" parameterName="mic" minValue="0"
                            maxValue="1" value="{value}">
                <binding type="general" level="tag" identifier="mic1" parameter="{parameter}"
                         translation="linear" translationOutputMin="0" translationOutputMax="1" />
              </labeled-knob>
            </tab>
          </ui>
          <groups>
            <group ampVelTrack="0">
              <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" tags="mic1" />
            </group>
          </groups>
        </DecentSampler>
        """;

    private sealed class TagWorld : IDisposable
    {
        private readonly DecentSamplerEngineFixtures _fixtures;

        private TagWorld(DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument)
        {
            _fixtures = fixtures;
            Instrument = instrument;
            Synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        }

        public DecentSamplerInstrument Instrument { get; }

        public DecentSamplerSynthesizer Synthesizer { get; }

        public DecentSamplerControl Control => Instrument.Controls[0];

        public static TagWorld Build(string preset)
        {
            var fixtures = DecentSamplerEngineFixtures.Create();
            fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);

            return new TagWorld(fixtures, fixtures.LoadPreset(preset));
        }

        public DecentSamplerTagState TagState(string name) =>
            Instrument.TagStates.Single(state => state.Name == name);

        public double Play()
        {
            Synthesizer.Reset();
            Synthesizer.NoteOn(0, 60, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 8);
            return DecentSamplerRenderProbe.Rms(left, 64, left.Length - 64);
        }

        public (double Before, double After) PlayThenChange(double from, double to)
        {
            Synthesizer.Reset();
            Control.SetValue(from);
            Synthesizer.NoteOn(0, 60, 100);

            var (before, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 8);

            Control.SetValue(to);

            var (after, _) = DecentSamplerRenderProbe.RenderBlocks(Synthesizer, 8);

            return (
                DecentSamplerRenderProbe.Rms(before, 64, before.Length - 64),
                DecentSamplerRenderProbe.Rms(after, 128, after.Length - 128));
        }

        public void Dispose()
        {
            Instrument.Dispose();
            _fixtures.Dispose();
        }
    }
}

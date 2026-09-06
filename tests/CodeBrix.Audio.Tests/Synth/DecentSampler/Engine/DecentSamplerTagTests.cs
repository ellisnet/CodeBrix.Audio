using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Tags: TAG_ENABLED, TAG_VOLUME and TAG_POLYPHONY, and the silencedByTags voice muting that legato and
/// drum libraries are built on, with both silencing modes and the silencingDecay override.
/// </summary>
public class DecentSamplerTagTests
{
    [Fact]
    public void a_disabled_tag_silences_the_zones_that_carry_it()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="mic1" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group tags="mic2" ampVelTrack="0">
                  <sample path="Samples/b.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <tags>
                <tag name="mic1" enabled="false" />
              </tags>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.2, 0.002);
    }

    [Fact]
    public void a_tag_volume_scales_the_zones_that_carry_it()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="mic1" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <tags>
                <tag name="mic1" volume="0.5" />
              </tags>
            </DecentSampler>
            """);

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.05, 0.002);
    }

    [Theory]
    [InlineData("ta", 0.5)]
    [InlineData("tb", 0.25)]
    [InlineData("ta,tb", 0.125)]
    [InlineData("tb,ta", 0.125)]
    [InlineData("ta,tb,tc", 0.0625)]
    [InlineData("th", 1.0)]
    public void tag_volumes_multiply_in_any_order(string tags, double factor)
    {
        //Arrange
        // MEASURED (round 2, item 24): 0.5 x 0.25 = 0.125 and 0.5 x 0.25 x 0.5 = 0.0625, to four
        // decimal places, and the order does not matter. An UNDECLARED tag contributes nothing.
        using var world = Build(TagVolumePreset("tags=\"" + tags + "\"", string.Empty));

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.1 * factor, 0.002);
    }

    [Fact]
    public void a_group_tag_and_a_sample_tag_multiply_together()
    {
        //Arrange
        // MEASURED: the two tag lists are unioned, so it makes no difference which element a tag is on.
        using var world = Build(TagVolumePreset("tags=\"tb\"", "tags=\"ta\""));

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.0125, 0.002);
    }

    [Theory]
    [InlineData("2", 2.0)]
    [InlineData("4", 4.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("0", 0.0)]
    [InlineData("0.5xyz", 0.5)]
    [InlineData("-6", 6.0)]
    [InlineData("-6dB", 6.0)]
    public void a_tag_volume_uses_its_own_parser(string written, double expected)
    {
        //Arrange
        // MEASURED (round 2, item 24): a <tag>'s volume is the ABSOLUTE VALUE of a plain linear number.
        // The "dB" suffix is not recognised, trailing junk is ignored, and neither the documented 0-1
        // range nor the [0, 16] clamp a <sample> or <group> volume gets is enforced. Compare item 3,
        // where a sample volume DOES honour an exact "dB" suffix and turns a negative into silence.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="ta" ampVelTrack="0">
                  <sample path="Samples/quiet.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
              <tags>
                <tag name="ta" volume="VALUE" />
              </tags>
            </DecentSampler>
            """.Replace("VALUE", written));

        //Act
        var level = world.Play(60);

        //Assert - a source 20 dB down, so even a gain of four stays well inside full scale.
        level.Should().BeApproximately(0.01 * expected, 0.0005);
    }

    [Theory]
    [InlineData("te")]
    [InlineData("td")]
    [InlineData("th")]
    public void a_tag_never_bypasses_or_scales_an_effect_carrying_it(string tags)
    {
        //Arrange
        // MEASURED (round 2, item 24): four cases - a control with no tags, an enabled tag with a
        // volume of 0.5, a DISABLED tag, and an undeclared tag - all landed on the same -12.000 dB.
        // Tag enable and tag volume act on ZONES only.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                  <effects>
                    <effect type="gain" levelUnit="linear" level="0.25" tags="TAGS" />
                  </effects>
                </group>
              </groups>
              <tags>
                <tag name="te" enabled="true" volume="0.5" />
                <tag name="td" enabled="false" />
              </tags>
            </DecentSampler>
            """.Replace("TAGS", tags));

        //Act
        var level = world.Play(60);

        //Assert
        level.Should().BeApproximately(0.025, 0.002);
    }

    private static string TagVolumePreset(string sampleTags, string groupTags) =>
        """
        <DecentSampler>
          <groups>
            <group GROUPTAGS ampVelTrack="0">
              <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" SAMPLETAGS />
            </group>
          </groups>
          <tags>
            <tag name="ta" volume="0.5" />
            <tag name="tb" volume="0.25" />
            <tag name="tc" volume="0.5" />
          </tags>
        </DecentSampler>
        """.Replace("GROUPTAGS", groupTags).Replace("SAMPLETAGS", sampleTags);

    [Fact]
    public void tag_polyphony_of_one_makes_a_group_monophonic()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="mono" ampVelTrack="0" release="0.001">
                  <sample path="Samples/a.wav" rootNote="60" loNote="0" hiNote="127" pitchKeyTrack="0" />
                </group>
              </groups>
              <tags>
                <tag name="mono" polyphony="1" />
              </tags>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var one = world.Measure(synthesizer);

        synthesizer.NoteOn(0, 64, 100);
        var two = world.MeasureTail(synthesizer, blocks: 8);

        //Assert - the second note steals the first, so once its five-millisecond choke has run the
        //level is one voice, not two.
        one.Should().BeApproximately(0.1, 0.002);
        two.Should().BeApproximately(0.1, 0.002);
    }

    [Fact]
    public void tag_polyphony_of_two_allows_two_voices()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="duo" ampVelTrack="0" release="0.001">
                  <sample path="Samples/a.wav" rootNote="60" loNote="0" hiNote="127" pitchKeyTrack="0" />
                </group>
              </groups>
              <tags>
                <tag name="duo" polyphony="2" />
              </tags>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        var two = world.Measure(synthesizer);

        synthesizer.NoteOn(0, 67, 100);
        var three = world.MeasureTail(synthesizer, blocks: 8);

        //Assert
        two.Should().BeApproximately(0.2, 0.003);
        three.Should().BeApproximately(0.2, 0.003);
    }

    [Fact]
    public void an_unlimited_tag_lets_every_voice_sound()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="open" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="0" hiNote="127" pitchKeyTrack="0" />
                </group>
              </groups>
              <tags>
                <tag name="open" polyphony="-1" />
              </tags>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        synthesizer.NoteOn(0, 67, 100);
        var level = world.Measure(synthesizer);

        //Assert
        level.Should().BeApproximately(0.3, 0.004);
    }

    [Fact]
    public void silencedByTags_stops_the_earlier_voice_when_the_named_tag_plays()
    {
        //Arrange - the guide's drum example: one hi-hat silences another.
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="hihat" silencedByTags="hihat" silencingMode="fast" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                  <sample path="Samples/b.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var open = world.Measure(synthesizer);

        synthesizer.NoteOn(0, 62, 100);
        var closed = world.MeasureTail(synthesizer, blocks: 16);

        //Assert - only the newer sample is left, so the level is its 0.2 rather than 0.3.
        open.Should().BeApproximately(0.1, 0.002);
        closed.Should().BeApproximately(0.2, 0.002);
    }

    [Fact]
    public void a_zone_that_lists_no_silencing_tag_is_left_alone()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="pad" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group tags="hihat" ampVelTrack="0">
                  <sample path="Samples/b.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 62, 100);
        var level = world.Measure(synthesizer);

        //Assert
        level.Should().BeApproximately(0.3, 0.004);
    }

    [Fact]
    public void silencingMode_normal_runs_the_zones_own_release()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="sustain" silencedByTags="legato" silencingMode="normal" release="0.5"
                       releaseCurve="0" ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group tags="legato" ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        world.Measure(synthesizer);

        //Act
        synthesizer.NoteOn(0, 62, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert - a long release, not a cut: still audible a quarter of a second in.
        DecentSamplerRenderProbe.Rms(left, 11025 - 512, 1024).Should().BeGreaterThan(0.02);
        DecentSamplerRenderProbe.Rms(left, left.Length - 512, 512).Should().BeLessThan(0.01);
    }

    [Fact]
    public void silencingMode_fast_cuts_within_a_few_milliseconds()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="sustain" silencedByTags="legato" silencingMode="fast" release="2.0"
                       ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group tags="legato" ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        world.Measure(synthesizer);

        //Act
        synthesizer.NoteOn(0, 62, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.1);

        //Assert - gone well inside 50 ms, despite the two-second release, and gone inside the MEASURED
        // choke time of two milliseconds plus the one 64-frame block the fade is granular to.
        // (Round 2, item 25: the reference chokes a fast-silenced voice inside about 2 ms, not 5.)
        DecentSamplerRenderProbe.Rms(left, 2205, 1024).Should().BeLessThan(0.001);
        DecentSamplerRenderProbe.Rms(left, 256, 128).Should().BeLessThan(0.001);
        DecentSamplerDefaults.FastSilencingSeconds.Should().Be(0.002);
    }

    [Fact]
    public void silencingDecay_is_the_envelopes_release_with_that_time_and_curve()
    {
        //Arrange
        using var world = Build("""
            <DecentSampler>
              <groups>
                <group tags="sustain" silencedByTags="legato" silencingMode="fast" silencingDecay="0.2"
                       ampVelTrack="0">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
                <group tags="legato" ampVelTrack="0">
                  <sample path="Samples/silent.wav" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """);

        var synthesizer = world.NewSynthesizer();
        synthesizer.NoteOn(0, 60, 100);
        world.Measure(synthesizer);

        //Act
        synthesizer.NoteOn(0, 62, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert
        // MEASURED (round 2, item 25): silencingDecay="0.5" and silencingMode="normal" with
        // release="0.5" land on the SAME CURVE to within 0.2 dB, so silencingDecay is the amplitude
        // envelope's release with that time and the group's default releaseCurve of +100 - not a
        // linear fade. At 100 ms into a 200 ms decay the reference measured -18.6 dB below the steady
        // level; a linear fade would have been -6.0 dB.
        var steady = 0.1;
        var atHundredMilliseconds = DecentSamplerRenderProbe.Rms(left, 4410 - 256, 512);

        (20.0 * Math.Log10(atHundredMilliseconds / steady)).Should().BeApproximately(-18.6, 1.5);
        DecentSamplerRenderProbe.Rms(left, 11025, 512).Should().BeLessThan(0.001);
    }

    private static TagWorld Build(string preset)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/a.wav", value: 0.1f);
        fixtures.WriteConstantWav("Samples/b.wav", value: 0.2f);
        fixtures.WriteConstantWav("Samples/silent.wav", value: 0.0f);
        fixtures.WriteConstantWav("Samples/quiet.wav", value: 0.01f);

        return new TagWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class TagWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        public DecentSamplerSynthesizer NewSynthesizer() =>
            DecentSamplerRenderProbe.Synthesizer(instrument);

        public double Play(int key, int velocity = 100)
        {
            var synthesizer = NewSynthesizer();
            synthesizer.NoteOn(0, key, velocity);
            return Measure(synthesizer);
        }

        public double Measure(DecentSamplerSynthesizer synthesizer, int blocks = 4)
        {
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, blocks);
            return DecentSamplerRenderProbe.Rms(left);
        }

        // The level once any silencing fade has run: a whole-window reading would average the fade in
        // with the steady level and report something in between.
        public double MeasureTail(DecentSamplerSynthesizer synthesizer, int blocks)
        {
            var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, blocks);
            return DecentSamplerRenderProbe.Rms(left, left.Length - 128, 128);
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

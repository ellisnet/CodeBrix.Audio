using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Glide: the three modes, the default, and the shape of the pitch ramp, against the measured reference
/// behaviour (plan section 7 item 10b).
/// </summary>
/// <remarks>
/// Measured: glideTime is the TOTAL transition time in seconds whatever the interval, the ramp is
/// LINEAR IN PITCH rather than in frequency, glideMode defaults to legato, and "always" takes its start
/// pitch from the last note triggered anywhere in the instrument.
/// </remarks>
public class DecentSamplerGlideTests
{
    private const double RootHz = 440.0;

    [Fact]
    public void glideMode_always_slides_even_from_a_finished_note()
    {
        //Arrange
        using var world = Build("always", glideTime: 0.5);

        //Act
        var render = world.PlaySeparatedPair(60, 63);

        //Assert - three semitones up from 440 Hz, reached after half a second.
        Pitch(render, 0.02).Should().BeApproximately(RootHz, 8.0);
        Pitch(render, 0.6).Should().BeApproximately(Semitones(3), 8.0);
    }

    [Fact]
    public void glideMode_legato_only_slides_from_a_sounding_note()
    {
        //Arrange
        using var world = Build("legato", glideTime: 0.5);

        //Act
        var separated = world.PlaySeparatedPair(60, 63);
        var overlapping = world.PlayOverlappingPair(60, 63);

        //Assert
        Pitch(separated, 0.02).Should().BeApproximately(Semitones(3), 8.0);
        Pitch(overlapping, 0.05).Should().BeLessThan(470.0);
        Pitch(overlapping, 0.6).Should().BeApproximately(Semitones(3), 8.0);
    }

    [Fact]
    public void no_glideMode_behaves_as_legato()
    {
        //Arrange
        using var world = Build(null, glideTime: 0.5);

        //Act
        var separated = world.PlaySeparatedPair(60, 63);
        var overlapping = world.PlayOverlappingPair(60, 63);

        //Assert
        Pitch(separated, 0.02).Should().BeApproximately(Semitones(3), 8.0);
        Pitch(overlapping, 0.05).Should().BeLessThan(470.0);
    }

    [Fact]
    public void glideMode_off_never_slides()
    {
        //Arrange
        using var world = Build("off", glideTime: 0.5);

        //Act
        var overlapping = world.PlayOverlappingPair(60, 63);

        //Assert
        Pitch(overlapping, 0.05).Should().BeApproximately(Semitones(3), 10.0);
    }

    [Fact]
    public void a_glide_time_of_zero_never_slides()
    {
        //Arrange
        using var world = Build("always", glideTime: 0.0);

        //Act
        var render = world.PlaySeparatedPair(60, 63);

        //Assert
        Pitch(render, 0.02).Should().BeApproximately(Semitones(3), 12.0);
    }

    [Fact]
    public void the_ramp_is_linear_in_pitch_and_takes_the_whole_glide_time()
    {
        //Arrange
        using var world = Build("always", glideTime: 0.5);

        //Act
        var render = world.PlaySeparatedPair(60, 63);

        //Assert - a straight line in semitones: one, two and three semitones at a third, two thirds and
        //all of the transition. Linear in FREQUENCY would read 468, 496 and 523 instead.
        Pitch(render, 0.1667).Should().BeApproximately(Semitones(1.0), 6.0);
        Pitch(render, 0.3333).Should().BeApproximately(Semitones(2.0), 6.0);
        Pitch(render, 0.55).Should().BeApproximately(Semitones(3.0), 6.0);
    }

    [Fact]
    public void the_glide_time_is_the_total_time_whatever_the_interval()
    {
        //Arrange
        using var world = Build("always", glideTime: 0.5);

        //Act
        var small = world.PlaySeparatedPair(60, 62);
        var large = world.PlaySeparatedPair(60, 67);

        //Assert - both arrive at their target at the same moment.
        Pitch(small, 0.55).Should().BeApproximately(Semitones(2), 6.0);
        Pitch(large, 0.55).Should().BeApproximately(Semitones(7), 12.0);
        Pitch(large, 0.25).Should().BeLessThan(Semitones(7) - 20.0);
    }

    [Fact]
    public void a_downward_glide_falls_to_the_target()
    {
        //Arrange
        using var world = Build("always", glideTime: 0.4);

        //Act
        var render = world.PlaySeparatedPair(60, 55);

        //Assert
        Pitch(render, 0.02).Should().BeApproximately(RootHz, 10.0);
        Pitch(render, 0.5).Should().BeApproximately(Semitones(-5), 6.0);
    }

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData("36", 36.0)]
    [InlineData("48", 36.0)]
    [InlineData("-48", -36.0)]
    public void groupTuning_is_clamped_to_three_octaves(string written, double expected)
    {
        //Arrange
        // MEASURED (round 2, item 24's "more behaviours"): groupTuning alone is clamped to plus or
        // minus 36 semitones. tuning and globalTuning are not.
        using var world = BuildTuned("groupTuning=\"" + written + "\"");

        //Act
        var render = world.PlayOne(60);

        //Assert
        Pitch(render, 0.05).Should().BeApproximately(Semitones(expected), Semitones(expected) * 0.03);
    }

    [Fact]
    public void tuning_itself_is_not_clamped()
    {
        //Arrange
        // Only groupTuning carries the clamp; four octaves up is well past its 36 semitones. The
        // attribute has to sit on the <sample>: a `tuning` written on a <group> IS the group's tuning,
        // which the resolver folds into groupTuning.
        using var world = BuildTuned(string.Empty, "tuning=\"48\"");

        //Act
        var render = world.PlayOne(60);

        //Assert
        Pitch(render, 0.05).Should().BeApproximately(Semitones(48.0), Semitones(48.0) * 0.03);
    }

    private static GlideWorld BuildTuned(string groupAttribute, string sampleAttribute = "")
    {
        var preset =
            "<DecentSampler><groups><group ampVelTrack=\"0\" " + groupAttribute + ">" +
            "<sample path=\"Samples/tone.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" " +
            sampleAttribute + " />" +
            "</group></groups></DecentSampler>";

        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: RootHz, frames: 44100 * 8);

        return new GlideWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private static double Semitones(double count) => RootHz * Math.Pow(2.0, count / 12.0);

    private static double Pitch(float[] samples, double seconds)
    {
        var rate = DecentSamplerEngineFixtures.SampleRate;
        const int window = 2048;
        var offset = Math.Clamp((int)(seconds * rate) - window / 2, 0, samples.Length - window);

        return DecentSamplerRenderProbe.Frequency(samples, offset, window, rate);
    }

    private static GlideWorld Build(string glideMode, double glideTime)
    {
        var modeAttribute = glideMode == null ? string.Empty : $""" glideMode="{glideMode}" """;

        var preset = $"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" release="0.001"
                       glideTime="{glideTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}"{modeAttribute}>
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: RootHz, frames: 44100 * 4);

        return new GlideWorld(fixtures, fixtures.LoadPreset(preset));
    }

    private sealed class GlideWorld(
        DecentSamplerEngineFixtures fixtures, DecentSamplerInstrument instrument) : IDisposable
    {
        // The first note sounds, is released and decays away; the returned buffer starts at the SECOND
        // note-on, so a window at t is t seconds into the glide.
        public float[] PlaySeparatedPair(int firstKey, int secondKey)
        {
            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

            synthesizer.NoteOn(0, firstKey, 100);
            DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);
            synthesizer.NoteOff(0, firstKey);
            DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

            synthesizer.NoteOn(0, secondKey, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.8);
            return left;
        }

        // The first note is still sounding when the second arrives, which is what "legato" means here.
        // Its key is released at once and the preset gives it a one-millisecond release, so the pitch
        // measured a few hundredths of a second later belongs to the new voice alone.
        public float[] PlayOverlappingPair(int firstKey, int secondKey)
        {
            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

            synthesizer.NoteOn(0, firstKey, 100);
            DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

            synthesizer.NoteOn(0, secondKey, 100);
            synthesizer.NoteOff(0, firstKey);

            var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.8);
            return left;
        }

        // One note, rendered from its own note-on.
        public float[] PlayOne(int key)
        {
            var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

            synthesizer.NoteOn(0, key, 100);
            var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.3);
            return left;
        }

        public void Dispose()
        {
            instrument.Dispose();
            fixtures.Dispose();
        }
    }
}

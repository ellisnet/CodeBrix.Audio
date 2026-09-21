using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The listening fixtures rendered offline: a family tour, the kit pattern and the dense piece all
/// play through <c>SoundFontRenderer</c> and come out as music rather than as silence or as a
/// clipped mess.
/// </summary>
/// <remarks>
/// These are what the audible tests and the per-family <c>.wav</c> renders are built on, so a break
/// in the fixtures is caught here rather than in a listening session.
/// </remarks>
public class GeneralMidiRenderTests
{
    [Fact]
    public void a_family_tour_renders_as_music()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);
        MidiSequence tour = GmFixtures.FamilyTour(GeneralMidiProgramFamily.ChromaticPercussion);

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, tour, TimeSpan.FromSeconds(1.5));

        //Assert
        tour.Length.Should().BeGreaterThan(TimeSpan.FromSeconds(10));
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.05);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void the_kit_pattern_renders_as_a_drum_part()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);
        MidiSequence pattern = GmFixtures.PercussionPattern(2);

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, pattern, TimeSpan.FromSeconds(1.0));

        //Assert
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.1);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void the_percussion_tour_strikes_every_piece_of_the_kit()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);
        MidiSequence tour = GmFixtures.PercussionTour();

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, tour, TimeSpan.FromSeconds(1.0));

        //Assert
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.1);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void the_dense_fixture_fills_the_voice_pool_and_fits_inside_full_scale_once_the_master_is_down()
    {
        //Arrange
        // Thirty-odd voices at once. The bank is calibrated so that ONE note at full velocity fits
        // inside full scale at the family's default master volume of 0.5; thirty of them do not, and
        // the master volume is the control for that - exactly as it is on a hardware module. A
        // renderer that never touches it will clip a dense arrangement.
        GeneralMidiSynthesizer loud = new GeneralMidiSynthesizer(
            new GeneralMidiSynthesizerSettings(GmProbe.SampleRate) { MaximumPolyphony = 64 });

        GeneralMidiSynthesizer turnedDown = new GeneralMidiSynthesizer(
            new GeneralMidiSynthesizerSettings(GmProbe.SampleRate)
            {
                MaximumPolyphony = 64,
                MasterVolume = 0.25f,
            });

        MidiSequence dense = GmFixtures.DenseFixture(2.0);

        //Act
        float[] atDefault = SoundFontRenderer.Render(loud, dense, TimeSpan.FromSeconds(1.0));
        float[] quieter = SoundFontRenderer.Render(turnedDown, dense, TimeSpan.FromSeconds(1.0));

        //Assert
        GmProbe.Peak(atDefault).Should().BeGreaterThan(0.2);
        GmProbe.Peak(quieter).Should().BeLessThanOrEqualTo(1.0);
        GmProbe.Peak(quieter).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void every_family_has_a_tour_that_makes_a_sound()
    {
        //Arrange
        int silent = 0;

        //Act
        foreach (GeneralMidiProgramFamily family in GmFixtures.Families())
        {
            GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);

            // A short window at the start of each tour: enough to know the fixture is not empty
            // without rendering sixteen whole auditions.
            MidiSequencer sequencer = new MidiSequencer(synthesizer);
            sequencer.Play(GmFixtures.FamilyTour(family), loop: false);

            float[] left = new float[GmProbe.SampleRate];
            float[] right = new float[left.Length];
            sequencer.Render(left, right);

            if (GmProbe.Peak(left) < 0.02) { silent++; }
        }

        //Assert
        silent.Should().Be(0);
    }
}

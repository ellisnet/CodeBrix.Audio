using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The auditions themselves, checked without a device and without a listener: the tour really
/// visits all 128 programs in order, a family audition really plays its eight, the roll call really
/// strikes all 47 kit pieces, the four real pieces really load and really sound, and every cue
/// really lands where the announcement says it does.
/// </summary>
/// <remarks>
/// A listening session is expensive - it costs a person's attention in real time - so everything
/// that can be wrong with the material is caught here first. A cue that has drifted from the sound
/// it names is worse than no cue at all.
/// </remarks>
public class GeneralMidiAuditionTests
{
    [Fact]
    public void the_tour_names_all_128_programs_in_order()
    {
        //Arrange
        GmAuditionPlan plan = GmAudition.AllPrograms();

        //Act
        int[] programs = plan.Cues
            .Where(cue => cue.Number != GmAuditionCue.NoNumber)
            .Select(cue => cue.Number)
            .ToArray();

        //Assert
        programs.Should().HaveCount(GeneralMidi.ProgramCount);
        programs.Should().BeInAscendingOrder();
        programs[0].Should().Be(0);
        programs[GeneralMidi.ProgramCount - 1].Should().Be(GeneralMidi.ProgramCount - 1);
    }

    [Fact]
    public void the_tour_marks_the_start_of_every_family()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmAudition.AllPrograms();

        //Assert
        plan.Cues.Count(cue => cue.Number == GmAuditionCue.NoNumber).Should().Be(16);
    }

    [Fact]
    public void every_cue_names_the_program_the_sequence_changes_to_at_that_moment()
    {
        //Arrange
        GmAuditionPlan plan = GmAudition.AllPrograms();

        //Act
        // Every cue that names a program is two seconds and a beat after the one before it, which
        // is the tour's own spacing. A drifted cue would announce the wrong instrument.
        GmAuditionCue[] programCues = plan.Cues
            .Where(cue => cue.Number != GmAuditionCue.NoNumber)
            .ToArray();

        //Assert
        for (int i = 1; i < programCues.Length; i++)
        {
            (programCues[i].Start - programCues[i - 1].Start).TotalSeconds
                .Should().BeApproximately(2.5, 0.001);
        }

        programCues[0].Name.Should().Be(GeneralMidi.DisplayName(GeneralMidiProgram.AcousticGrandPiano));
    }

    [Fact]
    public void the_tour_runs_for_about_five_and_a_half_minutes()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmAudition.AllPrograms();

        //Assert - 128 programs at two and a half seconds each, less the last gap.
        plan.Length.TotalSeconds.Should().BeApproximately(319.5, 1.0);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void a_family_audition_plays_its_eight_programs_in_order(GeneralMidiProgramFamily family)
    {
        //Arrange
        int first = (int)family * GeneralMidi.ProgramsPerFamily;

        //Act
        GmAuditionPlan plan = GmAudition.Family(family);

        //Assert
        plan.Cues.Should().HaveCount(GeneralMidi.ProgramsPerFamily);
        plan.Cues.Select(cue => cue.Number).Should().Equal(
            Enumerable.Range(first, GeneralMidi.ProgramsPerFamily).ToArray());
        plan.Length.TotalSeconds.Should().BeApproximately(32.375, 0.5);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void a_family_audition_makes_a_sound_on_every_one_of_its_programs(
        GeneralMidiProgramFamily family)
    {
        //Arrange
        // Each family is auditioned in a register that suits it, which is a thing that can be got
        // wrong: a program silent above or below its range would be heard as a hole in the tour.
        GmAuditionPlan plan = GmAudition.Family(family);
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, plan.Sequence, TimeSpan.Zero);

        //Assert
        List<string> silent = [];

        foreach (GmAuditionCue cue in plan.Cues)
        {
            if (PeakInWindow(rendered, cue.Start, TimeSpan.FromSeconds(1.0)) < 0.005)
            {
                silent.Add(cue.Describe());
            }
        }

        silent.Should().BeEmpty();
    }

    [Fact]
    public void the_roll_call_names_every_piece_of_the_kit_from_35_to_81()
    {
        //Arrange
        GmAuditionPlan plan = GmAudition.PercussionRollCall();
        int expected = GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1;

        //Act
        int[] notes = plan.Cues.Select(cue => cue.Number).ToArray();

        //Assert
        notes.Should().HaveCount(expected);
        notes.Should().Equal(
            Enumerable.Range(GeneralMidi.LowestPercussionNote, expected).ToArray());
        plan.Cues[0].Name.Should().Be(
            GeneralMidi.DisplayName(GeneralMidiPercussion.AcousticBassDrum));
    }

    [Fact]
    public void the_groove_uses_the_open_hi_hat_and_both_things_that_choke_it()
    {
        //Arrange
        GmAuditionPlan plan = GmAudition.PercussionGroove(1);
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);

        //Act
        float[] rendered = SoundFontRenderer.Render(
            synthesizer, plan.Sequence, TimeSpan.FromSeconds(1.5));

        //Assert - four bars, one cue each, and it comes out as a drum part rather than silence.
        plan.Cues.Should().HaveCount(4);
        plan.Length.TotalSeconds.Should().BeApproximately(7.75, 0.3);
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.1);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void every_cue_falls_inside_the_audition_it_belongs_to()
    {
        //Arrange
        List<GmAuditionPlan> plans =
        [
            GmAudition.AllPrograms(),
            GmAudition.Family(GeneralMidiProgramFamily.Piano),
            GmAudition.PercussionRollCall(),
            GmAudition.PercussionGroove(),
            .. GmRealPieces.All(),
        ];

        //Act & Assert
        foreach (GmAuditionPlan plan in plans)
        {
            plan.Cues.Should().NotBeEmpty();

            for (int i = 0; i < plan.Cues.Count; i++)
            {
                plan.Cues[i].Start.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
                plan.Cues[i].Start.Should().BeLessThanOrEqualTo(plan.Length);

                if (i > 0)
                {
                    plan.Cues[i].Start.Should().BeGreaterThanOrEqualTo(plan.Cues[i - 1].Start);
                }
            }
        }
    }

    [Fact]
    public void the_ordinary_piece_is_the_piano_violin_and_flute_the_file_asks_for()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmRealPieces.Ordinary();

        //Assert
        plan.Cues.Where(cue => cue.Number != GmAuditionCue.NoNumber)
            .Select(cue => cue.Number)
            .Should().Equal(
                (int)GeneralMidiProgram.AcousticGrandPiano,
                (int)GeneralMidiProgram.Violin,
                (int)GeneralMidiProgram.Flute);

        plan.Length.TotalSeconds.Should().BeApproximately(60.0, 2.0);
    }

    [Fact]
    public void the_dense_piece_asks_for_a_program_on_the_percussion_channel()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmRealPieces.Dense();

        //Assert
        // Worth stating plainly, because it is what a General MIDI writer does and it is what the
        // listening session is listening for: this file names a program on channel 10.
        plan.Cues.Should().Contain(cue => cue.Name.Contains("the percussion channel"));
        plan.Length.TotalSeconds.Should().BeApproximately(33.4, 2.0);
    }

    [Fact]
    public void a_program_change_arriving_on_the_percussion_channel_leaves_the_kit_where_it_is()
    {
        //Arrange
        // A General MIDI writer normally sends a program change on channel 10 as well: on that
        // channel it chooses a drum KIT, and program 0 is the standard one. A synthesizer that
        // read it as "put a piano on channel 10" would lose the drum part of any ordinary file -
        // and the dense piece here is exactly such a file.
        GeneralMidiSynthesizer told = GmProbe.Build();
        told.ProcessMidiMessage(GmProbe.PercussionWireChannel, 0xC0, 0, 0);

        GeneralMidiSynthesizer untold = GmProbe.Build();

        //Act
        var withTheChange = GmProbe.PlayNote(
            told, GeneralMidi.PercussionChannel, (int)GeneralMidiPercussion.BassDrum1, 110, 0.3, 0.4);

        var without = GmProbe.PlayNote(
            untold, GeneralMidi.PercussionChannel, (int)GeneralMidiPercussion.BassDrum1, 110, 0.3, 0.4);

        //Assert
        told.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeTrue();
        GmProbe.LargestDifference(withTheChange.Left, without.Left).Should().BeLessThan(0.000001);
    }

    [Fact]
    public void the_dense_piece_still_has_the_kit_on_channel_10_when_it_finishes()
    {
        //Arrange
        GmAuditionPlan plan = GmRealPieces.Dense();
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);

        //Act
        SoundFontRenderer.Render(synthesizer, plan.Sequence, TimeSpan.Zero);

        //Assert
        synthesizer.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeTrue();
    }

    [Fact]
    public void the_duet_is_voiced_celesta_over_choir_aahs_one_voice_each()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmRealPieces.Duet();

        //Assert
        plan.Cues.Where(cue => cue.Number != GmAuditionCue.NoNumber)
            .Select(cue => cue.Number)
            .Should().Equal(GmRealPieces.DuetUpperVoiceProgram, GmRealPieces.DuetLowerVoiceProgram);

        plan.Length.Should().BeGreaterThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void the_air_is_one_voice_on_the_program_chosen_for_it()
    {
        //Arrange & Act
        GmAuditionPlan plan = GmRealPieces.Air();

        //Assert
        plan.Cues.Where(cue => cue.Number != GmAuditionCue.NoNumber)
            .Select(cue => cue.Number)
            .Should().Equal(GmRealPieces.AirProgram);

        plan.Length.Should().BeGreaterThan(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [MemberData(nameof(RealPieces))]
    public void a_real_piece_renders_as_music(int index)
    {
        //Arrange
        GmAuditionPlan plan = GmRealPieces.All()[index];
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(GmProbe.SampleRate);
        MidiSequencer sequencer = new MidiSequencer(synthesizer);
        sequencer.Play(plan.Sequence, loop: false);

        //Act
        // The first eight seconds are enough to know the piece is playing rather than silent, and
        // cheap enough to belong in the ungated suite; the whole of it is rendered under the
        // listening-render gate.
        float[] left = new float[GmProbe.SampleRate * 8];
        float[] right = new float[left.Length];
        sequencer.Render(left, right);

        //Assert
        GmProbe.Peak(left).Should().BeGreaterThan(0.02);
        GmProbe.Rms(left).Should().BeGreaterThan(0.001);
    }

    /// <summary>The sixteen General MIDI families, as theory data.</summary>
    /// <returns>One family per case.</returns>
    public static TheoryData<GeneralMidiProgramFamily> Families()
    {
        TheoryData<GeneralMidiProgramFamily> data = new TheoryData<GeneralMidiProgramFamily>();

        foreach (GeneralMidiProgramFamily family in GmAudition.Families())
        {
            data.Add(family);
        }

        return data;
    }

    /// <summary>The four real pieces, by their position in <c>GmRealPieces.All()</c>.</summary>
    /// <returns>One index per case.</returns>
    public static TheoryData<int> RealPieces() => new TheoryData<int>(0, 1, 2, 3);

    // The loudest sample of either channel inside a window of an interleaved stereo render.
    private static double PeakInWindow(float[] interleaved, TimeSpan start, TimeSpan length)
    {
        int from = (int)(start.TotalSeconds * GmProbe.SampleRate) * 2;
        int to = Math.Min(
            interleaved.Length, from + ((int)(length.TotalSeconds * GmProbe.SampleRate) * 2));

        double peak = 0.0;

        for (int i = Math.Max(0, from); i < to; i++)
        {
            double value = Math.Abs(interleaved[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }
}

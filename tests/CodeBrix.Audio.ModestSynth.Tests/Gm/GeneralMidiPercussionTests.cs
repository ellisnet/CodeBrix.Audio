using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The percussion kit: a note number chooses a voicing rather than a pitch, a kit piece is a
/// one-shot that ignores note-off, the pieces that cannot sound together cut each other off, and the
/// kit is laid out across the stereo field.
/// </summary>
public class GeneralMidiPercussionTests
{
    [Fact]
    public void a_note_number_chooses_a_kit_piece_rather_than_a_pitch()
    {
        //Arrange
        GeneralMidiSynthesizer kick = GmProbe.BuildForPercussion();
        GeneralMidiSynthesizer hat = GmProbe.BuildForPercussion();

        //Act
        var low = GmProbe.PlayNote(kick, 1, (int)GeneralMidiPercussion.BassDrum1, 110, 0.3, 0.2);
        var high = GmProbe.PlayNote(hat, 1, (int)GeneralMidiPercussion.ClosedHiHat, 110, 0.3, 0.2);

        //Assert
        // Two note numbers a few semitones apart on a melodic program would differ only in pitch;
        // here they are a bass drum and a hi-hat, which share nothing at all.
        GmProbe.Centroid(high.Left).Should().BeGreaterThan(GmProbe.Centroid(low.Left) * 4.0);
    }

    [Fact]
    public void the_kit_sounds_on_the_percussion_channel_of_a_multi_timbral_synthesizer()
    {
        //Arrange
        GeneralMidiSynthesizer multiTimbral = GmProbe.Build();
        GeneralMidiSynthesizer pinned = GmProbe.BuildForPercussion();

        //Act
        var onTen = GmProbe.PlayNote(
            multiTimbral, GeneralMidi.PercussionChannel, (int)GeneralMidiPercussion.AcousticSnare, 110, 0.3, 0.2);
        var onOne = GmProbe.PlayNote(
            pinned, 1, (int)GeneralMidiPercussion.AcousticSnare, 110, 0.3, 0.2);

        //Assert
        GmProbe.LargestDifference(onTen.Left, onOne.Left).Should().Be(0.0);
    }

    [Fact]
    public void percussion_ignores_a_note_off_and_runs_to_its_natural_end()
    {
        //Arrange
        GeneralMidiSynthesizer released = GmProbe.BuildForPercussion();
        GeneralMidiSynthesizer held = GmProbe.BuildForPercussion();

        //Act
        // One of them has the key taken up almost at once; the other is left alone.
        released.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        var (releasedEarly, _) = GmProbe.Render(released, 0.02);
        released.NoteOff(1, (int)GeneralMidiPercussion.OpenHiHat);
        var (releasedLate, _) = GmProbe.Render(released, 0.8);

        held.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        var (heldEarly, _) = GmProbe.Render(held, 0.02);
        var (heldLate, _) = GmProbe.Render(held, 0.8);

        //Assert
        GmProbe.LargestDifference(releasedEarly, heldEarly).Should().Be(0.0);
        GmProbe.LargestDifference(releasedLate, heldLate).Should().Be(0.0);
        GmProbe.Rms(releasedLate).Should().BeGreaterThan(0.001);
    }

    [Fact]
    public void a_closed_hi_hat_cuts_a_sounding_open_one()
    {
        //Arrange
        GeneralMidiSynthesizer cut = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer open = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        cut.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(cut, 0.1);
        cut.NoteOn(1, (int)GeneralMidiPercussion.ClosedHiHat, 110);
        var (afterClosing, _) = GmProbe.Render(cut, 0.5);

        open.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(open, 0.1);
        var (stillOpen, _) = GmProbe.Render(open, 0.5);

        //Assert
        // The open hi-hat rings for well over half a second on its own; a closed one lands on top of
        // it and the ring stops.
        int late = GmProbe.SampleRate / 12;
        int window = GmProbe.SampleRate / 8;

        GmProbe.Rms(stillOpen, late, window).Should().BeGreaterThan(0.002);
        GmProbe.Rms(afterClosing, late, window)
            .Should().BeLessThan(GmProbe.Rms(stillOpen, late, window) * 0.2);
    }

    [Fact]
    public void a_pedal_hi_hat_cuts_a_sounding_open_one_too()
    {
        //Arrange
        GeneralMidiSynthesizer cut = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer open = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        cut.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(cut, 0.1);
        cut.NoteOn(1, (int)GeneralMidiPercussion.PedalHiHat, 110);
        var (afterPedal, _) = GmProbe.Render(cut, 0.5);

        open.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(open, 0.1);
        var (stillOpen, _) = GmProbe.Render(open, 0.5);

        //Assert
        int late = GmProbe.SampleRate / 12;
        int window = GmProbe.SampleRate / 8;

        GmProbe.Rms(afterPedal, late, window)
            .Should().BeLessThan(GmProbe.Rms(stillOpen, late, window) * 0.2);
    }

    [Fact]
    public void a_kit_piece_outside_the_hi_hat_group_cuts_nothing()
    {
        //Arrange
        GeneralMidiSynthesizer both = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer alone = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        both.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(both, 0.1);
        both.NoteOn(1, (int)GeneralMidiPercussion.BassDrum1, 110);
        var (withKick, _) = GmProbe.Render(both, 0.5);

        alone.NoteOn(1, (int)GeneralMidiPercussion.OpenHiHat, 110);
        GmProbe.Render(alone, 0.1);
        var (hatOnly, _) = GmProbe.Render(alone, 0.5);

        //Assert
        int late = GmProbe.SampleRate / 12;
        int window = GmProbe.SampleRate / 8;

        GmProbe.Rms(withKick, late, window)
            .Should().BeGreaterThan(GmProbe.Rms(hatOnly, late, window) * 0.8);
    }

    [Fact]
    public void the_triangles_cut_each_other_and_the_open_one_rings()
    {
        //Arrange
        GeneralMidiSynthesizer cut = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer open = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        cut.NoteOn(1, (int)GeneralMidiPercussion.OpenTriangle, 110);
        GmProbe.Render(cut, 0.1);
        cut.NoteOn(1, (int)GeneralMidiPercussion.MuteTriangle, 110);
        var (afterMuting, _) = GmProbe.Render(cut, 0.8);

        open.NoteOn(1, (int)GeneralMidiPercussion.OpenTriangle, 110);
        GmProbe.Render(open, 0.1);
        var (ringing, _) = GmProbe.Render(open, 0.8);

        //Assert
        int late = GmProbe.SampleRate / 5;
        int window = GmProbe.SampleRate / 4;

        GmProbe.Rms(ringing, late, window).Should().BeGreaterThan(0.002);
        GmProbe.Rms(afterMuting, late, window)
            .Should().BeLessThan(GmProbe.Rms(ringing, late, window) * 0.25);
    }

    [Fact]
    public void the_kit_is_laid_out_across_the_stereo_field()
    {
        //Arrange
        GeneralMidiSynthesizer floorTom = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer hiHat = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer kick = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        var tom = GmProbe.PlayNote(floorTom, 1, (int)GeneralMidiPercussion.LowFloorTom, 110, 0.4, 0.2);
        var hat = GmProbe.PlayNote(hiHat, 1, (int)GeneralMidiPercussion.ClosedHiHat, 110, 0.4, 0.2);
        var drum = GmProbe.PlayNote(kick, 1, (int)GeneralMidiPercussion.BassDrum1, 110, 0.4, 0.2);

        //Assert
        // The low floor tom sits to the left, the hi-hat to the right, and the kick up the middle.
        GmProbe.Rms(tom.Left).Should().BeGreaterThan(GmProbe.Rms(tom.Right) * 1.3);
        GmProbe.Rms(hat.Right).Should().BeGreaterThan(GmProbe.Rms(hat.Left) * 1.3);
        GmProbe.Rms(drum.Left).Should().BeApproximately(GmProbe.Rms(drum.Right), 1e-6);
    }

    [Fact]
    public void the_toms_sweep_from_one_side_to_the_other()
    {
        //Arrange
        int[] toms =
        [
            (int)GeneralMidiPercussion.LowFloorTom,
            (int)GeneralMidiPercussion.HighFloorTom,
            (int)GeneralMidiPercussion.LowTom,
            (int)GeneralMidiPercussion.LowMidTom,
            (int)GeneralMidiPercussion.HiMidTom,
            (int)GeneralMidiPercussion.HighTom,
        ];

        double[] balance = new double[toms.Length];

        //Act
        for (int i = 0; i < toms.Length; i++)
        {
            GeneralMidiSynthesizer synthesizer = GmProbe.BuildForPercussion(
                settings => settings.EnableReverbAndChorus = false);

            var render = GmProbe.PlayNote(synthesizer, 1, toms[i], 110, 0.4, 0.2);
            balance[i] = GmProbe.Rms(render.Right) - GmProbe.Rms(render.Left);
        }

        //Assert
        for (int i = 1; i < balance.Length; i++)
        {
            balance[i].Should().BeGreaterThan(balance[i - 1]);
        }
    }

    [Fact]
    public void a_kit_piece_answers_velocity()
    {
        //Arrange
        GeneralMidiSynthesizer soft = GmProbe.BuildForPercussion();
        GeneralMidiSynthesizer hard = GmProbe.BuildForPercussion();

        //Act
        var quiet = GmProbe.PlayNote(soft, 1, (int)GeneralMidiPercussion.AcousticSnare, 30, 0.3, 0.2);
        var loud = GmProbe.PlayNote(hard, 1, (int)GeneralMidiPercussion.AcousticSnare, 127, 0.3, 0.2);

        //Assert
        GmProbe.Rms(loud).Should().BeGreaterThan(GmProbe.Rms(quiet) * 2.0);
    }

    [Fact]
    public void a_note_outside_the_kit_makes_no_sound_at_all()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForPercussion();

        //Act
        var below = GmProbe.PlayNote(synthesizer, 1, GeneralMidi.LowestPercussionNote - 1, 110, 0.2, 0.1);
        var above = GmProbe.PlayNote(synthesizer, 1, GeneralMidi.HighestPercussionNote + 1, 110, 0.2, 0.1);

        //Assert
        // General MIDI Level 1 defines 35 to 81 and nothing else; a file asking for something
        // outside that gets silence rather than a wrong sound.
        synthesizer.ActiveVoiceCount.Should().Be(0);
        GmProbe.Peak(below).Should().Be(0.0);
        GmProbe.Peak(above).Should().Be(0.0);
    }

    [Fact]
    public void a_channel_can_be_taken_off_the_kit_and_given_a_melodic_program()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        synthesizer.SetProgram(GeneralMidi.PercussionChannel, (int)GeneralMidiProgram.Flute);

        //Assert
        synthesizer.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeFalse();
        synthesizer.GetProgram(GeneralMidi.PercussionChannel).Should().Be((int)GeneralMidiProgram.Flute);
    }

    [Fact]
    public void a_channel_can_be_put_back_onto_the_kit()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        synthesizer.SetPercussionChannel(1, true);
        var onChannelOne = GmProbe.PlayNote(
            synthesizer, 1, (int)GeneralMidiPercussion.Cowbell, 110, 0.3, 0.2);

        GeneralMidiSynthesizer reference = GmProbe.BuildForPercussion();
        var onTheKit = GmProbe.PlayNote(
            reference, 1, (int)GeneralMidiPercussion.Cowbell, 110, 0.3, 0.2);

        //Assert
        synthesizer.IsPercussionChannel(1).Should().BeTrue();
        GmProbe.LargestDifference(onChannelOne.Left, onTheKit.Left).Should().Be(0.0);
    }
}

using System;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The public per-program adjustments: the seven knobs a consumer turns on top of the bank, which
/// stay the same whatever the bank's internal rows are retuned into.
/// </summary>
[Collection(GeneralMidiLibraryCollection.Name)]
public class GeneralMidiAdjustmentsTests
{
    private const int Celesta = (int)GeneralMidiProgram.Celesta;
    private const int Snare = (int)GeneralMidiPercussion.AcousticSnare;

    [Fact]
    public void a_fresh_adjustment_changes_nothing()
    {
        //Arrange
        GeneralMidiSynthesizer adjusted = GmProbe.BuildForProgram(Celesta);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(Celesta);

        //Act
        GeneralMidiAdjustment adjustment = adjusted.Adjustments.Program(Celesta);
        var touched = GmProbe.PlayNote(adjusted, 1, 72, 100, 0.4, 0.3);
        var untouched = GmProbe.PlayNote(plain, 1, 72, 100, 0.4, 0.3);

        //Assert
        adjustment.IsDefault.Should().BeTrue();
        adjusted.Adjustments.HasAny.Should().BeFalse();
        GmProbe.LargestDifference(touched.Left, untouched.Left).Should().Be(0.0);
    }

    [Fact]
    public void level_turns_one_program_down()
    {
        //Arrange
        GeneralMidiSynthesizer quiet = GmProbe.BuildForProgram(Celesta);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(Celesta);

        //Act
        quiet.Adjustments.Program(Celesta).Level = 0.5;

        double quietLevel = GmProbe.Rms(GmProbe.PlayNote(quiet, 1, 72, 100, 0.4, 0.3));
        double plainLevel = GmProbe.Rms(GmProbe.PlayNote(plain, 1, 72, 100, 0.4, 0.3));

        //Assert
        (quietLevel / plainLevel).Should().BeApproximately(0.5, 0.03);
    }

    [Fact]
    public void brightness_makes_a_program_darker()
    {
        //Arrange
        GeneralMidiSynthesizer dark = GmProbe.BuildForProgram(Celesta);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(Celesta);

        //Act
        dark.Adjustments.Program(Celesta).Brightness = -2.5;

        var darker = GmProbe.PlayNote(dark, 1, 72, 100, 0.4, 0.3);
        var normal = GmProbe.PlayNote(plain, 1, 72, 100, 0.4, 0.3);

        //Assert
        GmProbe.Centroid(darker.Left).Should().BeLessThan(GmProbe.Centroid(normal.Left) * 0.8);
    }

    [Fact]
    public void attack_makes_a_program_start_more_slowly()
    {
        //Arrange
        int choir = (int)GeneralMidiProgram.ChoirAahs;

        GeneralMidiSynthesizer slow = GmProbe.BuildForProgram(choir);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(choir);

        //Act
        slow.Adjustments.Program(choir).Attack = 8.0;

        var slowly = GmProbe.PlayNote(slow, 1, 60, 100, 1.0, 0.3);
        var normally = GmProbe.PlayNote(plain, 1, 60, 100, 1.0, 0.3);

        //Assert
        int window = GmProbe.SampleRate / 20;

        GmProbe.Rms(slowly.Left, 0, window)
            .Should().BeLessThan(GmProbe.Rms(normally.Left, 0, window) * 0.6);
    }

    [Fact]
    public void release_makes_a_program_hang_on_longer()
    {
        //Arrange
        int choir = (int)GeneralMidiProgram.ChoirAahs;

        GeneralMidiSynthesizer lingering = GmProbe.BuildForProgram(
            choir, settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(
            choir, settings => settings.EnableReverbAndChorus = false);

        //Act
        lingering.Adjustments.Program(choir).Release = 4.0;

        var longer = GmProbe.PlayNote(lingering, 1, 60, 100, 0.5, 2.0);
        var shorter = GmProbe.PlayNote(plain, 1, 60, 100, 0.5, 2.0);

        //Assert
        int tail = (int)(1.2 * GmProbe.SampleRate);
        int window = GmProbe.SampleRate / 4;

        GmProbe.Rms(longer.Left, tail, window)
            .Should().BeGreaterThan(GmProbe.Rms(shorter.Left, tail, window) * 3.0);
    }

    [Fact]
    public void vibrato_depth_scales_what_the_program_asks_for_and_zero_takes_it_off()
    {
        //Arrange
        int flute = (int)GeneralMidiProgram.Flute;

        GeneralMidiSynthesizer straight = GmProbe.BuildForProgram(flute);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(flute);

        //Act
        straight.Adjustments.Program(flute).VibratoDepth = 0.0;

        var steady = GmProbe.PlayNote(straight, 1, 69, 100, 1.4, 0.1);
        var wobbling = GmProbe.PlayNote(plain, 1, 69, 100, 1.4, 0.1);

        //Assert
        double[] withoutVibrato = new double[4];
        double[] withVibrato = new double[4];

        for (int i = 0; i < 4; i++)
        {
            int offset = (GmProbe.SampleRate * 3 / 4) + (i * Spectrum.BlockLength);
            withoutVibrato[i] = Integration.RenderProbe.Frequency(steady.Left, offset);
            withVibrato[i] = Integration.RenderProbe.Frequency(wobbling.Left, offset);
        }

        Spread(withoutVibrato).Should().BeLessThan(Spread(withVibrato));
        Spread(withoutVibrato).Should().BeLessThan(2.0);
    }

    [Fact]
    public void the_reverb_send_can_be_replaced_outright()
    {
        //Arrange
        int xylophone = (int)GeneralMidiProgram.Xylophone;

        GeneralMidiSynthesizer dry = GmProbe.BuildForProgram(xylophone);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(xylophone);

        //Act
        dry.Adjustments.Program(xylophone).ReverbSend = 0.0;

        var withoutRoom = GmProbe.PlayNote(dry, 1, 72, 110, 0.3, 1.2);
        var withRoom = GmProbe.PlayNote(plain, 1, 72, 110, 0.3, 1.2);

        //Assert
        int tail = withRoom.Left.Length - (GmProbe.SampleRate / 2);
        int window = GmProbe.SampleRate / 2;

        GmProbe.Rms(withoutRoom.Left, tail, window)
            .Should().BeLessThan(GmProbe.Rms(withRoom.Left, tail, window) * 0.2);
    }

    [Fact]
    public void pan_moves_a_program_and_is_an_offset_rather_than_a_position()
    {
        //Arrange
        GeneralMidiSynthesizer moved = GmProbe.BuildForProgram(
            Celesta, settings => settings.EnableReverbAndChorus = false);

        //Act
        moved.Adjustments.Program(Celesta).Pan = -0.8;
        var render = GmProbe.PlayNote(moved, 1, 72, 100, 0.4, 0.3);

        //Assert
        GmProbe.Rms(render.Left).Should().BeGreaterThan(GmProbe.Rms(render.Right) * 2.0);
    }

    [Fact]
    public void an_adjustment_survives_a_program_change_because_it_belongs_to_the_program()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        GeneralMidiSynthesizer reference = GmProbe.Build();

        synthesizer.Adjustments.Program(Celesta).Level = 0.4;

        //Act
        // Away to another program and back again.
        synthesizer.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.Flute, 0);
        GmProbe.PlayNote(synthesizer, 1, 72, 100, 0.2, 0.1);
        synthesizer.ProcessMidiMessage(0, 0xC0, Celesta, 0);

        reference.ProcessMidiMessage(0, 0xC0, Celesta, 0);

        double adjusted = GmProbe.Rms(GmProbe.PlayNote(synthesizer, 1, 72, 100, 0.4, 0.3));
        double plain = GmProbe.Rms(GmProbe.PlayNote(reference, 1, 72, 100, 0.4, 0.3));

        //Assert
        (adjusted / plain).Should().BeApproximately(0.4, 0.03);
    }

    [Fact]
    public void the_whole_kit_and_one_piece_of_it_are_adjusted_together()
    {
        //Arrange
        GeneralMidiSynthesizer both = GmProbe.BuildForPercussion();
        GeneralMidiSynthesizer plain = GmProbe.BuildForPercussion();

        //Act
        both.Adjustments.Percussion.Level = 0.5;
        both.Adjustments.PercussionNote(Snare).Level = 0.5;

        double adjusted = GmProbe.Rms(GmProbe.PlayNote(both, 1, Snare, 110, 0.3, 0.2));
        double reference = GmProbe.Rms(GmProbe.PlayNote(plain, 1, Snare, 110, 0.3, 0.2));

        //Assert
        // The two multiply: half the kit, and half again for the snare.
        (adjusted / reference).Should().BeApproximately(0.25, 0.03);
    }

    [Fact]
    public void moving_the_whole_kit_keeps_its_layout()
    {
        //Arrange
        GeneralMidiSynthesizer moved = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);
        GeneralMidiSynthesizer plain = GmProbe.BuildForPercussion(
            settings => settings.EnableReverbAndChorus = false);

        //Act
        moved.Adjustments.Percussion.Pan = 0.3;

        var shiftedTom = GmProbe.PlayNote(
            moved, 1, (int)GeneralMidiPercussion.LowFloorTom, 110, 0.4, 0.2);
        var plainTom = GmProbe.PlayNote(
            plain, 1, (int)GeneralMidiPercussion.LowFloorTom, 110, 0.4, 0.2);

        //Assert
        // The tom is still left of centre - the kit has not been collapsed into one place - but it
        // has moved right along with everything else.
        Balance(shiftedTom).Should().BeLessThan(0.0);
        Balance(shiftedTom).Should().BeGreaterThan(Balance(plainTom));
    }

    [Fact]
    public void the_library_adjustments_are_copied_into_every_synthesizer_it_creates()
    {
        //Arrange
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        try
        {
            library.Adjustments.Program(Celesta).Level = 0.5;

            //Act
            GeneralMidiSynthesizer created =
                (GeneralMidiSynthesizer)library.CreateSynthesizer(Celesta, GmProbe.SampleRate);
            created.MasterVolume = 1f;

            GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(Celesta);

            double adjusted = GmProbe.Rms(GmProbe.PlayNote(created, 1, 72, 100, 0.4, 0.3));
            double reference = GmProbe.Rms(GmProbe.PlayNote(plain, 1, 72, 100, 0.4, 0.3));

            //Assert
            created.Adjustments.Program(Celesta).Level.Should().Be(0.5);
            (adjusted / reference).Should().BeApproximately(0.5, 0.03);
        }
        finally
        {
            library.Adjustments.Reset();
        }
    }

    [Fact]
    public void the_library_adjustments_are_a_template_rather_than_a_live_link()
    {
        //Arrange
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        //Act
        GeneralMidiSynthesizer created =
            (GeneralMidiSynthesizer)library.CreateSynthesizer(Celesta, GmProbe.SampleRate);

        try
        {
            library.Adjustments.Program(Celesta).Level = 0.25;

            //Assert
            created.Adjustments.Program(Celesta).Level.Should().Be(1.0);
        }
        finally
        {
            library.Adjustments.Reset();
        }
    }

    [Fact]
    public void the_knobs_are_clamped_rather_than_refused()
    {
        //Arrange
        GeneralMidiAdjustment adjustment = new GeneralMidiAdjustment();

        //Act
        adjustment.Level = 100.0;
        adjustment.Brightness = -50.0;
        adjustment.Attack = 0.0;
        adjustment.Release = 1000.0;
        adjustment.VibratoDepth = -3.0;
        adjustment.Pan = 5.0;
        adjustment.ReverbSend = 4.0;

        //Assert
        adjustment.Level.Should().Be(4.0);
        adjustment.Brightness.Should().Be(-4.0);
        adjustment.Attack.Should().Be(0.05);
        adjustment.Release.Should().Be(20.0);
        adjustment.VibratoDepth.Should().Be(0.0);
        adjustment.Pan.Should().Be(1.0);
        adjustment.ReverbSend.Should().Be(1.0);
    }

    [Fact]
    public void a_not_a_number_is_ignored_and_the_knob_keeps_its_value()
    {
        //Arrange
        GeneralMidiAdjustment adjustment = new GeneralMidiAdjustment { Level = 0.7 };

        //Act
        adjustment.Level = double.NaN;

        //Assert
        adjustment.Level.Should().Be(0.7);
    }

    [Fact]
    public void an_adjustment_can_be_reset_copied_and_cloned()
    {
        //Arrange
        GeneralMidiAdjustments adjustments = new GeneralMidiAdjustments();
        adjustments.Program(Celesta).Brightness = -1.0;
        adjustments.Percussion.Level = 0.6;

        //Act
        GeneralMidiAdjustments copy = adjustments.Clone();
        adjustments.Reset();

        //Assert
        copy.Program(Celesta).Brightness.Should().Be(-1.0);
        copy.Percussion.Level.Should().Be(0.6);
        copy.HasAny.Should().BeTrue();

        adjustments.HasAny.Should().BeFalse();
        adjustments.Program(Celesta).Brightness.Should().Be(0.0);
    }

    [Fact]
    public void the_arguments_are_checked()
    {
        //Arrange
        GeneralMidiAdjustments adjustments = new GeneralMidiAdjustments();

        //Act
        Action badProgram = () => adjustments.Program(128);
        Action lowNote = () => adjustments.PercussionNote(GeneralMidi.LowestPercussionNote - 1);
        Action highNote = () => adjustments.PercussionNote(GeneralMidi.HighestPercussionNote + 1);

        //Assert
        badProgram.Should().Throw<ArgumentOutOfRangeException>();
        lowNote.Should().Throw<ArgumentOutOfRangeException>();
        highNote.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static double Balance((float[] Left, float[] Right) render) =>
        GmProbe.Rms(render.Right) - GmProbe.Rms(render.Left);

    private static double Spread(double[] values)
    {
        double lowest = double.MaxValue;
        double highest = double.MinValue;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < lowest) { lowest = values[i]; }
            if (values[i] > highest) { highest = values[i]; }
        }

        return highest - lowest;
    }
}

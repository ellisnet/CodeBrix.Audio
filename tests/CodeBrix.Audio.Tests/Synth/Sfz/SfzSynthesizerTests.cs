using System;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers region selection and articulation: key/velocity/controller gating, key switches, trigger
/// modes with rt_decay, off groups, round robins, random layers, and note polyphony. Samples are
/// constant-value signals so which region sounded is readable straight off the output level.
/// </summary>
public class SfzSynthesizerTests
{
    private const int Rate = 44100;

    [Fact]
    public void a_note_on_renders_audio_and_a_note_off_releases_it()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var sounding = RenderPeak(synthesizer, 4410);
        synthesizer.NoteOff(0, 60);
        RenderPeak(synthesizer, 4410); // let the release finish
        var afterRelease = RenderPeak(synthesizer, 4410);

        //Assert
        sounding.Should().BeGreaterThan(0.2f);
        afterRelease.Should().BeLessThan(0.001f);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void key_and_velocity_ranges_gate_selection()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav lokey=36 hikey=36 lovel=100 hivel=127 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "the key is outside the region");

        synthesizer.NoteOn(0, 36, 64);
        synthesizer.ActiveVoiceCount.Should().Be(0, "the velocity is outside the region");

        synthesizer.NoteOn(0, 36, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void controller_ranges_gate_selection()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav locc64=64 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "the pedal is up");

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1, "the pedal is down");
    }

    [Fact]
    public void sw_last_selects_the_articulation_and_sw_default_covers_the_start()
    {
        //Arrange - two articulations on keyswitches 24 and 25; 24 is the declared default.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("loud.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("quiet.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <global> sw_lokey=24 sw_hikey=25 sw_default=24 loop_mode=loop_continuous
            <region> sample=loud.wav sw_last=24 lokey=36 hikey=96
            <region> sample=quiet.wav sw_last=25 lokey=36 hikey=96
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var beforeSwitch = RenderPeak(synthesizer, 2205);
        synthesizer.NoteOff(0, 60);
        RenderPeak(synthesizer, 4410);

        synthesizer.NoteOn(0, 25, 127); // the keyswitch itself
        synthesizer.NoteOff(0, 25);
        synthesizer.NoteOn(0, 60, 127);
        var afterSwitch = RenderPeak(synthesizer, 2205);

        //Assert
        beforeSwitch.Should().BeGreaterThan(0.2f, "sw_default selects the loud articulation");
        afterSwitch.Should().BeGreaterThan(0.02f);
        afterSwitch.Should().BeLessThan(0.5f * beforeSwitch, "the quiet articulation is selected now");
    }

    [Fact]
    public void sw_down_requires_its_key_held_and_sw_up_requires_it_released()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav sw_down=24 lokey=36 hikey=96 loop_mode=loop_continuous
            <region> sample=dc.wav sw_up=24 lokey=36 hikey=96 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert - with 24 up, only the sw_up region matches.
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1);

        // Hold 24 (no region maps it; it just becomes a held key), and only sw_down matches.
        synthesizer.NoteOn(0, 24, 1);
        synthesizer.NoteOn(0, 62, 127);
        synthesizer.ActiveVoiceCount.Should().Be(2);
    }

    [Fact]
    public void sw_previous_gates_on_the_preceding_note()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav sw_previous=60 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert
        synthesizer.NoteOn(0, 62, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "no previous note yet");

        synthesizer.NoteOff(0, 62);
        synthesizer.NoteOn(0, 64, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "the previous note was 62, not 60");

        synthesizer.NoteOff(0, 64);
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "the previous note was 64, not 60");

        synthesizer.NoteOff(0, 60);
        synthesizer.NoteOn(0, 65, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1, "the previous note is 60 now");
    }

    [Fact]
    public void trigger_first_and_legato_split_on_other_held_notes()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("first.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("legato.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=first.wav trigger=first loop_mode=loop_continuous
            <region> sample=legato.wav trigger=legato loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1, "the first note is trigger=first only");

        synthesizer.NoteOn(0, 64, 127);
        synthesizer.ActiveVoiceCount.Should().Be(2, "the second note, over a held one, is legato only");
    }

    [Fact]
    public void release_regions_fire_on_note_off_and_rt_decay_fades_them_with_hold_time()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("release.wav", 1f, Rate / 2, Rate);
        var instrument = fixture.Load(
            "<region> sample=release.wav trigger=release rt_decay=40 ampeg_release=0.5");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - a short hold, then a long hold of the same note.
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 441); // 10 ms held
        synthesizer.NoteOff(0, 60);
        var shortHold = RenderPeak(synthesizer, 2205);
        RenderPeak(synthesizer, Rate); // silence tail

        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, Rate); // 1 s held
        synthesizer.NoteOff(0, 60);
        var longHold = RenderPeak(synthesizer, 2205);

        //Assert - 40 dB/s for one second is a 100x drop.
        shortHold.Should().BeGreaterThan(0.1f);
        longHold.Should().BeLessThan(0.1f * shortHold);
    }

    [Fact]
    public void one_shot_regions_ignore_the_note_off()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("shot.wav", 1f, Rate, Rate); // a full second of signal
        var instrument = fixture.Load("<region> sample=shot.wav loop_mode=one_shot");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - note off immediately; the sample should keep playing anyway.
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.NoteOff(0, 60);
        RenderPeak(synthesizer, Rate / 2);
        var laterWindow = RenderPeak(synthesizer, 4410);

        //Assert
        laterWindow.Should().BeGreaterThan(0.2f, "a one-shot plays to the end of its sample");
    }

    [Fact]
    public void an_off_group_chokes_the_voices_it_names()
    {
        //Arrange - the open hi-hat (group 1) is choked by the closed one (group 1 too).
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("open.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("closed.wav", 0.1f, Rate / 10, Rate);
        var instrument = fixture.Load("""
            <region> sample=open.wav key=46 group=1 off_by=1 loop_mode=loop_continuous
            <region> sample=closed.wav key=42 group=1 off_by=1
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 46, 127); // open hat rings
        var openRinging = RenderPeak(synthesizer, 4410);
        synthesizer.NoteOn(0, 42, 127); // closed hat chokes it
        RenderPeak(synthesizer, 4410);  // choke fade + closed sample end
        RenderPeak(synthesizer, Rate / 5);
        var afterChoke = RenderPeak(synthesizer, 4410);

        //Assert
        openRinging.Should().BeGreaterThan(0.2f);
        afterChoke.Should().BeLessThan(0.001f, "the open hat was choked and the closed sample has ended");
    }

    [Fact]
    public void layered_regions_sharing_a_group_survive_their_own_note_on()
    {
        //Arrange - two layers of one instrument, both group=1 off_by=1.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, Rate, Rate);
        fixture.WriteConstantWav("b.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=a.wav group=1 off_by=1 loop_mode=loop_continuous
            <region> sample=b.wav group=1 off_by=1 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(2, "layers born of one note-on must not choke each other");
    }

    [Fact]
    public void round_robins_alternate_by_seq_position()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("rr1.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("rr2.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=rr1.wav seq_length=2 seq_position=1 loop_mode=loop_continuous
            <region> sample=rr2.wav seq_length=2 seq_position=2 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        var first = PeakOfOneNote(synthesizer);
        var second = PeakOfOneNote(synthesizer);
        var third = PeakOfOneNote(synthesizer);

        //Assert
        second.Should().BeLessThan(0.5f * first, "the second hit is the quiet robin");
        third.Should().BeApproximately(first, 0.05f, "the cycle wraps");
    }

    [Fact]
    public void random_layers_pick_exactly_one_region_per_note()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("low.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("high.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=low.wav lorand=0 hirand=0.5 loop_mode=loop_continuous
            <region> sample=high.wav lorand=0.5 hirand=1 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert - across many notes, always exactly one layer, and both layers eventually seen.
        var sawLow = false;
        var sawHigh = false;
        for (var i = 0; i < 24; i++)
        {
            synthesizer.NoteOn(0, 60, 127);
            synthesizer.ActiveVoiceCount.Should().Be(1, "adjacent random ranges select exactly one layer");

            var peak = RenderPeak(synthesizer, 441);
            if (peak > 0.25f)
            {
                sawLow = true;
            }
            else if (peak > 0.01f)
            {
                sawHigh = true;
            }

            synthesizer.NoteOff(0, 60);
            RenderPeak(synthesizer, 2205);
            synthesizer.ActiveVoiceCount.Should().Be(0);
        }

        sawLow.Should().BeTrue();
        sawHigh.Should().BeTrue();
    }

    [Fact]
    public void note_polyphony_steals_the_oldest_voice_of_the_note()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav note_polyphony=2 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - three hits of the same note; the first should be choked away.
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 2205);
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 2205);
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 4410); // give the fast choke time to finish

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(2);
    }

    [Fact]
    public void cc_triggered_regions_fire_when_the_controller_enters_range()
    {
        //Arrange - the classic sustain-pedal-down noise.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("pedal.wav", 1f, Rate / 10, Rate);
        var instrument = fixture.Load("<region> sample=pedal.wav on_locc64=126 on_hicc64=127");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act + Assert
        synthesizer.NoteOn(0, 60, 127);
        synthesizer.ActiveVoiceCount.Should().Be(0, "a CC-triggered region ignores notes");

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1, "the pedal entered the trigger range");

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.ActiveVoiceCount.Should().Be(1, "staying in range does not retrigger");
    }

    [Fact]
    public void the_hold_pedal_defers_releases()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - pedal down, note off, and the note keeps sounding until the pedal lifts.
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 2205);
        synthesizer.NoteOff(0, 60);
        var pedalHeld = RenderPeak(synthesizer, 4410);

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 0);
        RenderPeak(synthesizer, 4410);
        var pedalUp = RenderPeak(synthesizer, 4410);

        //Assert
        pedalHeld.Should().BeGreaterThan(0.2f, "the pedal holds the note through its note-off");
        pedalUp.Should().BeLessThan(0.001f);
    }

    private float PeakOfOneNote(SfzSynthesizer synthesizer)
    {
        synthesizer.NoteOn(0, 60, 127);
        var peak = RenderPeak(synthesizer, 441);
        synthesizer.NoteOff(0, 60);
        RenderPeak(synthesizer, 2205);
        return peak;
    }

    private static float RenderPeak(SfzSynthesizer synthesizer, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];
        synthesizer.Render(left, right);

        var peak = 0f;
        for (var i = 0; i < frames; i++)
        {
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
        }

        return peak;
    }
}

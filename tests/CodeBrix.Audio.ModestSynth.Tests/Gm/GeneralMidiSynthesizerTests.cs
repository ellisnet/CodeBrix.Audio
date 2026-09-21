using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The multi-timbral synthesizer: program change, the controllers a General MIDI file actually
/// sends, the shared voice pool, and hosting by everything in CodeBrix.Audio that drives a
/// synthesizer.
/// </summary>
public class GeneralMidiSynthesizerTests
{
    [Fact]
    public void a_program_change_switches_the_channel_to_a_different_sound()
    {
        //Arrange
        GeneralMidiSynthesizer piano = GmProbe.Build();
        GeneralMidiSynthesizer changed = GmProbe.Build();

        //Act
        var asPiano = GmProbe.PlayNote(piano, 1, 60, 100, 0.3, 0.2);

        changed.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.ChurchOrgan, 0);
        var asOrgan = GmProbe.PlayNote(changed, 1, 60, 100, 0.3, 0.2);

        //Assert
        changed.GetProgram(1).Should().Be((int)GeneralMidiProgram.ChurchOrgan);
        GmProbe.LargestDifference(asPiano.Left, asOrgan.Left).Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void a_program_change_arrives_on_the_wire_channel_it_was_sent_on()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        // Wire channel 4 is MIDI channel 5.
        synthesizer.ProcessMidiMessage(4, 0xC0, (int)GeneralMidiProgram.Flute, 0);

        //Assert
        synthesizer.GetProgram(5).Should().Be((int)GeneralMidiProgram.Flute);
        synthesizer.GetProgram(4).Should().Be(0);
    }

    [Fact]
    public void the_percussion_channel_starts_on_the_kit_and_every_other_channel_does_not()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        //Assert
        synthesizer.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeTrue();

        for (int channel = 1; channel <= GeneralMidiSynthesizer.ChannelCount; channel++)
        {
            if (channel == GeneralMidi.PercussionChannel) { continue; }

            synthesizer.IsPercussionChannel(channel).Should().BeFalse();
        }
    }

    [Fact]
    public void a_pinned_synthesizer_ignores_a_program_change()
    {
        //Arrange
        GeneralMidiSynthesizer pinned =
            GmProbe.BuildForProgram((int)GeneralMidiProgram.Celesta);
        GeneralMidiSynthesizer reference =
            GmProbe.BuildForProgram((int)GeneralMidiProgram.Celesta);

        //Act
        pinned.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.DistortionGuitar, 0);
        var afterChange = GmProbe.PlayNote(pinned, 1, 64, 100, 0.3, 0.2);
        var untouched = GmProbe.PlayNote(reference, 1, 64, 100, 0.3, 0.2);

        //Assert
        pinned.IsPinned.Should().BeTrue();
        pinned.PinnedProgram.Should().Be((int)GeneralMidiProgram.Celesta);
        GmProbe.LargestDifference(afterChange.Left, untouched.Left).Should().Be(0.0);
    }

    [Fact]
    public void a_pinned_synthesizer_plays_its_program_on_every_channel()
    {
        //Arrange
        GeneralMidiSynthesizer first = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);
        GeneralMidiSynthesizer second = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);

        //Act
        var onOne = GmProbe.PlayNote(first, 1, 67, 100, 0.3, 0.2);

        // Channel 10 is the percussion channel for a multi-timbral synthesizer; a pinned one has no
        // percussion channel at all.
        var onTen = GmProbe.PlayNote(second, GeneralMidi.PercussionChannel, 67, 100, 0.3, 0.2);

        //Assert
        GmProbe.LargestDifference(onOne.Left, onTen.Left).Should().Be(0.0);
    }

    [Fact]
    public void setting_a_program_on_a_pinned_synthesizer_is_refused()
    {
        //Arrange
        GeneralMidiSynthesizer pinned = GmProbe.BuildForProgram(8);

        //Act
        Action act = () => pinned.SetProgram(1, 9);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void bank_select_is_accepted_and_ignored()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        GeneralMidiSynthesizer reference = GmProbe.Build();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 0, 8);
        synthesizer.ProcessMidiMessage(0, 0xB0, 32, 3);

        var selected = GmProbe.PlayNote(synthesizer, 1, 60, 100, 0.3, 0.2);
        var plain = GmProbe.PlayNote(reference, 1, 60, 100, 0.3, 0.2);

        //Assert
        GmProbe.LargestDifference(selected.Left, plain.Left).Should().Be(0.0);
    }

    [Fact]
    public void channel_volume_turns_a_channel_down()
    {
        //Arrange
        GeneralMidiSynthesizer loud = GmProbe.Build();
        GeneralMidiSynthesizer quiet = GmProbe.Build();

        //Act
        quiet.ProcessMidiMessage(0, 0xB0, 7, 32);

        double loudLevel = GmProbe.Rms(GmProbe.PlayNote(loud, 1, 60, 100, 0.3, 0.2));
        double quietLevel = GmProbe.Rms(GmProbe.PlayNote(quiet, 1, 60, 100, 0.3, 0.2));

        //Assert
        // Volume 32 against the General MIDI default of 100.
        (quietLevel / loudLevel).Should().BeApproximately(32.0 / 100.0, 0.02);
    }

    [Fact]
    public void expression_turns_a_channel_down_on_top_of_its_volume()
    {
        //Arrange
        GeneralMidiSynthesizer plain = GmProbe.Build();
        GeneralMidiSynthesizer expressed = GmProbe.Build();

        //Act
        expressed.ProcessMidiMessage(0, 0xB0, 11, 64);

        double plainLevel = GmProbe.Rms(GmProbe.PlayNote(plain, 1, 60, 100, 0.3, 0.2));
        double expressedLevel = GmProbe.Rms(GmProbe.PlayNote(expressed, 1, 60, 100, 0.3, 0.2));

        //Assert
        (expressedLevel / plainLevel).Should().BeApproximately(64.0 / 127.0, 0.02);
    }

    [Fact]
    public void pan_moves_a_channel_across_the_stereo_field()
    {
        //Arrange
        // The send buses are off for this one: the reverb's own output is stereo and CENTRED - a
        // room does not move when the source does - so it would leave something on the far side and
        // say nothing about the pan.
        GeneralMidiSynthesizer synthesizer =
            GmProbe.Build(settings => settings.EnableReverbAndChorus = false);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 10, 0);
        var (left, right) = GmProbe.PlayNote(synthesizer, 1, 60, 100, 0.3, 0.2);

        //Assert
        GmProbe.Rms(left).Should().BeGreaterThan(0.001);
        GmProbe.Rms(right).Should().BeLessThan(GmProbe.Rms(left) * 0.02);
    }

    [Fact]
    public void a_pitch_bend_moves_the_note_by_two_semitones_by_default()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);

        //Act
        synthesizer.NoteOn(1, 69, 100);
        var (plain, _) = GmProbe.Render(synthesizer, 0.3);

        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 127);
        var (bent, _) = GmProbe.Render(synthesizer, 0.3);

        //Assert
        Integration.RenderProbe.Frequency(plain, 2048).Should().BeApproximately(440.0, 5.0);
        Integration.RenderProbe.Frequency(bent, 2048).Should().BeApproximately(493.88, 6.0);
    }

    [Fact]
    public void the_pitch_bend_range_can_be_widened_with_registered_parameter_zero()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 101, 0);
        synthesizer.ProcessMidiMessage(0, 0xB0, 100, 0);
        synthesizer.ProcessMidiMessage(0, 0xB0, 6, 12);
        synthesizer.ProcessMidiMessage(0, 0xB0, 38, 0);

        synthesizer.NoteOn(1, 57, 100);
        GmProbe.Render(synthesizer, 0.2);
        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 127);
        var (bent, _) = GmProbe.Render(synthesizer, 0.3);

        //Assert
        // A4 at 220 Hz, bent a whole octave up.
        Integration.RenderProbe.Frequency(bent, 2048).Should().BeApproximately(440.0, 10.0);
    }

    [Fact]
    public void reset_all_controllers_puts_the_volume_and_the_bend_back()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        GeneralMidiSynthesizer reference = GmProbe.Build();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 7, 20);
        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 30);
        synthesizer.ProcessMidiMessage(0, 0xB0, 10, 0);
        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 127);
        synthesizer.ProcessMidiMessage(0, 0xB0, 121, 0);

        var afterReset = GmProbe.PlayNote(synthesizer, 1, 60, 100, 0.3, 0.2);
        var untouched = GmProbe.PlayNote(reference, 1, 60, 100, 0.3, 0.2);

        //Assert
        GmProbe.LargestDifference(afterReset.Left, untouched.Left).Should().Be(0.0);
    }

    [Fact]
    public void the_sustain_pedal_holds_a_released_note_until_it_lifts()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram((int)GeneralMidiProgram.ChurchOrgan);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(1, 60, 100);
        GmProbe.Render(synthesizer, 0.1);
        synthesizer.NoteOff(1, 60);
        GmProbe.Render(synthesizer, 0.3);
        int whilePedalDown = synthesizer.ActiveVoiceCount;

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 0);
        GmProbe.Render(synthesizer, 0.4);

        //Assert
        whilePedalDown.Should().Be(1);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void all_sound_off_stops_everything_at_once()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram((int)GeneralMidiProgram.StringEnsemble1);

        //Act
        for (int key = 60; key < 66; key++) { synthesizer.NoteOn(1, key, 100); }

        GmProbe.Render(synthesizer, 0.2);
        int sounding = synthesizer.ActiveVoiceCount;

        synthesizer.ProcessMidiMessage(0, 0xB0, 120, 0);
        GmProbe.Render(synthesizer, 0.2);

        //Assert
        sounding.Should().Be(6);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void the_voice_pool_is_shared_by_every_channel_and_respects_its_limit()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build(settings => settings.MaximumPolyphony = 6);

        //Act
        for (int channel = 1; channel <= 8; channel++)
        {
            synthesizer.NoteOn(channel, 60 + channel, 100);
            GmProbe.Render(synthesizer, 0.01);
        }

        //Assert
        synthesizer.MaximumPolyphony.Should().Be(6);
        synthesizer.ActiveVoiceCount.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void reset_silences_everything_and_puts_the_programs_back()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        synthesizer.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.Xylophone, 0);
        synthesizer.NoteOn(1, 60, 100);
        GmProbe.Render(synthesizer, 0.1);

        //Act
        synthesizer.Reset();

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(0);
        synthesizer.GetProgram(1).Should().Be(0);
        synthesizer.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeTrue();
    }

    [Fact]
    public void the_send_buses_can_be_switched_off()
    {
        //Arrange
        // A xylophone rather than a pad: its own release is a tenth of a second, so a second later
        // anything still sounding came out of the reverb rather than out of the note.
        GeneralMidiSynthesizer wet = GmProbe.BuildForProgram((int)GeneralMidiProgram.Xylophone);
        GeneralMidiSynthesizer dry = GmProbe.BuildForProgram(
            (int)GeneralMidiProgram.Xylophone, settings => settings.EnableReverbAndChorus = false);

        //Act
        var withSends = GmProbe.PlayNote(wet, 1, 72, 110, 0.3, 1.2);
        var withoutSends = GmProbe.PlayNote(dry, 1, 72, 110, 0.3, 1.2);

        //Assert
        wet.ReverbAndChorusEnabled.Should().BeTrue();
        dry.ReverbAndChorusEnabled.Should().BeFalse();

        // The tail is where a reverb shows: long after the note is gone there is still something
        // when the bus runs, and nothing at all when it does not.
        int tailStart = withSends.Left.Length - (GmProbe.SampleRate / 4);

        GmProbe.Rms(withSends.Left, tailStart, GmProbe.SampleRate / 4)
            .Should().BeGreaterThan(GmProbe.Rms(withoutSends.Left, tailStart, GmProbe.SampleRate / 4) * 4.0);
    }

    [Fact]
    public void the_reverb_send_controller_changes_the_tail()
    {
        //Arrange
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram((int)GeneralMidiProgram.AcousticGrandPiano);
        GeneralMidiSynthesizer washed = GmProbe.BuildForProgram((int)GeneralMidiProgram.AcousticGrandPiano);

        //Act
        washed.ProcessMidiMessage(0, 0xB0, 91, 127);

        var quiet = GmProbe.PlayNote(plain, 1, 60, 100, 0.3, 1.2);
        var loud = GmProbe.PlayNote(washed, 1, 60, 100, 0.3, 1.2);

        //Assert
        int tailStart = quiet.Left.Length - (GmProbe.SampleRate / 3);

        GmProbe.Rms(loud.Left, tailStart, GmProbe.SampleRate / 3)
            .Should().BeGreaterThan(GmProbe.Rms(quiet.Left, tailStart, GmProbe.SampleRate / 3) * 1.5);
    }

    [Fact]
    public void the_chorus_send_controller_widens_the_sound()
    {
        //Arrange
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram((int)GeneralMidiProgram.ElectricPiano1);
        GeneralMidiSynthesizer chorused = GmProbe.BuildForProgram((int)GeneralMidiProgram.ElectricPiano1);

        //Act
        chorused.ProcessMidiMessage(0, 0xB0, 93, 127);

        var dry = GmProbe.PlayNote(plain, 1, 60, 100, 0.4, 0.3);
        var wet = GmProbe.PlayNote(chorused, 1, 60, 100, 0.4, 0.3);

        //Assert
        GmProbe.LargestDifference(dry.Left, wet.Left).Should().BeGreaterThan(0.005);
        GmProbe.Rms(wet).Should().BeGreaterThan(GmProbe.Rms(dry));
    }

    [Fact]
    public void an_explicit_reverb_send_survives_a_later_program_change()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        GeneralMidiSynthesizer reference = GmProbe.Build();

        //Act
        // The file says how wet it wants the channel; a program change after that must not undo it.
        synthesizer.ProcessMidiMessage(0, 0xB0, 91, 127);
        synthesizer.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.AcousticBass, 0);

        reference.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.AcousticBass, 0);
        reference.ProcessMidiMessage(0, 0xB0, 91, 127);

        var moved = GmProbe.PlayNote(synthesizer, 1, 48, 100, 0.3, 0.6);
        var straight = GmProbe.PlayNote(reference, 1, 48, 100, 0.3, 0.6);

        //Assert
        GmProbe.LargestDifference(moved.Left, straight.Left).Should().Be(0.0);
    }

    [Fact]
    public void it_plays_through_the_offline_renderer_every_other_synthesizer_uses()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        MidiSequence sequence = TwoPartSequence();

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, sequence, TimeSpan.FromSeconds(0.5));

        //Assert
        rendered.Length.Should().Be(
            (int)Math.Ceiling((sequence.Length.TotalSeconds + 0.5) * GmProbe.SampleRate) * 2);
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.02);
    }

    [Fact]
    public void it_plays_through_the_sequencer_every_other_synthesizer_uses()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        MidiSequencer sequencer = new MidiSequencer(synthesizer);

        //Act
        sequencer.Play(TwoPartSequence(), loop: false);

        float[] left = new float[GmProbe.SampleRate];
        float[] right = new float[GmProbe.SampleRate];
        sequencer.Render(left, right);

        //Assert
        GmProbe.Peak(left).Should().BeGreaterThan(0.02);
    }

    [Fact]
    public void it_plays_under_the_routing_synthesizer()
    {
        //Arrange
        RoutingSynthesizer router = new RoutingSynthesizer(GmProbe.SampleRate);
        router.MasterVolume = 1f;

        router.SetChannel(1, GeneralMidiSynthesizer.CreateForProgram(
            (int)GeneralMidiProgram.Celesta, GmProbe.Settings()));
        router.SetChannel(2, GeneralMidiSynthesizer.CreateForPercussion(GmProbe.Settings()));

        //Act
        // Wire channels 0 and 1 are the router's channels 1 and 2.
        router.ProcessMidiMessage(0, 0x90, 72, 100);
        router.ProcessMidiMessage(1, 0x90, (int)GeneralMidiPercussion.ClosedHiHat, 110);

        float[] left = new float[GmProbe.SampleRate / 2];
        float[] right = new float[left.Length];
        router.Render(left, right);

        //Assert
        router.UnroutedMessageCount.Should().Be(0L);
        GmProbe.Peak(left).Should().BeGreaterThan(0.02);
        router.ActiveVoiceCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void rendering_allocates_nothing_once_the_notes_are_running()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        synthesizer.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.StringEnsemble1, 0);
        synthesizer.NoteOn(1, 60, 100);
        synthesizer.NoteOn(1, 64, 100);
        synthesizer.NoteOn(GeneralMidi.PercussionChannel, 42, 110);

        float[] left = new float[synthesizer.BlockSize];
        float[] right = new float[synthesizer.BlockSize];
        Action work = () => synthesizer.Render(left, right);

        // Warm the oscillator pools and the effect chain before measuring.
        for (int i = 0; i < 8; i++) { work(); }

        //Act
        long bytes = AllocationProbe.LowestBytes(work);

        //Assert
        bytes.Should().Be(0L);
    }

    [Fact]
    public void buffers_of_different_lengths_are_rejected()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        Action act = () => synthesizer.Render(new float[64], new float[32]);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_arguments_are_checked()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        Action noSettings = () => new GeneralMidiSynthesizer((GeneralMidiSynthesizerSettings)null);
        Action badRate = () => new GeneralMidiSynthesizer(12);
        Action badChannel = () => synthesizer.GetProgram(17);
        Action badProgram = () => synthesizer.SetProgram(1, 128);

        //Assert
        noSettings.Should().Throw<ArgumentNullException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badChannel.Should().Throw<ArgumentOutOfRangeException>();
        badProgram.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_settings_are_copied_so_later_changes_have_no_effect()
    {
        //Arrange
        GeneralMidiSynthesizerSettings settings = new GeneralMidiSynthesizerSettings(GmProbe.SampleRate)
        {
            MaximumPolyphony = 8,
        };

        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(settings);

        //Act
        settings.MaximumPolyphony = 64;

        //Assert
        synthesizer.MaximumPolyphony.Should().Be(8);
        synthesizer.SampleRate.Should().Be(GmProbe.SampleRate);
        synthesizer.Channels.Should().Be(16);
    }

    private static MidiSequence TwoPartSequence()
    {
        MidiEventCollection events = new MidiEventCollection(1, 480);

        events.AddEvent(new PatchChangeEvent(0, 1, (int)GeneralMidiProgram.Celesta), 1);
        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 72, 100), 1);
        events.AddEvent(new NoteEvent(480, 1, MidiCommandCode.NoteOff, 72, 0), 1);

        events.AddEvent(
            new NoteEvent(0, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOn, 36, 110),
            GeneralMidi.PercussionChannel);
        events.AddEvent(
            new NoteEvent(240, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOff, 36, 0),
            GeneralMidi.PercussionChannel);
        events.AddEvent(
            new NoteEvent(480, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOn, 42, 100),
            GeneralMidi.PercussionChannel);
        events.AddEvent(
            new NoteEvent(720, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOff, 42, 0),
            GeneralMidi.PercussionChannel);

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }
}

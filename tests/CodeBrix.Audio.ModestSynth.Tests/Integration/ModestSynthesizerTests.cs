using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The standalone synthesizer: a patch played from MIDI events with no Decent Sampler file and no
/// registration.
/// </summary>
public class ModestSynthesizerTests
{
    private const int SampleRate = Spectrum.SampleRate;
    private const int Window = Spectrum.BlockLength;

    [Fact]
    public void a_note_sounds_at_the_key_it_was_struck_at()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());

        //Act
        synthesizer.NoteOn(0, 69, 100);
        var (left, right) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        RenderProbe.Frequency(left, Window).Should().BeApproximately(440.0, 4.0);
        left.Should().Equal(right);
    }

    [Fact]
    public void a_note_off_runs_the_release_and_then_frees_the_voice()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine(), settings =>
        {
            settings.Release = 0.2;
        });

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (held, _) = RenderProbe.RenderSeconds(synthesizer, 0.3);
        int soundingWhileHeld = synthesizer.ActiveVoiceCount;

        synthesizer.NoteOff(0, 60);
        var (released, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        soundingWhileHeld.Should().Be(1);
        RenderProbe.Rms(held, Window, 4096).Should().BeGreaterThan(0.1);
        RenderProbe.Rms(released, 0, 1024).Should().BeGreaterThan(0.1);
        RenderProbe.Rms(released, 15000, 1024).Should().BeApproximately(0.0, 1e-6);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void a_velocity_of_zero_is_a_note_off()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());

        //Act
        synthesizer.NoteOn(0, 60, 100);
        RenderProbe.RenderSeconds(synthesizer, 0.05);
        synthesizer.NoteOn(0, 60, 0);
        RenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void a_full_pitch_bend_moves_the_note_by_the_settings_range()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());

        //Act
        synthesizer.NoteOn(0, 69, 100);
        var (plain, _) = RenderProbe.RenderSeconds(synthesizer, 0.25);
        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 127);
        var (bent, _) = RenderProbe.RenderSeconds(synthesizer, 0.25);

        //Assert
        RenderProbe.Frequency(plain, 2048).Should().BeApproximately(440.0, 4.0);

        // Two semitones up from 440 Hz.
        RenderProbe.Frequency(bent, 2048).Should().BeApproximately(493.88, 5.0);
    }

    [Fact]
    public void the_sustain_pedal_holds_a_released_note_until_it_lifts()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine(), settings =>
        {
            settings.Release = 0.05;
        });

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.NoteOn(0, 60, 100);
        RenderProbe.RenderSeconds(synthesizer, 0.1);
        synthesizer.NoteOff(0, 60);
        RenderProbe.RenderSeconds(synthesizer, 0.3);
        int whilePedalDown = synthesizer.ActiveVoiceCount;

        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 0);
        RenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert
        whilePedalDown.Should().Be(1);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void an_all_notes_off_stops_everything()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());

        //Act
        for (int key = 60; key < 66; key++) { synthesizer.NoteOn(0, key, 100); }

        RenderProbe.RenderSeconds(synthesizer, 0.1);
        int sounding = synthesizer.ActiveVoiceCount;

        synthesizer.NoteOffAll(true);
        RenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert
        sounding.Should().Be(6);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void polyphony_is_capped_and_the_oldest_note_is_the_one_that_goes()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine(), settings =>
        {
            settings.MaximumPolyphony = 4;
        });

        //Act
        for (int key = 60; key < 68; key++)
        {
            synthesizer.NoteOn(0, key, 100);
            RenderProbe.RenderBlocks(synthesizer, 1);
        }

        //Assert
        synthesizer.MaximumPolyphony.Should().Be(4);
        synthesizer.ActiveVoiceCount.Should().Be(4);
    }

    [Fact]
    public void reset_silences_everything_and_forgets_the_bend()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());
        synthesizer.NoteOn(0, 69, 100);
        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 127);
        RenderProbe.RenderSeconds(synthesizer, 0.1);

        //Act
        synthesizer.Reset();
        synthesizer.NoteOn(0, 69, 100);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.25);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(1);
        RenderProbe.Frequency(left, 2048).Should().BeApproximately(440.0, 4.0);
    }

    [Fact]
    public void the_same_events_render_the_same_bytes()
    {
        //Arrange
        ModestSynthesizer first = Build(ModestSynthPresets.PluckedString());
        ModestSynthesizer second = Build(ModestSynthPresets.PluckedString());

        //Act
        first.NoteOn(0, 45, 100);
        second.NoteOn(0, 45, 100);
        var (a, _) = RenderProbe.RenderSeconds(first, 0.3);
        var (b, _) = RenderProbe.RenderSeconds(second, 0.3);

        //Assert
        a.Should().Equal(b);
    }

    [Fact]
    public void two_voices_of_the_same_noisy_patch_are_not_the_same_samples()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = Oscillators.ModestWaveform.Noise };
        ModestSynthesizer alone = Build(patch);
        ModestSynthesizer paired = Build(patch);

        //Act
        alone.NoteOn(0, 60, 100);
        var (single, _) = RenderProbe.RenderSeconds(alone, 0.2);

        paired.NoteOn(0, 60, 100);
        paired.NoteOn(0, 67, 100);
        var (both, _) = RenderProbe.RenderSeconds(paired, 0.2);

        //Assert
        // Two voices seeded alike would sum to one copy at exactly twice the level.
        RenderProbe.Rms(both).Should().BeLessThan(RenderProbe.Rms(single) * 1.9);
        RenderProbe.Rms(both).Should().BeGreaterThan(RenderProbe.Rms(single) * 1.2);
    }

    [Fact]
    public void rendering_allocates_nothing_once_a_note_is_running()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.AdditiveOrgan());
        synthesizer.NoteOn(0, 60, 100);

        float[] left = new float[synthesizer.BlockSize];
        float[] right = new float[synthesizer.BlockSize];
        Action work = () => synthesizer.Render(left, right);
        work();

        //Act
        long bytes = AllocationProbe.LowestBytes(work);

        //Assert
        bytes.Should().Be(0L);
    }

    [Fact]
    public void it_plays_through_the_offline_renderer_every_other_synthesizer_uses()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SawLead());
        MidiSequence sequence = TwoNoteSequence();

        //Act
        float[] rendered = SoundFontRenderer.Render(synthesizer, sequence, TimeSpan.FromSeconds(0.5));

        //Assert
        rendered.Length.Should().Be((int)Math.Ceiling((sequence.Length.TotalSeconds + 0.5) * SampleRate) * 2);
        Peak(rendered).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void it_plays_through_the_sequencer_every_other_synthesizer_uses()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SawLead());
        MidiSequencer sequencer = new MidiSequencer(synthesizer);

        //Act
        sequencer.Play(TwoNoteSequence(), loop: false);

        float[] left = new float[SampleRate];
        float[] right = new float[SampleRate];
        sequencer.Render(left, right);

        //Assert
        RenderProbe.Peak(left).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void the_music_player_accepts_a_synthesizer_of_your_own()
    {
        //Arrange
        using MidiMusicPlayer player = new MidiMusicPlayer();
        MidiSequence sequence = TwoNoteSequence();

        //Act
        Action noSynthesizer = () => player.Load((IMidiSynthesizer)null, sequence);
        Action noFactory = () => player.Load((Func<int, IMidiSynthesizer>)null, sequence);
        Action noSequence = () => player.Load(Build(ModestSynthPresets.SubSine()), null);

        //Assert
        // The device is never opened: every one of these is rejected before the player starts anything.
        noSynthesizer.Should().Throw<ArgumentNullException>();
        noFactory.Should().Throw<ArgumentNullException>();
        noSequence.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void a_null_patch_or_settings_is_rejected()
    {
        //Arrange
        //Act
        Action noPatch = () => new ModestSynthesizer(null, SampleRate);
        Action noSettings = () => new ModestSynthesizer(ModestSynthPresets.SubSine(), null);

        //Assert
        noPatch.Should().Throw<ArgumentNullException>();
        noSettings.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_settings_are_copied_so_later_changes_have_no_effect()
    {
        //Arrange
        ModestSynthesizerSettings settings = new ModestSynthesizerSettings(SampleRate)
        {
            MaximumPolyphony = 8,
        };

        ModestSynthesizer synthesizer = new ModestSynthesizer(ModestSynthPresets.SubSine(), settings);

        //Act
        settings.MaximumPolyphony = 64;

        //Assert
        synthesizer.MaximumPolyphony.Should().Be(8);
        synthesizer.SampleRate.Should().Be(SampleRate);
        synthesizer.ChannelCount.Should().Be(16);
    }

    [Fact]
    public void buffers_of_different_lengths_are_rejected()
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(ModestSynthPresets.SubSine());

        //Act
        Action act = () => synthesizer.Render(new float[64], new float[32]);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    private static ModestSynthesizer Build(
        ModestPatch patch, Action<ModestSynthesizerSettings> configure = null)
    {
        ModestSynthesizerSettings settings = new ModestSynthesizerSettings(SampleRate)
        {
            MasterVolume = 1f,
        };

        configure?.Invoke(settings);

        return new ModestSynthesizer(patch, settings);
    }

    private static MidiSequence TwoNoteSequence()
    {
        MidiEventCollection events = new MidiEventCollection(1, 480);
        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100), 1);
        events.AddEvent(new NoteEvent(480, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        events.AddEvent(new NoteEvent(480, 1, MidiCommandCode.NoteOn, 67, 100), 1);
        events.AddEvent(new NoteEvent(960, 1, MidiCommandCode.NoteOff, 67, 0), 1);
        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    private static double Peak(float[] samples)
    {
        double peak = 0.0;

        foreach (float sample in samples)
        {
            double value = Math.Abs(sample);
            if (value > peak) { peak = value; }
        }

        return peak;
    }
}

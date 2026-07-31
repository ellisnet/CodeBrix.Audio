using System;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers what the voices actually sound like: velocity response, volume/amplitude/pan and their CC
/// modulation, tuning and pitch bend, the filter, envelope stages, offset and end, stereo samples,
/// deterministic rendering, and the offline renderer driving an SFZ instrument through a MIDI sequence.
/// </summary>
public class SfzRenderingTests
{
    private const int Rate = 44100;

    // ------------------------------------------------------------------ gain family

    [Fact]
    public void velocity_shapes_amplitude_with_the_default_concave_curve()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav loop_mode=loop_continuous");

        //Act - a fresh synthesizer per note keeps the measurements independent.
        var full = PeakOfNote(instrument, key: 60, velocity: 127);
        var half = PeakOfNote(instrument, key: 60, velocity: 64);

        //Assert - the default curve is (velocity/127)^2, so velocity 64 is about a quarter.
        half.Should().BeApproximately(full * (64f / 127f) * (64f / 127f), 0.02f);
    }

    [Fact]
    public void amp_veltrack_zero_flattens_the_velocity_response()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav amp_veltrack=0 loop_mode=loop_continuous");

        //Act
        var loud = PeakOfNote(instrument, key: 60, velocity: 127);
        var soft = PeakOfNote(instrument, key: 60, velocity: 10);

        //Assert
        soft.Should().BeApproximately(loud, 0.001f);
    }

    [Fact]
    public void volume_is_decibels_and_volume_oncc_adds_decibels()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 loop_mode=loop_continuous
            <region> sample=dc.wav key=62 volume=-6 loop_mode=loop_continuous
            <region> sample=dc.wav key=64 volume_oncc7=-6 loop_mode=loop_continuous
            """);

        //Act
        var reference = PeakOfNote(instrument, key: 60, velocity: 127);
        var attenuated = PeakOfNote(instrument, key: 62, velocity: 127);
        var ccAttenuated = PeakOfNote(instrument, key: 64, velocity: 127,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 7, 127));

        //Assert - minus six decibels is close to half.
        attenuated.Should().BeApproximately(reference * 0.501f, 0.01f);
        ccAttenuated.Should().BeApproximately(reference * 0.501f, 0.01f);
    }

    [Fact]
    public void amplitude_oncc_is_a_fader_from_silence_to_full()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav amplitude_oncc11=100 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - the controller starts at zero, so the region is silent until it moves. The render
        // sizes are multiples of the 64-frame block so no partial block leaks between measurements.
        synthesizer.NoteOn(0, 60, 127);
        var faderDown = RenderPeak(synthesizer, 448);

        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 127);
        var faderUp = RenderPeakAfterSkip(synthesizer, skipFrames: 448, measureFrames: 448);

        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 64);
        // Skip a settling window first: the anti-pop ramp lets the old gain touch the first block.
        var faderMiddle = RenderPeakAfterSkip(synthesizer, skipFrames: 448, measureFrames: 448);

        //Assert
        faderDown.Should().BeLessThan(0.001f);
        faderUp.Should().BeGreaterThan(0.2f);
        faderMiddle.Should().BeApproximately(faderUp * (64f / 127f), 0.02f);
    }

    [Fact]
    public void set_cc_gives_a_controller_its_initial_value()
    {
        //Arrange - same fader region, but the file initializes CC11 to full.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <control> set_cc11=127
            <region> sample=dc.wav amplitude_oncc11=100 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var peak = RenderPeak(synthesizer, 441);

        //Assert
        peak.Should().BeGreaterThan(0.2f, "set_cc11=127 raised the fader before the first note");
    }

    [Fact]
    public void a_curvecc_reshapes_a_modulation()
    {
        //Arrange - depth 100 through the inverted built-in curve: full when the CC is at zero.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav amplitude_oncc11=100 amplitude_curvecc11=2 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - block-aligned sizes so no partial block leaks between measurements.
        synthesizer.NoteOn(0, 60, 127);
        var ccAtZero = RenderPeak(synthesizer, 448);

        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 127);
        // Skip the anti-pop ramp's settling window before measuring the new level.
        var ccAtFull = RenderPeakAfterSkip(synthesizer, skipFrames: 448, measureFrames: 448);

        //Assert
        ccAtZero.Should().BeGreaterThan(0.2f);
        ccAtFull.Should().BeLessThan(0.001f);
    }

    // ------------------------------------------------------------------ pan

    [Fact]
    public void pan_moves_a_mono_source_between_the_channels()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 pan=-100 loop_mode=loop_continuous
            <region> sample=dc.wav key=62 pan=100 loop_mode=loop_continuous
            <region> sample=dc.wav key=64 pan_oncc10=100 loop_mode=loop_continuous
            """);

        //Act + Assert - hard left, on a fresh synthesizer per note.
        var (left, right) = ChannelsOfNote(instrument, key: 60);
        left.Should().BeGreaterThan(0.2f);
        right.Should().BeLessThan(0.001f);

        // Hard right.
        (left, right) = ChannelsOfNote(instrument, key: 62);
        left.Should().BeLessThan(0.001f);
        right.Should().BeGreaterThan(0.2f);

        // CC10 hard up pans the third region fully right.
        (left, right) = ChannelsOfNote(instrument, key: 64,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 10, 127));
        left.Should().BeLessThan(0.001f);
        right.Should().BeGreaterThan(0.2f);
    }

    [Fact]
    public void stereo_samples_keep_their_channels_and_balance_with_pan()
    {
        //Arrange - left holds +0.5, right holds -0.25, so the channels are unmistakable.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteStereoWav("stereo.wav", leftValue: 0.5f, rightValue: -0.25f, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=stereo.wav key=60 loop_mode=loop_continuous
            <region> sample=stereo.wav key=62 pan=100 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - center: both channels through, signs preserved.
        synthesizer.NoteOn(0, 60, 127);
        var frames = 441;
        var leftBuffer = new float[frames];
        var rightBuffer = new float[frames];
        synthesizer.Render(leftBuffer, rightBuffer);
        synthesizer.NoteOffAll(true);

        //Assert
        leftBuffer[frames - 1].Should().BeGreaterThan(0.1f);
        rightBuffer[frames - 1].Should().BeLessThan(-0.05f);

        // Balanced hard right: the left channel is silenced, the right keeps its content.
        synthesizer.NoteOn(0, 62, 127);
        var left2 = new float[frames];
        var right2 = new float[frames];
        synthesizer.Render(left2, right2);
        MathF.Abs(left2[frames - 1]).Should().BeLessThan(0.001f);
        right2[frames - 1].Should().BeLessThan(-0.05f);
    }

    // ------------------------------------------------------------------ pitch family

    [Fact]
    public void tune_transpose_and_keytrack_move_the_playback_rate()
    {
        //Arrange - a ramp sample makes playback speed visible as the value reached.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var instrument = fixture.Load("""
            <region> sample=ramp.wav key=60 loop_mode=no_loop
            <region> sample=ramp.wav key=62 pitch_keycenter=62 transpose=12 loop_mode=no_loop
            <region> sample=ramp.wav key=64 pitch_keycenter=64 pitch_keytrack=0 loop_mode=no_loop
            """);

        //Assert - an octave up reads the ramp twice as fast.
        ValueReached(instrument, key: 62, frames: 4410)
            .Should().BeApproximately(2f * ValueReached(instrument, key: 60, frames: 4410), 0.01f);

        // keytrack 0: any key plays at the recorded rate.
        ValueReached(instrument, key: 64, frames: 4410)
            .Should().BeApproximately(ValueReached(instrument, key: 60, frames: 4410), 0.01f);
    }

    [Fact]
    public void pitch_bend_follows_bend_up_within_its_range()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var instrument = fixture.Load("<region> sample=ramp.wav bend_up=1200 loop_mode=no_loop");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - full bend up with a 1200-cent range is one octave: double speed.
        var reference = ValueReached(instrument, key: 60, frames: 4410);

        synthesizer.ProcessMidiMessage(0, 0xE0, 0x7F, 0x7F); // bend all the way up
        synthesizer.NoteOn(0, 60, 127);
        var buffer = new float[4410];
        synthesizer.Render(buffer, new float[4410]);
        var bent = Percentile95(buffer);

        //Assert
        bent.Should().BeApproximately(2f * reference, 0.05f * reference + 0.01f);
    }

    // ------------------------------------------------------------------ sample bounds

    [Fact]
    public void offset_skips_into_the_sample_and_end_truncates_it()
    {
        //Arrange - the ramp again: offset should raise the starting value, end should cap it.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", 1000);
        var instrument = fixture.Load("""
            <region> sample=ramp.wav key=60 offset=500 loop_mode=no_loop
            <region> sample=ramp.wav key=62 end=99 loop_mode=no_loop
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - the offset region starts half way up the ramp.
        synthesizer.NoteOn(0, 60, 127);
        var left = new float[16];
        synthesizer.Render(left, new float[16]);

        //Assert
        left[1].Should().BeGreaterThan(0.15f, "offset=500 starts at half the ramp's height");

        // The end region never reads past frame 99, a tenth of the ramp.
        synthesizer.NoteOffAll(true);
        synthesizer.NoteOn(0, 62, 127);
        var capped = new float[2000];
        synthesizer.Render(capped, new float[2000]);
        Percentile95(capped).Should().BeLessThan(0.06f);
    }

    // ------------------------------------------------------------------ filter

    [Fact]
    public void the_low_pass_filter_darkens_and_cutoff_oncc_reopens_it()
    {
        //Arrange - a bright tone well above the cutoff.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("bright.wav", frequency: 8000, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=bright.wav key=60 loop_mode=loop_continuous
            <region> sample=bright.wav key=62 cutoff=500 loop_mode=loop_continuous
            <region> sample=bright.wav key=64 cutoff=500 cutoff_oncc74=9600 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var unfiltered = RenderRms(synthesizer, 4410);
        synthesizer.NoteOffAll(true);

        synthesizer.NoteOn(0, 62, 127);
        var filtered = RenderRms(synthesizer, 4410);
        synthesizer.NoteOffAll(true);

        synthesizer.ProcessMidiMessage(0, 0xB0, 74, 127);
        synthesizer.NoteOn(0, 64, 127);
        var reopened = RenderRms(synthesizer, 4410);

        //Assert - 8 kHz through a 500 Hz low-pass loses well over 20 dB.
        filtered.Should().BeLessThan(0.1f * unfiltered);
        reopened.Should().BeGreaterThan(0.5f * unfiltered, "9600 cents of CC opened the filter well past the tone");
    }

    [Fact]
    public void the_high_pass_filter_thins_a_low_tone()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("low.wav", frequency: 100, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=low.wav key=60 loop_mode=loop_continuous
            <region> sample=low.wav key=62 cutoff=4000 fil_type=hpf_2p loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var unfiltered = RenderRms(synthesizer, 4410);
        synthesizer.NoteOffAll(true);

        synthesizer.NoteOn(0, 62, 127);
        var filtered = RenderRms(synthesizer, 4410);

        //Assert
        filtered.Should().BeLessThan(0.1f * unfiltered);
    }

    // ------------------------------------------------------------------ envelope

    [Fact]
    public void the_attack_stage_ramps_in_and_release_rings_out()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate * 2, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav ampeg_attack=0.2 ampeg_release=0.3 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - 50 ms into a 200 ms attack the level is about a quarter.
        synthesizer.NoteOn(0, 60, 127);
        var early = RenderPeak(synthesizer, 2205);
        var late = RenderPeakAfterSkip(synthesizer, skipFrames: Rate / 2, measureFrames: 2205);

        synthesizer.NoteOff(0, 60);
        RenderPeak(synthesizer, 4410); // 100 ms into the release
        var releasing = RenderPeak(synthesizer, 2205);
        RenderPeak(synthesizer, Rate * 2);
        var silent = RenderPeak(synthesizer, 2205);

        //Assert
        early.Should().BeLessThan(0.5f * late, "the attack is still ramping at 50 ms");
        releasing.Should().BeGreaterThan(0.001f, "a 300 ms release is still audible at 150 ms");
        releasing.Should().BeLessThan(late);
        silent.Should().BeLessThan(0.0001f);
    }

    [Fact]
    public void ampeg_sustain_sets_the_held_level()
    {
        //Arrange - instant decay to a 50 % sustain.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 loop_mode=loop_continuous
            <region> sample=dc.wav key=62 ampeg_decay=0.01 ampeg_sustain=50 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var full = RenderPeakAfterSkip(synthesizer, skipFrames: 4410, measureFrames: 441);
        synthesizer.NoteOffAll(true);

        synthesizer.NoteOn(0, 62, 127);
        var sustained = RenderPeakAfterSkip(synthesizer, skipFrames: 4410, measureFrames: 441);

        //Assert
        sustained.Should().BeApproximately(0.5f * full, 0.02f);
    }

    // ------------------------------------------------------------------ determinism and the renderer

    [Fact]
    public void identical_settings_render_identical_audio_including_random_layers()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 1f, Rate, Rate);
        fixture.WriteConstantWav("b.wav", 0.25f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=a.wav lorand=0 hirand=0.5
            <region> sample=b.wav lorand=0.5 hirand=1
            """);
        var sequence = BuildMotifSequence();

        //Act
        var first = SoundFontRenderer.Render(instrument, sequence, Rate);
        var second = SoundFontRenderer.Render(instrument, sequence, Rate);

        //Assert
        second.Should().Equal(first, "a fixed seed makes SFZ rendering reproducible");
    }

    [Fact]
    public void the_offline_renderer_drives_an_sfz_instrument_through_a_midi_sequence()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 440, frames: Rate);
        var instrument = fixture.Load("<region> sample=tone.wav loop_mode=loop_continuous ampeg_release=0.05");

        //Act
        var samples = SoundFontRenderer.Render(instrument, BuildMotifSequence(), Rate, tail: TimeSpan.FromSeconds(0.5));

        //Assert
        samples.Length.Should().BeGreaterThan(0);
        samples.Max(MathF.Abs).Should().BeGreaterThan(0.05f, "the motif must actually sound");
    }

    [Fact]
    public void the_renderer_writes_a_playable_wav()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 440, frames: Rate);
        var instrument = fixture.Load("<region> sample=tone.wav loop_mode=loop_continuous");
        var path = System.IO.Path.Combine(fixture.Directory, "render.wav");

        //Act
        SoundFontRenderer.RenderToWavFile(instrument, BuildMotifSequence(), path, Rate);

        //Assert
        using var reader = new CodeBrix.Audio.Wave.WaveFileReader(path);
        reader.WaveFormat.Channels.Should().Be(2);
        reader.WaveFormat.SampleRate.Should().Be(Rate);
        reader.Length.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------ helpers

    // One note on a fresh synthesizer: measurements never contaminate each other.
    private static float PeakOfNote(SfzInstrument instrument, int key, int velocity, Action<SfzSynthesizer> setup = null)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        setup?.Invoke(synthesizer);
        synthesizer.NoteOn(0, key, velocity);
        return RenderPeak(synthesizer, 448);
    }

    private static (float Left, float Right) ChannelsOfNote(SfzInstrument instrument, int key, Action<SfzSynthesizer> setup = null)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        setup?.Invoke(synthesizer);
        synthesizer.NoteOn(0, key, 127);
        return RenderChannels(synthesizer, 448);
    }

    private static MidiSequence BuildMotifSequence()
    {
        var events = new MidiEventCollection(1, 120);
        var notes = new[] { 67, 69, 65, 53, 60 }; // the five-tone motif
        for (var i = 0; i < notes.Length; i++)
        {
            events.AddEvent(new NoteOnEvent(i * 120, 1, notes[i], 100, 110), 1);
            events.AddEvent(new NoteEvent(i * 120 + 110, 1, MidiCommandCode.NoteOff, notes[i], 0), 1);
        }
        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    private static void WriteRampWav(SfzTestInstruments fixture, string name, int frames)
    {
        var path = System.IO.Path.Combine(fixture.Directory, name);
        var format = CodeBrix.Audio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);
        using var writer = new CodeBrix.Audio.Wave.WaveFileWriter(path, format);
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = i / (float)frames;
        }
        writer.WriteSamples(samples, 0, samples.Length);
    }

    // Renders one note against a fresh synthesizer and reports the highest ramp value reached - a
    // direct read of how fast the sample was consumed.
    private static float ValueReached(SfzInstrument instrument, int key, int frames)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, key, 127);
        var left = new float[frames];
        synthesizer.Render(left, new float[frames]);
        return Percentile95(left);
    }

    // The 95th percentile of absolute value: peak-like, but immune to the single-sample edges the
    // gain ramp smooths.
    private static float Percentile95(float[] buffer)
    {
        var sorted = buffer.Select(MathF.Abs).OrderBy(v => v).ToArray();
        return sorted[(int)(sorted.Length * 0.95)];
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

    private static float RenderPeakAfterSkip(SfzSynthesizer synthesizer, int skipFrames, int measureFrames)
    {
        RenderPeak(synthesizer, skipFrames);
        return RenderPeak(synthesizer, measureFrames);
    }

    private static float RenderRms(SfzSynthesizer synthesizer, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];
        synthesizer.Render(left, right);

        var sum = 0.0;
        for (var i = 0; i < frames; i++)
        {
            sum += (double)left[i] * left[i];
        }

        return (float)Math.Sqrt(sum / frames);
    }

    private static (float Left, float Right) RenderChannels(SfzSynthesizer synthesizer, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];
        synthesizer.Render(left, right);

        var peakLeft = 0f;
        var peakRight = 0f;
        for (var i = 0; i < frames; i++)
        {
            peakLeft = MathF.Max(peakLeft, MathF.Abs(left[i]));
            peakRight = MathF.Max(peakRight, MathF.Abs(right[i]));
        }

        return (peakLeft, peakRight);
    }
}

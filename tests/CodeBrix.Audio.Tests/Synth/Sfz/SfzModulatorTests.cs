using System;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers the modulators: envelope shapes, velocity-tracked stage times, dynamic retiming, the EQ
/// bands, the second filter, the filter and pitch envelopes, the v1 and v2 LFO families, flexible
/// envelopes (including the key-delta portamento idiom), variators, amplitude smoothing, and the
/// determinism of everything random.
/// </summary>
public class SfzModulatorTests
{
    private const int Rate = 44100;

    // ------------------------------------------------------------------ amplifier envelope extras

    [Fact]
    public void ampeg_attack_shape_positive_rises_late()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var linear = fixture.Load("<region> sample=dc.wav ampeg_attack=0.2 loop_mode=loop_continuous");
        var shaped = fixture.Load("<region> sample=dc.wav ampeg_attack=0.2 ampeg_attack_shape=8 loop_mode=loop_continuous");

        //Act - the first 50 ms of a 200 ms attack.
        var linearLevel = RmsOfNote(linear, skipFrames: 0, measureFrames: 2240);
        var shapedLevel = RmsOfNote(shaped, skipFrames: 0, measureFrames: 2240);

        //Assert - shape 8 keeps the early attack almost silent.
        shapedLevel.Should().BeLessThan(linearLevel * 0.1f);
    }

    [Fact]
    public void ampeg_decay_shape_zero_makes_the_decay_linear()
    {
        //Arrange - decay to a zero sustain; the halfway level tells the curve apart: linear is still
        // at half, the default exponential is nearly done.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var exponential = fixture.Load("<region> sample=dc.wav ampeg_decay=0.2 ampeg_sustain=0 loop_mode=loop_continuous");
        var linear = fixture.Load("<region> sample=dc.wav ampeg_decay=0.2 ampeg_sustain=0 ampeg_decay_shape=0 loop_mode=loop_continuous");

        //Act - the window around 100 ms into the decay.
        var exponentialLevel = RmsOfNote(exponential, skipFrames: 4480, measureFrames: 1088);
        var linearLevel = RmsOfNote(linear, skipFrames: 4480, measureFrames: 1088);

        //Assert
        linearLevel.Should().BeGreaterThan(exponentialLevel * 3f);
    }

    [Fact]
    public void ampeg_vel2attack_shortens_the_attack_with_velocity()
    {
        //Arrange - attack 0.35 s, minus 0.3 s at full velocity; amp_veltrack=0 isolates the timing.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav amp_veltrack=0 ampeg_attack=0.35 ampeg_vel2attack=-0.3 loop_mode=loop_continuous");

        //Act - the first 100 ms.
        var soft = RmsOfNote(instrument, skipFrames: 0, measureFrames: 4480, velocity: 1);
        var hard = RmsOfNote(instrument, skipFrames: 0, measureFrames: 4480, velocity: 127);

        //Assert - at full velocity the attack is 0.05 s, so the window is mostly at full level.
        hard.Should().BeGreaterThan(soft * 3f);
    }

    [Fact]
    public void ampeg_dynamic_follows_a_sustain_modulation_mid_note()
    {
        //Arrange - CC1 pulls the sustain from 100 down to 0; only ampeg_dynamic=1 listens mid-note.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var latched = fixture.Load(
            "<region> sample=dc.wav ampeg_decay=0.01 ampeg_sustain_oncc1=-100 loop_mode=loop_continuous");
        var dynamic = fixture.Load(
            "<region> sample=dc.wav ampeg_decay=0.01 ampeg_sustain_oncc1=-100 ampeg_dynamic=1 loop_mode=loop_continuous");

        //Act
        var latchedLevel = LevelAfterMidNoteCc(latched);
        var dynamicLevel = LevelAfterMidNoteCc(dynamic);

        //Assert - the latched region ignores the controller; the dynamic one fades out.
        latchedLevel.Should().BeGreaterThan(0.2f);
        dynamicLevel.Should().BeLessThan(0.01f);
    }

    // ------------------------------------------------------------------ EQ and the second filter

    [Fact]
    public void an_eq_band_cuts_its_center_frequency()
    {
        //Arrange - a 1 kHz tone against an 18 dB cut centered on it; key= keeps the pitch unshifted.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 1000, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=tone.wav key=60 eq1_freq=1000 eq1_bw=2 eq1_gain=-18 loop_mode=loop_continuous
            <region> sample=tone.wav key=62 loop_mode=loop_continuous
            """);

        //Act
        var cut = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, key: 60);
        var flat = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, key: 62);

        //Assert - minus 18 dB is a factor of 0.126.
        cut.Should().BeLessThan(flat * 0.3f);
    }

    [Fact]
    public void eq_gain_oncc_engages_the_band_when_the_controller_moves()
    {
        //Arrange - the NakedDrums idiom: the band exists only through its CC-modulated gain.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 1000, frames: Rate);
        var instrument = fixture.Load(
            "<region> sample=tone.wav key=60 eq1_freq=1000 eq1_bw=2 eq1_gain_oncc77=-18 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var flat = RenderRms(synthesizer, 2240);
        synthesizer.ProcessMidiMessage(0, 0xB0, 77, 127);
        RenderRms(synthesizer, 2240); // settle the anti-pop ramp and the band switch-on
        var cut = RenderRms(synthesizer, 2240);

        //Assert
        cut.Should().BeLessThan(flat * 0.3f);
    }

    [Fact]
    public void the_second_filter_runs_in_series()
    {
        //Arrange - a 2 kHz tone against a 200 Hz one-pole low-pass as the SECOND filter only.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 2000, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=tone.wav key=60 fil2_type=lpf_2p cutoff2=200 loop_mode=loop_continuous
            <region> sample=tone.wav key=62 loop_mode=loop_continuous
            """);

        //Act
        var filtered = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, key: 60);
        var open = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, key: 62);

        //Assert
        filtered.Should().BeLessThan(open * 0.2f);
    }

    // ------------------------------------------------------------------ modulation envelopes

    [Fact]
    public void fileg_opens_the_filter_over_its_attack()
    {
        //Arrange - the cutoff starts at 150 Hz (a 1.5 kHz tone is buried) and the envelope adds
        // 4800 cents (x16, to 2400 Hz) over a 100 ms attack.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 1500, frames: Rate);
        var instrument = fixture.Load(
            "<region> sample=tone.wav key=60 cutoff=150 fileg_attack=0.1 fileg_depth=4800 loop_mode=loop_continuous");

        //Act
        var closed = RmsOfNote(instrument, skipFrames: 0, measureFrames: 1088);
        var open = RmsOfNote(instrument, skipFrames: 6720, measureFrames: 2240);

        //Assert
        open.Should().BeGreaterThan(closed * 3f);
    }

    [Fact]
    public void pitcheg_shifts_the_pitch_by_its_depth()
    {
        //Arrange - +1200 cents held from the start doubles ramp consumption.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var straight = fixture.Load("<region> sample=ramp.wav");
        var shifted = fixture.Load("<region> sample=ramp.wav pitcheg_attack=0 pitcheg_depth=1200");

        //Act
        var straightReach = PeakOfNote(straight, frames: 4416);
        var shiftedReach = PeakOfNote(shifted, frames: 4416);

        //Assert
        (shiftedReach / straightReach).Should().BeApproximately(2f, 0.2f);
    }

    // ------------------------------------------------------------------ LFOs

    [Fact]
    public void a_v1_amplfo_makes_tremolo()
    {
        //Arrange - a 4 Hz sine LFO swinging the volume by 24 dB, through the v1 block spelling.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav amplfo_freq=4 amplfo_depth=-24 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - windows around the LFO's positive peak (62 ms, -24 dB) and negative peak (187 ms, +24 dB).
        synthesizer.NoteOn(0, 60, 127);
        var quiet = RenderRmsAfterSkip(synthesizer, skipFrames: 2240, measureFrames: 1088);
        var loud = RenderRmsAfterSkip(synthesizer, skipFrames: 4480, measureFrames: 1088);

        //Assert
        loud.Should().BeGreaterThan(quiet * 10f);
    }

    [Fact]
    public void a_v2_lfo_targets_volume_the_same_way()
    {
        //Arrange - the same tremolo written as an SFZ v2 LFO block.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav lfo01_wave=1 lfo01_freq=4 lfo01_volume=-24 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var quiet = RenderRmsAfterSkip(synthesizer, skipFrames: 2240, measureFrames: 1088);
        var loud = RenderRmsAfterSkip(synthesizer, skipFrames: 4480, measureFrames: 1088);

        //Assert
        loud.Should().BeGreaterThan(quiet * 10f);
    }

    [Fact]
    public void lfo_delay_holds_the_modulation_back()
    {
        //Arrange - the tremolo only begins after its half-second delay.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav lfo01_wave=1 lfo01_freq=4 lfo01_delay=0.5 lfo01_volume=-24 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - the same two windows as the tremolo test fall inside the delay and stay steady.
        synthesizer.NoteOn(0, 60, 127);
        var early = RenderRmsAfterSkip(synthesizer, skipFrames: 2240, measureFrames: 1088);
        var late = RenderRmsAfterSkip(synthesizer, skipFrames: 4480, measureFrames: 1088);

        //Assert
        late.Should().BeApproximately(early, early * 0.05f);
    }

    // ------------------------------------------------------------------ flexible envelopes

    [Fact]
    public void a_flex_eg_on_key_delta_glides_in_from_the_previous_note()
    {
        //Arrange - the corpus portamento idiom: the envelope starts a key-delta below the note and
        // glides to pitch over 120 ms, so early ramp consumption is slower than an unglided note.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var glide = fixture.Load("""
            <region> sample=ramp.wav lokey=0 hikey=127 pitch_keycenter=60
            eg06_level0=-1 eg06_time0=0 eg06_level1=0 eg06_time1=0.12 eg06_sustain=1 eg06_pitch_oncc140=100
            """);
        var straight = fixture.Load("<region> sample=ramp.wav lokey=0 hikey=127 pitch_keycenter=60");

        //Act
        var glideReach = ReachAfterInterval(glide);
        var straightReach = ReachAfterInterval(straight);

        //Assert - gliding up from 12 half-steps below consumes the ramp measurably slower at first.
        glideReach.Should().BeLessThan(straightReach * 0.8f);
    }

    // ------------------------------------------------------------------ variators

    [Fact]
    public void a_variator_scales_the_cutoff_from_controller_and_velocity()
    {
        //Arrange - the weresax idiom: cutoff opens by up to 4800 cents, scaled by CC1 times the
        // note-on velocity (extended source 131), multiplied.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteSineWav("tone.wav", frequency: 2000, frames: Rate);
        var instrument = fixture.Load("""
            <region> sample=tone.wav key=60 cutoff=200
            var01_cutoff=4800 var01_mod=mult var01_oncc1=1 var01_oncc131=1 loop_mode=loop_continuous
            """);

        //Act
        var closed = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, setCc: (1, 0));
        var open = RmsOfNote(instrument, skipFrames: 2240, measureFrames: 2240, setCc: (1, 127));

        //Assert - at full controller and velocity the cutoff is 3200 Hz and the tone passes.
        open.Should().BeGreaterThan(closed * 3f);
    }

    // ------------------------------------------------------------------ smoothing

    [Fact]
    public void amplitude_smoothcc_glides_instead_of_jumping()
    {
        //Arrange - a fader with and without a 500 ms smoothing time.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var stepped = fixture.Load(
            "<region> sample=dc.wav amplitude_oncc4=100 loop_mode=loop_continuous");
        var smoothed = fixture.Load(
            "<region> sample=dc.wav amplitude_oncc4=100 amplitude_smoothcc4=500 loop_mode=loop_continuous");

        //Act
        var steppedLevel = LevelShortlyAfterFaderUp(stepped);
        var smoothedLevel = LevelShortlyAfterFaderUp(smoothed);

        //Assert - 25 ms into a 500 ms glide the smoothed fader has barely moved.
        steppedLevel.Should().BeGreaterThan(0.2f);
        smoothedLevel.Should().BeLessThan(steppedLevel * 0.3f);
    }

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void renders_with_randoms_and_sample_and_hold_are_deterministic()
    {
        //Arrange - every random mechanism at once: level, offset and delay randoms plus an S&H LFO.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav amp_random=6 offset_random=1000 delay_random=0.01
            lfo03_wave=-1 lfo03_freq=8 lfo03_volume=6 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act
        var first = RenderNote(synthesizer);
        synthesizer.Reset();
        var second = RenderNote(synthesizer);

        //Assert
        second.Should().Equal(first);
    }

    // ------------------------------------------------------------------ helpers

    private static float RmsOfNote(
        SfzInstrument instrument, int skipFrames, int measureFrames,
        int key = 60, int velocity = 127, (int Cc, int Value)? setCc = null)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        if (setCc.HasValue)
        {
            synthesizer.ProcessMidiMessage(0, 0xB0, setCc.Value.Cc, setCc.Value.Value);
        }

        synthesizer.NoteOn(0, key, velocity);
        if (skipFrames > 0)
        {
            RenderRms(synthesizer, skipFrames);
        }

        return RenderRms(synthesizer, measureFrames);
    }

    private static float LevelAfterMidNoteCc(SfzInstrument instrument)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 60, 127);
        RenderRms(synthesizer, 2240);
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, 127);
        RenderRms(synthesizer, 4480);
        return RenderRms(synthesizer, 2240);
    }

    private static float LevelShortlyAfterFaderUp(SfzInstrument instrument)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 60, 127);
        RenderRms(synthesizer, 448);
        synthesizer.ProcessMidiMessage(0, 0xB0, 4, 127);
        RenderRms(synthesizer, 448); // the anti-pop ramp's settling window
        return RenderRms(synthesizer, 640);
    }

    // Plays a low note, then the measured note a full octave up, and reports how far into the ramp
    // the second note got - the portamento glide shows up as slower early consumption.
    private static float ReachAfterInterval(SfzInstrument instrument)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 48, 127);
        RenderRms(synthesizer, 448);
        synthesizer.NoteOffAll(true);

        synthesizer.NoteOn(0, 60, 127);
        var left = new float[4416];
        synthesizer.Render(left, new float[4416]);

        var peak = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(left[i]));
        }

        return peak;
    }

    private static float PeakOfNote(SfzInstrument instrument, int frames)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 60, 127);
        var left = new float[frames];
        synthesizer.Render(left, new float[frames]);

        var peak = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(left[i]));
        }

        return peak;
    }

    private static float[] RenderNote(SfzSynthesizer synthesizer)
    {
        synthesizer.NoteOn(0, 60, 127);
        var left = new float[8960];
        synthesizer.Render(left, new float[8960]);
        return left;
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

    private static float RenderRmsAfterSkip(SfzSynthesizer synthesizer, int skipFrames, int measureFrames)
    {
        RenderRms(synthesizer, skipFrames);
        return RenderRms(synthesizer, measureFrames);
    }
}

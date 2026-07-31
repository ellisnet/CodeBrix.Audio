using System;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers the articulation extras: region delay, offset modulation, per-voice randoms, key tracking of
/// amplitude, scope-level volumes, the gain_ccN alias, stereo width, program selection, the keyswitch
/// range and previous-velocity switches, the remapped sustain pedal, group polyphony, timed chokes,
/// and the key/velocity/controller crossfades.
/// </summary>
public class SfzArticulationExtrasTests
{
    private const int Rate = 44100;

    // ------------------------------------------------------------------ timing and randoms

    [Fact]
    public void delay_holds_the_sample_back()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav delay=0.05 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - 0.05 s is 2205 frames; the delay gate works in whole 64-frame blocks, so the first
        // 2112 frames are guaranteed silent and sound starts by 2240.
        synthesizer.NoteOn(0, 60, 127);
        var duringDelay = RenderPeak(synthesizer, 2112);
        var afterDelay = RenderPeak(synthesizer, 448);

        //Assert
        duringDelay.Should().BeLessThan(0.0001f);
        afterDelay.Should().BeGreaterThan(0.2f);
    }

    [Fact]
    public void offset_oncc_moves_the_sample_start()
    {
        //Arrange - a rising ramp makes the start position directly readable from the output level.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var instrument = fixture.Load("<region> sample=ramp.wav offset_oncc1=22050");

        //Act
        var fromStart = FirstWindowLevel(instrument, setCc1: 0);
        var fromMiddle = FirstWindowLevel(instrument, setCc1: 127);

        //Assert - the ramp reaches 0.5 at its middle, so starting there is immediately at half of
        // full scale (about 0.18 after the 0.5 master volume and the equal-power center pan).
        fromStart.Should().BeLessThan(0.02f);
        fromMiddle.Should().BeGreaterThan(0.15f);
    }

    [Fact]
    public void amp_random_varies_the_level_between_voices()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav amp_random=6 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - two voices from one synthesizer draw different randoms.
        synthesizer.NoteOn(0, 60, 127);
        var first = RenderPeak(synthesizer, 448);
        synthesizer.NoteOffAll(true);
        synthesizer.NoteOn(0, 60, 127);
        var second = RenderPeak(synthesizer, 448);

        //Assert
        MathF.Abs(first - second).Should().BeGreaterThan(0.001f);
    }

    [Fact]
    public void pitch_veltrack_raises_pitch_with_velocity()
    {
        //Arrange - playback speed read off a ramp: +1200 cents at full velocity doubles consumption.
        using var fixture = SfzTestInstruments.Create();
        WriteRampWav(fixture, "ramp.wav", Rate);
        var instrument = fixture.Load("<region> sample=ramp.wav pitch_veltrack=1200 amp_veltrack=0");

        //Act
        var slow = ValueReached(instrument, velocity: 1, frames: 4416);
        var fast = ValueReached(instrument, velocity: 127, frames: 4416);

        //Assert
        (fast / slow).Should().BeApproximately(2f, 0.2f);
    }

    // ------------------------------------------------------------------ gain family

    [Fact]
    public void amp_keytrack_attenuates_away_from_its_keycenter()
    {
        //Arrange - pitch_keytrack=0 keeps the DC sample identical on both keys; only the gain moves.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav lokey=0 hikey=127 pitch_keytrack=0 amp_keytrack=-6 amp_keycenter=60 loop_mode=loop_continuous");

        //Act
        var atCenter = PeakOfNote(instrument, key: 60);
        var twoKeysUp = PeakOfNote(instrument, key: 62);

        //Assert - two keys at -6 dB each is -12 dB, close to a quarter.
        twoKeysUp.Should().BeApproximately(atCenter * 0.251f, 0.01f);
    }

    [Fact]
    public void scope_volumes_stack_with_the_region_volume()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 loop_mode=loop_continuous
            <region> sample=dc.wav key=62 volume=-3 group_volume=-3 master_volume=-3 global_volume=-3 loop_mode=loop_continuous
            """);

        //Act
        var reference = PeakOfNote(instrument, key: 60);
        var stacked = PeakOfNote(instrument, key: 62);

        //Assert - four stages of -3 dB sum to -12 dB.
        stacked.Should().BeApproximately(reference * 0.251f, 0.01f);
    }

    [Fact]
    public void gain_cc_is_the_v1_spelling_of_a_volume_modulation()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("<region> sample=dc.wav gain_cc7=-6 loop_mode=loop_continuous");

        //Act
        var ccAtZero = PeakOfNote(instrument, key: 60);
        var ccAtFull = PeakOfNote(instrument, key: 60,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 7, 127));

        //Assert
        ccAtFull.Should().BeApproximately(ccAtZero * 0.501f, 0.01f);
    }

    [Fact]
    public void width_zero_collapses_a_stereo_sample_to_mono()
    {
        //Arrange - a hard-left stereo sample: left carries 1, right carries 0.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteStereoWav("stereo.wav", 1f, 0f, Rate);
        var wide = fixture.Load("<region> sample=stereo.wav loop_mode=loop_continuous");
        var mono = fixture.Load("<region> sample=stereo.wav width=0 loop_mode=loop_continuous");

        //Act
        var wideChannels = ChannelsOfNote(wide);
        var monoChannels = ChannelsOfNote(mono);

        //Assert - untouched, the right channel is silent; at width zero both carry the mid signal.
        wideChannels.Right.Should().BeLessThan(0.001f);
        monoChannels.Right.Should().BeApproximately(monoChannels.Left, 0.001f);
        monoChannels.Left.Should().BeApproximately(wideChannels.Left / 2f, 0.01f);
    }

    // ------------------------------------------------------------------ selection extras

    [Fact]
    public void loprog_hiprog_select_regions_by_program()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 loprog=0 hiprog=0 loop_mode=loop_continuous
            <region> sample=dc.wav key=60 loprog=1 hiprog=1 volume=-12 loop_mode=loop_continuous
            """);

        //Act
        var programZero = PeakOfNote(instrument, key: 60);
        var programOne = PeakOfNote(instrument, key: 60,
            setup: s => s.ProcessMidiMessage(0, 0xC0, 1, 0));

        //Assert - program 1 selects only the attenuated region.
        programOne.Should().BeApproximately(programZero * 0.251f, 0.01f);
    }

    [Fact]
    public void sw_lolast_and_sw_hilast_select_by_keyswitch_range()
    {
        //Arrange - two articulations on keyswitch ranges 24-25 and 26-27.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 sw_lokey=24 sw_hikey=27 sw_lolast=24 sw_hilast=25 sw_default=24 loop_mode=loop_continuous
            <region> sample=dc.wav key=60 sw_lokey=24 sw_hikey=27 sw_lolast=26 sw_hilast=27 volume=-12 loop_mode=loop_continuous
            """);

        //Act
        var defaulted = PeakOfNote(instrument, key: 60);
        var switched = PeakOfNote(instrument, key: 60, setup: s => s.NoteOn(0, 27, 100));

        //Assert
        switched.Should().BeApproximately(defaulted * 0.251f, 0.01f);
    }

    [Fact]
    public void sw_vel_previous_tests_the_previous_notes_velocity()
    {
        //Arrange - the region needs velocity 100+, measured on the PREVIOUS note.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav key=60 lovel=100 sw_vel=previous loop_mode=loop_continuous");

        //Act - a soft note with no history stays silent; after a hard note elsewhere it sounds.
        var withoutHistory = PeakOfNote(instrument, key: 60, velocity: 30);
        var withHistory = PeakOfNote(instrument, key: 60, velocity: 30,
            setup: s => s.NoteOn(0, 72, 120));

        //Assert
        withoutHistory.Should().BeLessThan(0.001f);
        withHistory.Should().BeGreaterThan(0.01f);
    }

    [Fact]
    public void sustain_cc_remaps_the_hold_pedal()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav sustain_cc=90 ampeg_release=0.001 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - CC90 held down defers the release; CC64, the usual pedal, must not.
        synthesizer.ProcessMidiMessage(0, 0xB0, 90, 127);
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 448);
        synthesizer.NoteOff(0, 60);
        var heldByPedal = RenderPeakAfterSkip(synthesizer, skipFrames: 448, measureFrames: 448);

        synthesizer.ProcessMidiMessage(0, 0xB0, 90, 0);
        var released = RenderPeakAfterSkip(synthesizer, skipFrames: 2240, measureFrames: 448);

        //Assert
        heldByPedal.Should().BeGreaterThan(0.2f);
        released.Should().BeLessThan(0.001f);
    }

    // ------------------------------------------------------------------ polyphony and chokes

    [Fact]
    public void polyphony_caps_the_groups_simultaneous_voices()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav lokey=0 hikey=127 group=1 polyphony=1 loop_mode=loop_continuous");
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - the second note steals the first: after the 5 ms choke fade only one voice remains.
        synthesizer.NoteOn(0, 60, 127);
        RenderPeak(synthesizer, 448);
        synthesizer.NoteOn(0, 62, 127);
        RenderPeak(synthesizer, 2240);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void off_mode_time_fades_the_choked_voice_over_off_time()
    {
        //Arrange - key 62 (barely audible itself) chokes key 60 with a 0.2 s fade.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav key=60 off_by=1 off_mode=time off_time=0.2 loop_mode=loop_continuous
            <region> sample=dc.wav key=62 group=1 volume=-80 loop_mode=loop_continuous
            """);
        var synthesizer = new SfzSynthesizer(instrument, Rate);

        //Act - RMS windows, so a 5 ms fast choke (which empties the window) cannot pass.
        synthesizer.NoteOn(0, 60, 127);
        var beforeChoke = RenderRms(synthesizer, 4480);
        synthesizer.NoteOn(0, 62, 127);
        var earlyInFade = RenderRms(synthesizer, 2240);
        var afterFade = RenderRmsAfterSkip(synthesizer, skipFrames: 8960, measureFrames: 2240);

        //Assert - the timed fade has barely moved 50 ms into its 200 ms run, and is gone by 0.3 s.
        earlyInFade.Should().BeGreaterThan(beforeChoke * 0.7f);
        afterFade.Should().BeLessThan(0.01f);
    }

    // ------------------------------------------------------------------ crossfades

    [Fact]
    public void velocity_crossfade_scales_gain_linearly_with_the_gain_curve()
    {
        //Arrange - amp_veltrack=0 removes the ordinary velocity response, leaving only the crossfade.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load(
            "<region> sample=dc.wav amp_veltrack=0 xfin_lovel=0 xfin_hivel=127 xf_velcurve=gain loop_mode=loop_continuous");

        //Act
        var full = PeakOfNote(instrument, key: 60, velocity: 127);
        var half = PeakOfNote(instrument, key: 60, velocity: 64);

        //Assert
        half.Should().BeApproximately(full * (64f / 127f), 0.02f);
    }

    [Fact]
    public void controller_crossfade_pair_sums_to_a_constant_with_the_gain_curve()
    {
        //Arrange - the classic dynamics crossfade: CC1 fades one layer out while fading the other in.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var instrument = fixture.Load("""
            <region> sample=dc.wav amp_veltrack=0 xfout_locc1=0 xfout_hicc1=127 xf_cccurve=gain loop_mode=loop_continuous
            <region> sample=dc.wav amp_veltrack=0 xfin_locc1=0 xfin_hicc1=127 xf_cccurve=gain loop_mode=loop_continuous
            """);

        //Act
        var ccLow = PeakOfNote(instrument, key: 60,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 1, 0));
        var ccMiddle = PeakOfNote(instrument, key: 60,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 1, 64));
        var ccHigh = PeakOfNote(instrument, key: 60,
            setup: s => s.ProcessMidiMessage(0, 0xB0, 1, 127));

        //Assert - a linear pair keeps the summed level constant across the fade.
        ccMiddle.Should().BeApproximately(ccLow, 0.02f);
        ccHigh.Should().BeApproximately(ccLow, 0.02f);
    }

    // ------------------------------------------------------------------ helpers

    private static float PeakOfNote(SfzInstrument instrument, int key, int velocity = 127, Action<SfzSynthesizer> setup = null)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        setup?.Invoke(synthesizer);
        synthesizer.NoteOn(0, key, velocity);
        return RenderPeak(synthesizer, 448);
    }

    private static (float Left, float Right) ChannelsOfNote(SfzInstrument instrument)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 60, 127);

        var left = new float[448];
        var right = new float[448];
        synthesizer.Render(left, right);

        var peakLeft = 0f;
        var peakRight = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            peakLeft = MathF.Max(peakLeft, MathF.Abs(left[i]));
            peakRight = MathF.Max(peakRight, MathF.Abs(right[i]));
        }

        return (peakLeft, peakRight);
    }

    private static float FirstWindowLevel(SfzInstrument instrument, int setCc1)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, setCc1);
        synthesizer.NoteOn(0, 60, 127);
        return RenderPeak(synthesizer, 448);
    }

    private static float ValueReached(SfzInstrument instrument, int velocity, int frames)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, 60, velocity);
        var left = new float[frames];
        synthesizer.Render(left, new float[frames]);

        var peak = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(left[i]));
        }

        return peak;
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

    private static float RenderRmsAfterSkip(SfzSynthesizer synthesizer, int skipFrames, int measureFrames)
    {
        RenderRms(synthesizer, skipFrames);
        return RenderRms(synthesizer, measureFrames);
    }
}

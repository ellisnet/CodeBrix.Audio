using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// Global scope against voice scope, the targets the guide says are modulatable, and what a modulator
/// actually does to the audio.
/// </summary>
public class DecentSamplerModulationScopeTests
{
    [Fact]
    public void a_global_modulator_moves_the_published_parameter()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"4\" scope=\"global\">" + VolumeBinding("set") + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5), () => harness.Instrument.Groups[0].Volume);

        //Assert
        ModulationHarness.Max(trace).Should().BeApproximately(1.0, 0.01);
        ModulationHarness.Min(trace).Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void a_voice_modulator_leaves_the_published_parameter_alone_but_shapes_the_audio()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"2\" scope=\"voice\">" + VolumeBinding("set") + "</lfo>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var audio = ModulationHarness.TraceAudio(
            synthesizer, ModulationHarness.Blocks(0.5),
            () => harness.Instrument.Groups[0].Volume, out var trace);

        //Assert
        trace.Should().AllSatisfy(value => value.Should().Be(1.0));

        // The LFO starts at its centre on note-on, peaks a quarter of a cycle later and troughs at
        // three quarters, so a 2 Hz LFO is loudest at 0.125 s and quietest at 0.375 s.
        var loud = Window(audio, 0.125);
        var quiet = Window(audio, 0.375);

        loud.Should().BeGreaterThan(quiet * 10.0);
    }

    [Fact]
    public void two_voices_carry_their_own_modulation_at_the_same_time()
    {
        //Arrange
        // A voice-scope envelope with a long attack: the note struck first is further up its own
        // envelope than the one struck later, which cannot happen unless the state is per voice.
        using var harness = ModulationHarness.Load(
            "<envelope scope=\"voice\" attack=\"0.4\" sustain=\"1\" attackCurve=\"0\">" +
            VolumeBinding("multiply") + "</envelope>");

        var together = harness.Synthesizer();
        var staggered = harness.Synthesizer();

        //Act
        together.NoteOn(0, 60, 100);
        together.NoteOn(0, 64, 100);
        var (togetherLeft, _) = DecentSamplerRenderProbe.RenderBlocks(
            together, ModulationHarness.Blocks(0.2));

        staggered.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(staggered, ModulationHarness.Blocks(0.15));
        staggered.NoteOn(0, 64, 100);
        var (staggeredLeft, _) = DecentSamplerRenderProbe.RenderBlocks(
            staggered, ModulationHarness.Blocks(0.05));

        //Assert
        // Both synthesizers hold two voices, but the staggered pair is not at the same point of the
        // envelope, so the last window is quieter than the same window of the pair struck together.
        together.ActiveVoiceCount.Should().Be(2);
        staggered.ActiveVoiceCount.Should().Be(2);

        var togetherRms = DecentSamplerRenderProbe.Rms(
            togetherLeft, togetherLeft.Length - 1024, 1024);
        var staggeredRms = DecentSamplerRenderProbe.Rms(
            staggeredLeft, staggeredLeft.Length - 1024, 1024);

        staggeredRms.Should().BeLessThan(togetherRms * 0.8);
    }

    [Fact]
    public void a_global_envelope_restarts_from_zero_on_every_note_on()
    {
        //Arrange
        // MEASURED (round 4, item 51): scope="global" keeps ONE envelope instance and every note-on
        // in the group restarts it from zero, so a voice that is already sounding drops out and
        // re-attacks. The reference read a 9 dB step at the second note-on and a full second attack
        // peaking 0.4 s later.
        using var harness = ModulationHarness.Load(EnvelopeModulator("global"));
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var rising = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.4), () => harness.Instrument.Groups[0].Volume);

        synthesizer.NoteOn(0, 64, 100);
        var restarted = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.4), () => harness.Instrument.Groups[0].Volume);

        //Assert
        // 0.4 s into a 0.5 s attack the measured shape reads 0.98 of full; one block after the restart
        // it is back at the bottom, and 0.4 s later it has climbed the same attack again.
        rising[^1].Should().BeApproximately(0.98, 0.02);
        restarted[0].Should().BeLessThan(0.05);
        restarted[^1].Should().BeApproximately(0.98, 0.02);
    }

    [Fact]
    public void a_global_envelope_interrupts_a_voice_that_is_already_sounding()
    {
        //Arrange
        using var harness = ModulationHarness.Load(EnvelopeModulator("global"));
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (before, _) = DecentSamplerRenderProbe.RenderBlocks(
            synthesizer, ModulationHarness.Blocks(0.4));

        synthesizer.NoteOn(0, 64, 100);
        var (after, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 8);

        //Assert - the sounding voice collapses with the restart instead of being joined by the new one.
        // The window starts one block in, past the click-free gain ramp the collapse itself rides on.
        var loud = DecentSamplerRenderProbe.Rms(before, before.Length - 256, 256);
        var quiet = DecentSamplerRenderProbe.Rms(after, 64, 256);

        synthesizer.ActiveVoiceCount.Should().Be(2);
        quiet.Should().BeLessThan(loud * 0.15);
    }

    [Fact]
    public void a_voice_envelope_leaves_a_sounding_voice_alone_when_a_second_note_starts()
    {
        //Arrange
        // The same preset at scope="voice": every voice carries its own instance, so the first note
        // holds its level and the second one simply adds to it.
        using var harness = ModulationHarness.Load(EnvelopeModulator("voice"));
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var (before, _) = DecentSamplerRenderProbe.RenderBlocks(
            synthesizer, ModulationHarness.Blocks(0.4));

        synthesizer.NoteOn(0, 64, 100);
        var (after, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 8);

        //Assert
        var held = DecentSamplerRenderProbe.Rms(before, before.Length - 256, 256);
        var both = DecentSamplerRenderProbe.Rms(after, 64, 256);

        synthesizer.ActiveVoiceCount.Should().Be(2);
        both.Should().BeGreaterThan(held * 0.99);
        harness.Instrument.Groups[0].Volume.Should().Be(1.0);
    }

    [Fact]
    public void one_note_alone_sounds_the_same_at_either_envelope_scope()
    {
        //Arrange
        // MEASURED (round 4, item 51): the solo trajectories agree to 0.2 dB, which is why round 3
        // could not separate the two scopes at all.
        using var globalFiles = ModulationHarness.Load(EnvelopeModulator("global"));
        using var voiceFiles = ModulationHarness.Load(EnvelopeModulator("voice"));

        var globalScope = globalFiles.Synthesizer();
        var voiceScope = voiceFiles.Synthesizer();

        //Act
        globalScope.NoteOn(0, 60, 100);
        var (globalLeft, _) = DecentSamplerRenderProbe.RenderBlocks(
            globalScope, ModulationHarness.Blocks(1.0));

        voiceScope.NoteOn(0, 60, 100);
        var (voiceLeft, _) = DecentSamplerRenderProbe.RenderBlocks(
            voiceScope, ModulationHarness.Blocks(1.0));

        //Assert
        for (var index = 0; index < globalLeft.Length; index++)
        {
            Math.Abs(globalLeft[index] - voiceLeft[index]).Should().BeLessThan(1e-6f);
        }
    }

    [Fact]
    public void a_modulator_reaches_an_effect_parameter()
    {
        //Arrange
        const string effects =
            "<effects><effect type=\"lowpass\" frequency=\"1000\" resonance=\"0.7\" /></effects>";

        using var harness = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"2\" scope=\"global\">" +
            "<binding type=\"effect\" level=\"instrument\" effectIndex=\"0\" " +
            "parameter=\"FX_FILTER_FREQUENCY\" modBehavior=\"add\" translation=\"linear\" " +
            "translationOutputMin=\"0\" translationOutputMax=\"2000\" /></lfo>",
            extraSections: effects);

        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5),
            () => harness.Instrument.Effects.Effects[0].Frequency ?? 0.0);

        //Assert
        // add on a base of 1000 with the LFO spanning 0 to 2000.
        trace[0].Should().BeApproximately(2000.0, 20.0);
        ModulationHarness.Max(trace).Should().BeApproximately(3000.0, 20.0);
        ModulationHarness.Min(trace).Should().BeApproximately(1000.0, 20.0);
    }

    [Fact]
    public void a_modulator_reaches_the_instruments_own_volume()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiCC number=\"7\" channel=\"1\" scope=\"global\">" +
            "<binding type=\"amp\" level=\"instrument\" parameter=\"AMP_VOLUME\" modBehavior=\"set\" " +
            "translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" /></midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 7, 127);
        var loud = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].InstrumentVolume)[0];

        synthesizer.ProcessMidiMessage(0, 0xB0, 7, 0);
        var silent = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].InstrumentVolume)[0];

        //Assert
        loud.Should().BeApproximately(1.0, 0.005);
        silent.Should().Be(0.0);
    }

    [Fact]
    public void a_modulator_reaches_group_tuning_and_bends_the_pitch()
    {
        //Arrange
        using var harness = ModulationHarness.LoadSine(
            "<midiCC number=\"1\" channel=\"1\" scope=\"global\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_TUNING\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"12\" /></midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, 127);
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 512);

        //Assert
        // Twelve semitones up from the recorded 440 Hz.
        var hertz = DecentSamplerRenderProbe.Frequency(
            left, 4096, 16384, ModulationHarness.SampleRate);
        hertz.Should().BeApproximately(880.0, 5.0);
    }

    [Fact]
    public void a_modulator_binding_at_instrument_level_moves_global_tuning_here()
    {
        //Arrange
        // A PUBLISHED DIVERGENCE. MEASURED (round 3, item 45): a modulator binding at
        // level="instrument" to GLOBAL_TUNING does NOTHING in the reference - +12, +0.09 and -12
        // semitones all left a 440 Hz tone at 440.00 Hz - while AMP_VOLUME and PAN at that level both
        // work there. Appendix B lists GLOBAL_TUNING at instrument level, so this engine honours it.
        using var harness = ModulationHarness.LoadSine(
            "<midiCC number=\"1\" channel=\"1\" scope=\"global\">" +
            "<binding type=\"amp\" level=\"instrument\" parameter=\"GLOBAL_TUNING\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"12\" /></midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, 127);
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 512);

        //Assert - twelve semitones up from the recorded 440 Hz, where the reference stays at 440.
        var hertz = DecentSamplerRenderProbe.Frequency(
            left, 4096, 16384, ModulationHarness.SampleRate);
        hertz.Should().BeApproximately(880.0, 5.0);
    }

    [Fact]
    public void switching_the_modulator_runtime_off_renders_the_preset_as_the_sampler_alone_would()
    {
        //Arrange
        // Two INSTRUMENTS, not two synthesizers over one: a preset's parameter values are instrument
        // state, so a global modulator moves them for everything playing that instrument.
        using var modulatedFiles = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"2\" scope=\"global\">" + VolumeBinding("set") + "</lfo>");
        using var plainFiles = ModulationHarness.Load(
            "<lfo shape=\"sine\" frequency=\"2\" scope=\"global\">" + VolumeBinding("set") + "</lfo>");

        var modulated = modulatedFiles.Synthesizer();
        var plain = plainFiles.Synthesizer(settings => settings.EnableModulators = false);

        //Act
        modulated.NoteOn(0, 60, 100);
        var (modulatedLeft, _) = DecentSamplerRenderProbe.RenderBlocks(modulated, 256);

        plain.NoteOn(0, 60, 100);
        var (plainLeft, _) = DecentSamplerRenderProbe.RenderBlocks(plain, 256);

        //Assert
        DecentSamplerRenderProbe.Rms(plainLeft).Should().BeGreaterThan(
            DecentSamplerRenderProbe.Rms(modulatedLeft));
        plainLeft.Should().AllSatisfy(sample => Math.Abs(sample).Should().BeGreaterThan(0.2f));
    }

    // The item-51 modulator: the measured attack, decay, sustain and release, setting the group's
    // volume over the full 0-to-1 range.
    private static string EnvelopeModulator(string scope) =>
        "<envelope attack=\"0.5\" decay=\"0.5\" sustain=\"0.5\" release=\"1.0\" scope=\"" + scope +
        "\" modAmount=\"1.0\" modBehavior=\"set\">" + VolumeBinding("set") + "</envelope>";

    private static string VolumeBinding(string behavior) =>
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"" + behavior + "\" translation=\"linear\" translationOutputMin=\"0\" " +
        "translationOutputMax=\"1\" />";

    // The root-mean-square of a short window centred on a moment in the render.
    private static double Window(float[] audio, double seconds)
    {
        const int length = 512;
        var centre = (int)(seconds * ModulationHarness.SampleRate);
        var start = Math.Clamp(centre - (length / 2), 0, audio.Length - length);

        return DecentSamplerRenderProbe.Rms(audio, start, length);
    }
}

using System;
using System.IO;
using System.IO.Compression;
using CodeBrix.Audio.ModestSynth.Integration;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The oscillator adapter, driven the way a consumer drives it: a synthetic preset holding one
/// <c>oscillator</c> group, played through <c>DecentSamplerSynthesizer</c> after
/// <c>ModestSynth.Register()</c>.
/// </summary>
/// <remarks>
/// Every render is block-aligned and every level is read as RMS over a window well after the note-on,
/// because a peak cannot tell a fade from a steady level and a partial block leaks the previous call's
/// audio into the next one.
/// </remarks>
public class ModestVoiceSourceTests
{
    private const int Window = Spectrum.BlockLength;

    public ModestVoiceSourceTests() => ModestSynth.Register();

    [Theory]
    [InlineData("sine")]
    [InlineData("saw")]
    [InlineData("square")]
    [InlineData("triangle")]
    [InlineData("pluck1")]
    [InlineData("harmonic")]
    [InlineData("fm6op")]
    public void every_waveform_plays_at_the_key_it_was_struck_at(string waveform)
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"" + waveform + "\""));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 69, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        RenderProbe.Frequency(left, Window).Should().BeApproximately(440.0, 4.0);
    }

    [Fact]
    public void an_oscillator_zone_sits_where_the_reference_puts_one_relative_to_a_sample_zone()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        fixtures.WriteSineWav("Samples/a4.wav", 440.0, 1f);

        using DecentSamplerInstrument oscillator =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"sine\""), "oscillator.dspreset");
        using DecentSamplerInstrument sampled = fixtures.LoadPreset(
            PresetXml.Wrap(null,
                "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
                "      <sample path=\"Samples/a4.wav\" rootNote=\"69\" loNote=\"0\" hiNote=\"127\" />\n" +
                "    </group>"),
            "sampled.dspreset");

        //Act
        double oscillatorRms = LevelOf(oscillator);
        double sampleRms = LevelOf(sampled);

        //Assert
        // Measured (items 12 and 13): -8.81 dBFS through the reference standalone's fixed -5.28 dB
        // output trim, which is 0.52 dB below the full-scale sine a sample zone plays.
        RenderProbe.Decibels(oscillatorRms, sampleRms).Should().BeApproximately(-0.52, 0.05);
        ModestVoiceSource.ReferenceOscillatorGain.Should().BeApproximately(0.9419, 0.0001);
    }

    [Theory]
    [InlineData("harmonic", 0.0)]
    [InlineData("wavetable", 0.0)]
    [InlineData("fm6op", -4.44)]
    [InlineData("noise", -2.68)]
    public void the_measured_level_of_each_waveform_relative_to_a_sine_is_preserved(
        string waveform, double expectedDecibels)
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument reference =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"sine\""), "sine.dspreset");
        using DecentSamplerInstrument other =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"" + waveform + "\""), "other.dspreset");

        //Act
        double difference = RenderProbe.Decibels(LevelOf(other), LevelOf(reference));

        //Assert
        difference.Should().BeApproximately(expectedDecibels, 0.1);
    }

    [Fact]
    public void the_groups_envelope_gates_the_oscillator()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.Wrap(null,
            "    <group attack=\"0.25\" decay=\"0\" sustain=\"1\" release=\"0.25\">\n" +
            "      <oscillator waveform=\"sine\" />\n    </group>"));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 69, 127);
        var (attack, _) = RenderProbe.RenderSeconds(synthesizer, 0.5);
        synthesizer.NoteOff(0, 69);
        var (release, _) = RenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert
        double early = RenderProbe.Rms(attack, 0, 1024);
        double half = RenderProbe.Rms(attack, 6000, 1024);
        double open = RenderProbe.Rms(attack, 20000, 1024);

        early.Should().BeLessThan(half);
        half.Should().BeLessThan(open);
        open.Should().BeApproximately(0.666, 0.02);

        RenderProbe.Rms(release, 0, 1024).Should().BeLessThan(open);
        RenderProbe.Rms(release, 16000, 1024).Should().BeApproximately(0.0, 1e-5);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void an_oscillator_wavetable_position_binding_moves_the_timbre()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        fixtures.WriteWavetableWav("Samples/table.wav");

        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.Wrap(
            PresetXml.Knob("OSCILLATOR_WAVETABLE_POSITION"),
            "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
            "      <oscillator waveform=\"wavetable\" wavetableFile=\"Samples/table.wav\"\n" +
            "                  wavetableFrameSize=\"1024\" wavetablePosition=\"0\" />\n    </group>"));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (first, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        double dark = Spectrum.PowerCentroid(first, Window);

        instrument.Controls[0].SetValue(1.0);
        var (second, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        double bright = Spectrum.PowerCentroid(second, Window);

        //Assert
        synthesizer.Problems.Should().BeEmpty();
        dark.Should().BeApproximately(112.5, 5.0);
        bright.Should().BeGreaterThan(dark * 1.8);
    }

    [Fact]
    public void an_oscillator_harmonic_partial_binding_adds_the_partial_it_names()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.Wrap(
            PresetXml.Knob("OSCILLATOR_HARMONIC_PARTIAL_3_LEVEL"),
            "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
            "      <oscillator waveform=\"harmonic\" harmonicPartial1Level=\"1.0\" />\n    </group>"));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        // MIDI 45 is 110 Hz, which lands on bin 19 of an 8,192-point block at 48 kHz.
        int fundamental = 19;

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (before, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        double[] quiet = Spectrum.WindowedMagnitudes(before, Window);

        instrument.Controls[0].SetValue(1.0);
        var (after, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        double[] loud = Spectrum.WindowedMagnitudes(after, Window);

        //Assert
        quiet[fundamental * 3].Should().BeLessThan(1e-4);
        loud[fundamental * 3].Should().BeGreaterThan(0.1);
        loud[fundamental].Should().BeApproximately(quiet[fundamental], quiet[fundamental] * 0.02);
    }

    [Fact]
    public void fm6op_velocity_sensitivity_makes_a_hard_note_brighter_without_making_it_louder()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.OneOscillator(
            "waveform=\"fm6op\" fmAlgorithm=\"1\" fmOp1Level=\"1.0\" fmOp2Level=\"1.0\"" +
            " fmOp2Ratio=\"2.0\" fmOp2VelocitySensitivity=\"7\"",
            "ampVelTrack=\"0\""));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (hard, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        synthesizer.NoteOffAll(true);
        RenderProbe.RenderSeconds(synthesizer, 0.1);

        synthesizer.NoteOn(0, 45, 10);
        var (soft, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        Spectrum.PowerCentroid(hard, Window).Should().BeGreaterThan(
            Spectrum.PowerCentroid(soft, Window) * 4.0);
        RenderProbe.Rms(hard, Window, Window).Should().BeApproximately(
            RenderProbe.Rms(soft, Window, Window), 0.1);
    }

    [Fact]
    public void a_bare_fm6op_zone_is_the_pure_sine_the_reference_renders()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"fm6op\""));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        double[] magnitudes = Spectrum.WindowedMagnitudes(left, Window);

        //Assert
        // The guide's attribute table gives every operator a level of 1.0, but the reference plays a
        // pure sine, so only operator 1 sounds; nothing shows up at twice the fundamental.
        Spectrum.PeakBin(magnitudes).Should().Be(19);
        magnitudes[38].Should().BeLessThan(magnitudes[19] * 0.001);
    }

    [Fact]
    public void a_fm6op_voice_ends_on_its_operator_release_not_the_groups()
    {
        //Arrange
        // MEASURED (round 4, item 56): every six-operator case was written with a GROUP release of
        // 3.0 s, and with operator releases of 0.02 s the sound was at the silence floor half a
        // second after the note-off. A fm6op voice ends when its operators' envelopes end.
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.Wrap(
            null,
            "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"3.0\">\n" +
            "      <oscillator waveform=\"fm6op\" fmAlgorithm=\"32\" fmOp1Attack=\"0.001\"\n" +
            "                  fmOp1Decay=\"0\" fmOp1Sustain=\"1\" fmOp1Release=\"0.02\" />\n" +
            "    </group>"));

        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (held, _) = RenderProbe.RenderSeconds(synthesizer, 0.5);

        synthesizer.NoteOff(0, 45);
        var (tail, _) = RenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert - a 3 s group release would still be four fifths of the way up here.
        RenderProbe.Rms(held, Window, Window).Should().BeGreaterThan(0.05);
        RenderProbe.Peak(tail, tail.Length - Window, Window).Should().Be(0.0);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void a_long_fm6op_operator_release_outlives_a_short_group_release()
    {
        //Arrange - the other side of the same rule: the operators own the tail either way.
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(PresetXml.Wrap(
            null,
            "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.02\">\n" +
            "      <oscillator waveform=\"fm6op\" fmAlgorithm=\"32\" fmOp1Attack=\"0.001\"\n" +
            "                  fmOp1Decay=\"0\" fmOp1Sustain=\"1\" fmOp1Release=\"1.5\" />\n" +
            "    </group>"));

        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        RenderProbe.RenderSeconds(synthesizer, 0.3);

        synthesizer.NoteOff(0, 45);
        var (tail, _) = RenderProbe.RenderSeconds(synthesizer, 0.5);

        //Assert - half a second after the key came up the 1.5 s operator release is still sounding,
        //where the group's own 0.02 s release would have cut it long ago.
        RenderProbe.Rms(tail, tail.Length - Window, Window).Should().BeGreaterThan(1e-4);
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void the_same_events_render_the_same_bytes()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument = fixtures.LoadPreset(
            PresetXml.OneOscillator("waveform=\"noise\" randomPhase=\"true\""));

        //Act
        float[] first = RenderChord(instrument);
        float[] second = RenderChord(instrument);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void a_wavetable_file_is_resolved_relative_to_the_preset()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        fixtures.WriteWavetableWav("Samples/deep/table.wav");

        using DecentSamplerInstrument instrument = fixtures.LoadPreset(
            PresetXml.OneOscillator(
                "waveform=\"wavetable\" wavetableFile=\"Samples/deep/table.wav\"" +
                " wavetableFrameSize=\"1024\" wavetablePosition=\"1\""),
            "nested/instrument.dspreset");
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        // The path is relative to the PRESET's own folder, which here is nested/, so it does not
        // resolve; the oscillator falls back to a sine and says so rather than going silent.
        Spectrum.PowerCentroid(left, Window).Should().BeApproximately(110.0, 5.0);
        SourceProblemOf(instrument, synthesizer).Should().Contain("Missing file reference");
    }

    [Fact]
    public void a_wavetable_inside_a_library_archive_is_read_through_the_container()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        fixtures.WriteWavetableWav("build/Samples/table.wav");
        fixtures.WritePreset(
            PresetXml.OneOscillator(
                "waveform=\"wavetable\" wavetableFile=\"Samples/table.wav\"" +
                " wavetableFrameSize=\"1024\" wavetablePosition=\"1\""),
            "build/archived.dspreset");

        string archive = Path.Combine(fixtures.Directory, "library.dslibrary");
        ZipFile.CreateFromDirectory(Path.Combine(fixtures.Directory, "build"), archive);

        using DecentSamplerInstrument instrument = DecentSamplerInstrument.Load(archive);
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 45, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert
        synthesizer.Problems.Should().BeEmpty();
        Spectrum.PowerCentroid(left, Window).Should().BeGreaterThan(200.0);
    }

    [Fact]
    public void rendering_an_oscillator_zone_allocates_nothing_once_the_voice_is_running()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"harmonic\" harmonicPartial1Level=\"1.0\""));
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);
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

    private static double LevelOf(DecentSamplerInstrument instrument)
    {
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 69, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        return RenderProbe.Rms(left, Window, Window);
    }

    private static float[] RenderChord(DecentSamplerInstrument instrument)
    {
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        synthesizer.NoteOn(0, 67, 100);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.3);
        return left;
    }

    // The adapter records why a wavetable did not load on the source itself; the engine's Problems
    // list only carries what it found while BUILDING the zone, so the two are read together.
    private static string SourceProblemOf(
        DecentSamplerInstrument instrument, DecentSamplerSynthesizer synthesizer)
    {
        ModestVoiceSource source = new ModestVoiceSource(
            new CodeBrix.Audio.Synth.DecentSampler.Engine.OscillatorContext(
                instrument, instrument.Zones[0], synthesizer.SampleRate, "wavetable"));

        source.Start(45, 100, 110.0);
        return source.Problem ?? string.Empty;
    }
}

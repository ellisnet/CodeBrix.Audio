using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The whole add-on through the front door: <c>ModestSynth.Register()</c>, then a preset naming each
/// documented waveform and each documented effect type, played through
/// <c>DecentSamplerSynthesizer</c>.
/// </summary>
/// <remarks>
/// This is the acceptance test behind the sentence "reference the package, call Register() before you
/// load, and every oscillator and effect a preset asks for is there". The matching "without it"
/// cases live in <see cref="ModestSynthRegistrationTests"/>, which builds an isolated registry.
/// </remarks>
public class ModestSynthEndToEndTests
{
    private const int Window = Spectrum.BlockLength;

    public ModestSynthEndToEndTests() => ModestSynth.Register();

    [Theory]
    [InlineData("sine")]
    [InlineData("saw")]
    [InlineData("square")]
    [InlineData("triangle")]
    [InlineData("noise")]
    [InlineData("white_noise")]
    [InlineData("pluck1")]
    [InlineData("wavetable")]
    [InlineData("harmonic")]
    [InlineData("fm6op")]
    [InlineData("formant")]
    public void every_registered_waveform_sounds_inside_a_preset(string waveform)
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        fixtures.WriteWavetableWav("Samples/table.wav");

        string attributes = waveform == "wavetable"
            ? "waveform=\"wavetable\" wavetableFile=\"Samples/table.wav\" wavetableFrameSize=\"1024\""
            : "waveform=\"" + waveform + "\"";

        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator(attributes));

        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 69, 110);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);

        //Assert - the group is audible, and nothing asked for a package that is not there.
        RenderProbe.Rms(left, Window, Window).Should().BeGreaterThan(0.01);
        instrument.Problems.Should().NotContain(problem => problem.Contains("needs CodeBrix.Audio"));
    }

    [Theory]
    [InlineData("<effect type=\"phaser\" mix=\"0.5\" modDepth=\"0.0\" modRate=\"0.0\" " +
                "centerFrequency=\"600\" feedback=\"0.0\" />")]
    [InlineData("<effect type=\"pitch_shift\" pitchShift=\"7\" />")]
    [InlineData("<effect type=\"wave_folder\" drive=\"6\" />")]
    [InlineData("<effect type=\"wave_shaper\" drive=\"12\" driveBoost=\"1\" highQuality=\"true\" />")]
    [InlineData("<effect type=\"stereo_simulator\" algorithm=\"lauridsen\" width=\"1\" />")]
    [InlineData("<effect type=\"bit_crusher\" bitDepth=\"3\" sampleRateReduction=\"8\" />")]
    [InlineData("<effect type=\"gate\" amount=\"1\" />")]
    public void every_registered_effect_changes_what_a_preset_plays(string effect)
    {
        //Arrange - the same oscillator group with and without the chain.
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();

        using DecentSamplerInstrument dry =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"saw\""), "dry.dspreset");

        using DecentSamplerInstrument wet = fixtures.LoadPreset(
            PresetXml.Wrap(
                null,
                "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
                "      <oscillator waveform=\"saw\" />\n    </group>",
                "  <effects>\n    " + effect + "\n  </effects>\n"),
            "wet.dspreset");

        //Act
        float[] dryLeft = Play(dry);
        float[] wetLeft = Play(wet);

        //Assert - the effect was built, and it did something.
        wet.Problems.Should().NotContain(problem => problem.Contains("needs CodeBrix.Audio"));
        wet.UnsupportedFeatures.Should().BeEmpty();
        Difference(dryLeft, wetLeft).Should().BeGreaterThan(1e-4);
    }

    [Fact]
    public void an_oscillator_and_an_add_on_effect_play_together_in_one_preset()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();

        using DecentSamplerInstrument instrument = fixtures.LoadPreset(
            PresetXml.Wrap(
                null,
                "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
                "      <oscillator waveform=\"fm6op\" fmAlgorithm=\"1\" fmOp2Level=\"0.5\" />\n" +
                "    </group>",
                "  <effects>\n" +
                "    <effect type=\"bit_crusher\" bitDepth=\"4\" sampleRateReduction=\"4\" />\n" +
                "    <effect type=\"phaser\" mix=\"0.5\" modRate=\"1\" modDepth=\"0.5\" " +
                "centerFrequency=\"800\" />\n" +
                "  </effects>\n"));

        //Act
        float[] left = Play(instrument);

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.UnsupportedFeatures.Should().BeEmpty();
        RenderProbe.Rms(left, Window, Window).Should().BeGreaterThan(0.005);
    }

    private static float[] Play(DecentSamplerInstrument instrument)
    {
        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 57, 110);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        return left;
    }

    private static double Difference(float[] first, float[] second)
    {
        double total = 0.0;

        for (int i = Window; i < first.Length && i < second.Length; i++)
        {
            total += Math.Abs(first[i] - second[i]);
        }

        return total / Math.Max(1, first.Length - Window);
    }
}

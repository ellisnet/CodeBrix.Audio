using System;
using CodeBrix.Audio.ModestSynth.Integration;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The <c>OSCILLATOR_*</c> parameter surface of the adapter, driven directly rather than through a
/// binding - which is how a modulator, or a host with its own control surface, reaches it.
/// </summary>
public class ModestVoiceSourceParameterTests
{
    [Theory]
    [InlineData("pluck1", "OSCILLATOR_DAMPING", 0.8)]
    [InlineData("pluck1", "OSCILLATOR_PLUCK_TYPE", 0.25)]
    [InlineData("wavetable", "OSCILLATOR_WAVETABLE_POSITION", 0.75)]
    [InlineData("wavetable", "OSCILLATOR_WAVETABLE_FRAME_INTERPOLATION", 0.0)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_NUM_PARTIALS", 24.0)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_TILT", -0.5)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_ODD_EVEN_BALANCE", 0.25)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_NORMALIZATION", 1.0)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_PARTIAL_7_LEVEL", 0.6)]
    [InlineData("harmonic", "OSCILLATOR_HARMONIC_PARTIAL_64_LEVEL", 0.1)]
    [InlineData("fm6op", "OSCILLATOR_FM_ALGORITHM", 17.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_RATIO", 3.5)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP2_DETUNE", 5.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP3_MODE", 1.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP3_FIXED_FREQ", 220.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP4_LEVEL", 0.8)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP5_VELOCITY_SENSITIVITY", 6.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP6_FEEDBACK", 0.4)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_ATTACK", 0.2)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_DECAY", 0.3)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_SUSTAIN", 0.4)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_RELEASE", 0.5)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP1_EG_TYPE", 1.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP2_EG_RATE1", 80.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP2_EG_RATE4", 40.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP2_EG_LEVEL1", 70.0)]
    [InlineData("fm6op", "OSCILLATOR_FM_OP2_EG_LEVEL4", 20.0)]
    public void every_oscillator_parameter_round_trips(string waveform, string parameter, double value)
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"" + waveform + "\""));
        ModestVoiceSource source = SourceFor(instrument, waveform);

        //Act
        bool written = source.TrySetParameter(parameter, value);
        bool read = source.TryGetParameter(parameter, out double readBack);

        //Assert
        written.Should().BeTrue();
        read.Should().BeTrue();
        readBack.Should().BeApproximately(value, 1e-9);
    }

    [Fact]
    public void a_parameter_that_belongs_to_another_waveform_is_not_answered()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"sine\""));
        ModestVoiceSource source = SourceFor(instrument, "sine");

        //Act
        //Assert
        source.TrySetParameter("OSCILLATOR_DAMPING", 0.5).Should().BeFalse();
        source.TryGetParameter("OSCILLATOR_HARMONIC_TILT", out _).Should().BeFalse();
        source.TrySetParameter("OSCILLATOR_FM_OP2_LEVEL", 1.0).Should().BeFalse();
        source.TrySetParameter("NOT_A_PARAMETER", 1.0).Should().BeFalse();
        source.TrySetParameter(null, 1.0).Should().BeFalse();
    }

    [Fact]
    public void a_parameter_name_is_matched_without_case_or_punctuation()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"harmonic\""));
        ModestVoiceSource source = SourceFor(instrument, "harmonic");

        //Act
        source.TrySetParameter("oscillatorHarmonicTilt", 0.5).Should().BeTrue();
        source.TryGetParameter("OSCILLATOR-HARMONIC-TILT", out double dashed).Should().BeTrue();
        source.TryGetParameter("harmonic tilt", out double bare).Should().BeTrue();

        //Assert
        dashed.Should().Be(0.5);
        bare.Should().Be(0.5);
    }

    [Fact]
    public void the_waveform_parameter_reads_back_the_shape_being_generated()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"saw\""));
        ModestVoiceSource source = SourceFor(instrument, "saw");

        //Act
        source.TryGetParameter("OSCILLATOR_WAVEFORM", out double waveform);

        //Assert
        waveform.Should().Be((double)(int)CodeBrix.Audio.Synth.DecentSampler.Model.DecentSamplerWaveform.Saw);
        source.Waveform.Should().Be(Oscillators.ModestWaveform.Saw);
    }

    [Fact]
    public void a_waveform_written_through_the_parameter_set_is_generated_at_the_next_note()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"sine\""));
        ModestVoiceSource source = SourceFor(instrument, "sine");

        //Act
        source.TrySetParameter(
            "OSCILLATOR_WAVEFORM",
            (int)CodeBrix.Audio.Synth.DecentSampler.Model.DecentSamplerWaveform.Square);
        source.Start(60, 100, 220.0);

        //Assert
        source.Waveform.Should().Be(Oscillators.ModestWaveform.Square);
    }

    [Fact]
    public void a_source_is_mono_and_ends_when_the_waveform_does()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"pluck1\" damping=\"0\""));
        ModestVoiceSource source = SourceFor(instrument, "pluck1");

        float[] left = new float[64];
        float[] right = new float[64];

        //Act
        source.Start(96, 100, 2093.0);
        source.IsStereo.Should().BeFalse();
        source.IsFinished.Should().BeFalse();

        bool rendering = true;
        int blocks = 0;

        while (rendering && blocks < 48000 / 64 * 30)
        {
            rendering = source.Render(left, right, left.Length);
            blocks++;
        }

        //Assert
        rendering.Should().BeFalse();
        source.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void a_null_context_is_rejected()
    {
        //Arrange
        //Act
        Action act = () => new ModestVoiceSource(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static ModestVoiceSource SourceFor(DecentSamplerInstrument instrument, string waveform)
    {
        ModestVoiceSource source = new ModestVoiceSource(
            new OscillatorContext(instrument, instrument.Zones[0], Spectrum.SampleRate, waveform));

        source.Start(60, 100, 261.63);
        return source;
    }
}
